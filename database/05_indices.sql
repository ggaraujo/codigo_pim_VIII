/* =============================================================================
   05 — Índices (seção 5.4 do PIM)
   Cada índice abaixo atende a uma consulta específica; a justificativa fica
   registrada no próprio script (LESSA, 2026, p. 254).
   ============================================================================= */
SET NOCOUNT ON;
-- Obrigatórios para índices filtrados: procedures e triggers guardam estas
-- opções no momento da criação.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Painel de SLA e fila de atendimento: só ordens em aberto (status 1, 2 e 3),
-- ordenadas pelo prazo. Índice filtrado: menor, mais barato de manter e cobre
-- a consulta sem acessar a tabela (INCLUDE).
CREATE NONCLUSTERED INDEX IX_OrdemServico_Abertas_Prazo
ON unifica.OrdemServico (StatusId, PrazoSlaUtc)
INCLUDE (Protocolo, ClienteId, Titulo, Prioridade, AbertaEmUtc)
WHERE StatusId IN (1, 2, 3);
GO

-- Consulta das ordens de um cliente (portal web e app mobile).
CREATE NONCLUSTERED INDEX IX_OrdemServico_Cliente_Abertura
ON unifica.OrdemServico (ClienteId, AbertaEmUtc DESC)
INCLUDE (Protocolo, Titulo, StatusId, PrazoSlaUtc);
GO

-- Chave estrangeira usada em OS por contrato e em fn_ConsumoContrato.
CREATE NONCLUSTERED INDEX IX_OrdemServico_Contrato
ON unifica.OrdemServico (ContratoId);
GO

-- Soma das horas por OS (usp_ObterOrdem) sem ler a linha inteira do apontamento.
CREATE NONCLUSTERED INDEX IX_Apontamento_Ordem
ON unifica.Apontamento (OrdemId)
INCLUDE (Horas, DataTrabalho);
GO

-- Histórico exibido na tela de detalhe da OS.
CREATE NONCLUSTERED INDEX IX_Historico_Ordem
ON unifica.HistoricoStatusOrdem (OrdemId, AlteradoEmUtc);
GO

-- Consulta de diagnóstico: índices com mais escrita do que leitura são candidatos
-- a remoção (cada índice tem custo em INSERT/UPDATE — LESSA, 2026, p. 71).
-- SELECT OBJECT_NAME(s.object_id) AS Tabela, i.name AS Indice,
--        s.user_seeks + s.user_scans + s.user_lookups AS Leituras, s.user_updates AS Escritas
-- FROM   sys.dm_db_index_usage_stats AS s
-- JOIN   sys.indexes AS i ON i.object_id = s.object_id AND i.index_id = s.index_id
-- WHERE  s.database_id = DB_ID() AND OBJECT_SCHEMA_NAME(s.object_id) = 'unifica';
