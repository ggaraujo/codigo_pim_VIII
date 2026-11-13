/* =============================================================================
   02 — Functions e views (seção 5.2 do PIM)
   ============================================================================= */
SET NOCOUNT ON;
-- Obrigatórios para índices filtrados: procedures e triggers guardam estas
-- opções no momento da criação.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Função escalar determinística: formata o protocolo exibido ao cliente.
-- Não acessa tabelas, então pode ser usada sem custo relevante por linha.
CREATE OR ALTER FUNCTION unifica.fn_FormatarProtocolo (@Ano int, @Numero int)
RETURNS varchar(20)
WITH SCHEMABINDING
AS
BEGIN
    RETURN CONCAT('OS-', @Ano, '-', RIGHT(CONCAT('000000', @Numero), 6));
END;
GO

-- Função de tabela em linha (inline TVF): classifica a situação do SLA.
-- Usada com CROSS APPLY, é expandida pelo otimizador como parte da consulta,
-- o que evita o custo de chamar uma função escalar linha a linha.
CREATE OR ALTER FUNCTION unifica.fn_SituacaoSla
(
    @AbertaEmUtc datetime2(0),
    @PrazoSlaUtc datetime2(0),
    @StatusId    tinyint,
    @AgoraUtc    datetime2(0)
)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT Situacao = CASE
        WHEN @StatusId IN (4, 5, 6) THEN 'Concluida'
        WHEN @AgoraUtc > @PrazoSlaUtc THEN 'Violado'
        -- "Em risco" quando 80% do tempo total do SLA já foi consumido.
        WHEN DATEDIFF_BIG(SECOND, @AbertaEmUtc, @AgoraUtc) * 100
             >= DATEDIFF_BIG(SECOND, @AbertaEmUtc, @PrazoSlaUtc) * 80 THEN 'EmRisco'
        ELSE 'NoPrazo'
    END;
GO

-- Consumo mensal de horas de um contrato (relatório do gestor de contas).
CREATE OR ALTER FUNCTION unifica.fn_ConsumoContrato (@ContratoId int)
RETURNS TABLE
AS
RETURN
    SELECT  Mes   = DATEFROMPARTS(YEAR(a.DataTrabalho), MONTH(a.DataTrabalho), 1),
            Horas = SUM(a.Horas),
            Ordens = COUNT(DISTINCT a.OrdemId)
    FROM    unifica.Apontamento AS a
    JOIN    unifica.OrdemServico AS o ON o.OrdemId = a.OrdemId
    WHERE   o.ContratoId = @ContratoId
    GROUP BY DATEFROMPARTS(YEAR(a.DataTrabalho), MONTH(a.DataTrabalho), 1);
GO

-- View das ordens em aberto: simplifica consultas e é o único objeto de leitura
-- liberado ao perfil de relatórios (LESSA, 2026, p. 97).
CREATE OR ALTER VIEW unifica.vw_OrdensEmAberto
AS
SELECT  o.OrdemId,
        o.Protocolo,
        o.ClienteId,
        Cliente = c.RazaoSocial,
        o.Titulo,
        o.Prioridade,
        o.StatusId,
        Status  = s.Nome,
        o.AbertaEmUtc,
        o.PrazoSlaUtc,
        sla.Situacao
FROM    unifica.OrdemServico AS o
JOIN    unifica.Cliente      AS c ON c.ClienteId = o.ClienteId
JOIN    unifica.StatusOrdem  AS s ON s.StatusId  = o.StatusId
CROSS APPLY unifica.fn_SituacaoSla(o.AbertaEmUtc, o.PrazoSlaUtc, o.StatusId, SYSUTCDATETIME()) AS sla
WHERE   o.StatusId IN (1, 2, 3);
GO
