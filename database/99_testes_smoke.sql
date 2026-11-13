/* =============================================================================
   99 — Testes de fumaça do banco. Executado pelo pipeline de CI contra um
   SQL Server em contêiner, logo após os scripts 01 a 07. Qualquer THROW aqui
   faz o job falhar.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

DECLARE @Agora datetime2(0) = SYSUTCDATETIME();
DECLARE @OrdemId bigint, @Versao binary(8), @Alterou bit, @SaldoAntes decimal(9,2), @SaldoDepois decimal(9,2);
DECLARE @Usuario nvarchar(100) = N'ana.souza@alunostech.example';

-- 1) Abertura gera protocolo e histórico inicial.
EXEC unifica.usp_AbrirOrdem @ContratoId = 2, @Titulo = N'Lentidão no ERP', @Prioridade = 3,
     @AbertaEmUtc = @Agora, @PrazoSlaUtc = '2099-01-01', @Usuario = @Usuario, @OrdemId = @OrdemId OUTPUT;

IF NOT EXISTS (SELECT 1 FROM unifica.OrdemServico WHERE OrdemId = @OrdemId AND Protocolo LIKE 'OS-____-______')
    THROW 59001, N'Falha: protocolo não gerado no formato esperado.', 1;
IF (SELECT COUNT(*) FROM unifica.HistoricoStatusOrdem WHERE OrdemId = @OrdemId) <> 1
    THROW 59002, N'Falha: histórico inicial não registrado pelo trigger.', 1;

-- 2) Versão desatualizada não altera (concorrência otimista).
EXEC unifica.usp_AlterarStatusOrdem @OrdemId, 2, 0x0000000000000000, @Usuario, @Alterou OUTPUT;
IF @Alterou <> 0 THROW 59003, N'Falha: alterou com versão incorreta.', 1;

-- 3) Transição não cadastrada (Aberta → Encerrada) não altera.
SELECT @Versao = Versao FROM unifica.OrdemServico WHERE OrdemId = @OrdemId;
EXEC unifica.usp_AlterarStatusOrdem @OrdemId, 5, @Versao, @Usuario, @Alterou OUTPUT;
IF @Alterou <> 0 THROW 59004, N'Falha: transição inválida foi aceita.', 1;

-- 4) Transição válida com a versão correta.
EXEC unifica.usp_AlterarStatusOrdem @OrdemId, 2, @Versao, @Usuario, @Alterou OUTPUT;
IF @Alterou <> 1 THROW 59005, N'Falha: transição válida rejeitada.', 1;
IF (SELECT TOP (1) AlteradoPor FROM unifica.HistoricoStatusOrdem WHERE OrdemId = @OrdemId ORDER BY HistoricoId DESC) <> @Usuario
    THROW 59006, N'Falha: usuário não propagado ao histórico via SESSION_CONTEXT.', 1;

-- 5) Apontamento debita o saldo do contrato na mesma transação e gera auditoria.
SELECT @SaldoAntes = SaldoHoras FROM unifica.Contrato WHERE ContratoId = 2;
EXEC unifica.usp_RegistrarApontamento @OrdemId, 2.5, N'Análise de índices', @Agora, @Usuario;
SELECT @SaldoDepois = SaldoHoras FROM unifica.Contrato WHERE ContratoId = 2;
IF @SaldoDepois <> @SaldoAntes - 2.5 THROW 59007, N'Falha: saldo não debitado.', 1;
IF NOT EXISTS (SELECT 1 FROM unifica.LogAuditoria WHERE Tabela = N'Contrato' AND Chave = N'2')
    THROW 59008, N'Falha: auditoria do contrato não registrada.', 1;

-- 6) Saldo insuficiente é recusado e nada é gravado (atomicidade).
DECLARE @Qtde int = (SELECT COUNT(*) FROM unifica.Apontamento WHERE OrdemId = @OrdemId);
BEGIN TRY
    EXEC unifica.usp_RegistrarApontamento @OrdemId, 12, N'Teste', @Agora, @Usuario;   -- 1ª vez: ok
    EXEC unifica.usp_RegistrarApontamento @OrdemId, 12, N'Teste', @Agora, @Usuario;
    EXEC unifica.usp_RegistrarApontamento @OrdemId, 12, N'Teste', @Agora, @Usuario;
    EXEC unifica.usp_RegistrarApontamento @OrdemId, 12, N'Teste', @Agora, @Usuario;
    EXEC unifica.usp_RegistrarApontamento @OrdemId, 12, N'Teste', @Agora, @Usuario;
    EXEC unifica.usp_RegistrarApontamento @OrdemId, 12, N'Teste', @Agora, @Usuario;
    EXEC unifica.usp_RegistrarApontamento @OrdemId, 12, N'Teste', @Agora, @Usuario;   -- excede 80 h
    THROW 59009, N'Falha: saldo negativo aceito.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 50003 THROW;
END CATCH;
IF (SELECT SaldoHoras FROM unifica.Contrato WHERE ContratoId = 2) < 0
    THROW 59010, N'Falha: saldo ficou negativo.', 1;
IF (SELECT COUNT(*) FROM unifica.Apontamento WHERE OrdemId = @OrdemId) <> @Qtde + 6
    THROW 59011, N'Falha: apontamento recusado deixou registro parcial.', 1;

-- 7) Exclusão de apontamento é bloqueada pelo trigger INSTEAD OF.
BEGIN TRY
    DELETE FROM unifica.Apontamento WHERE OrdemId = @OrdemId;
    THROW 59012, N'Falha: exclusão de apontamento permitida.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 50010 THROW;
END CATCH;

-- 8) Listagem paginada devolve o total.
DECLARE @Lista TABLE (OrdemId bigint, Protocolo varchar(20), Cliente nvarchar(150), Titulo nvarchar(150),
                      Prioridade tinyint, StatusId tinyint, AbertaEmUtc datetime2(0), PrazoSlaUtc datetime2(0),
                      Situacao varchar(10), TotalItens int);
INSERT INTO @Lista EXEC unifica.usp_ListarOrdens @StatusId = 2, @Pagina = 1, @TamanhoPagina = 10;
IF NOT EXISTS (SELECT 1 FROM @Lista WHERE OrdemId = @OrdemId AND TotalItens >= 1)
    THROW 59013, N'Falha: listagem paginada não retornou a ordem.', 1;

PRINT N'Testes de fumaça do banco: OK';
GO
