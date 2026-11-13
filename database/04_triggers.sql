/* =============================================================================
   04 — Triggers (seção 5.3 do PIM)
   Todos os triggers tratam conjuntos de linhas (inserted/deleted), pois um
   único UPDATE pode afetar várias ordens.
   ============================================================================= */
SET NOCOUNT ON;
-- Obrigatórios para índices filtrados: procedures e triggers guardam estas
-- opções no momento da criação.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- AFTER INSERT/UPDATE: grava o histórico de status da OS.
-- O usuário vem do SESSION_CONTEXT definido pelas procedures; se o comando vier
-- de fora delas, registra o login do banco.
CREATE OR ALTER TRIGGER unifica.trg_OrdemServico_Historico
ON unifica.OrdemServico
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted)
        RETURN;

    INSERT INTO unifica.HistoricoStatusOrdem (OrdemId, StatusAnterior, StatusNovo, AlteradoPor)
    SELECT  i.OrdemId,
            d.StatusId,
            i.StatusId,
            COALESCE(CAST(SESSION_CONTEXT(N'usuario') AS nvarchar(100)), ORIGINAL_LOGIN())
    FROM    inserted AS i
    LEFT JOIN deleted AS d ON d.OrdemId = i.OrdemId
    WHERE   d.OrdemId IS NULL              -- inserção (status inicial)
       OR   d.StatusId <> i.StatusId;      -- mudança de status
END;
GO

-- INSTEAD OF DELETE: apontamentos não podem ser apagados — já debitaram o saldo
-- do contrato e servem de base para faturamento. Correções são feitas por estorno.
CREATE OR ALTER TRIGGER unifica.trg_Apontamento_ImpedirExclusao
ON unifica.Apontamento
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 50010, N'[UNIFICA:APONTAMENTO] Apontamentos não podem ser excluídos; registre um estorno.', 1;
END;
GO

-- AFTER INSERT: barra apontamento em OS finalizada mesmo que o INSERT não venha
-- da procedure (defesa em profundidade). O THROW desfaz o comando que disparou o trigger.
CREATE OR ALTER TRIGGER unifica.trg_Apontamento_ValidarOrdem
ON unifica.Apontamento
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1
               FROM   inserted AS i
               JOIN   unifica.OrdemServico AS o ON o.OrdemId = i.OrdemId
               WHERE  o.StatusId IN (5, 6))
    BEGIN
        THROW 50011, N'[UNIFICA:STATUS] Não é permitido apontar horas em ordem encerrada ou cancelada.', 1;
    END
END;
GO

-- AFTER UPDATE: auditoria das alterações de saldo e vigência dos contratos (LGPD/
-- governança). Os valores anterior e novo são gravados em JSON.
CREATE OR ALTER TRIGGER unifica.trg_Contrato_Auditoria
ON unifica.Contrato
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO unifica.LogAuditoria (Tabela, Operacao, Chave, DadosAnteriores, DadosNovos, Usuario)
    SELECT  N'Contrato', 'U', CAST(i.ContratoId AS nvarchar(50)),
            (SELECT d.SaldoHoras, d.VigenciaFim, d.Ativo FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
            (SELECT i.SaldoHoras, i.VigenciaFim, i.Ativo FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
            COALESCE(CAST(SESSION_CONTEXT(N'usuario') AS nvarchar(100)), ORIGINAL_LOGIN())
    FROM    inserted AS i
    JOIN    deleted  AS d ON d.ContratoId = i.ContratoId
    WHERE   d.SaldoHoras  <> i.SaldoHoras
       OR   d.VigenciaFim <> i.VigenciaFim
       OR   d.Ativo       <> i.Ativo;
END;
GO
