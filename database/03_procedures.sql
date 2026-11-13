/* =============================================================================
   03 — Stored procedures (seções 5.2 e 5.3 do PIM)
   A API só acessa o banco por estas procedures (permissão EXECUTE apenas).
   Erros de regra usam THROW 50xxx com o prefixo [UNIFICA:...], que o
   repositório C# converte em respostas 409 para o cliente.
   ============================================================================= */
SET NOCOUNT ON;
-- Obrigatórios para índices filtrados: procedures e triggers guardam estas
-- opções no momento da criação.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ---------- Leitura de contrato (usada para calcular o SLA) --------------- */
CREATE OR ALTER PROCEDURE unifica.usp_ObterContrato
    @ContratoId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  ContratoId, ClienteId, Nivel, SaldoHoras,
            Ativo = CAST(CASE WHEN Ativo = 1
                               AND CAST(SYSUTCDATETIME() AS date) BETWEEN VigenciaInicio AND VigenciaFim
                              THEN 1 ELSE 0 END AS bit)
    FROM    unifica.Contrato
    WHERE   ContratoId = @ContratoId;
END;
GO

/* ---------- Abertura de OS -------------------------------------------------- */
CREATE OR ALTER PROCEDURE unifica.usp_AbrirOrdem
    @ContratoId  int,
    @Titulo      nvarchar(150),
    @Descricao   nvarchar(2000) = NULL,
    @Prioridade  tinyint,
    @AbertaEmUtc datetime2(0),
    @PrazoSlaUtc datetime2(0),
    @Usuario     nvarchar(100),
    @OrdemId     bigint OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ClienteId int;

    SELECT  @ClienteId = ClienteId
    FROM    unifica.Contrato
    WHERE   ContratoId = @ContratoId
      AND   Ativo = 1
      AND   CAST(@AbertaEmUtc AS date) BETWEEN VigenciaInicio AND VigenciaFim;

    IF @ClienteId IS NULL
        THROW 50001, N'[UNIFICA:CONTRATO] Contrato inexistente, inativo ou fora da vigência.', 1;

    DECLARE @Numero int = NEXT VALUE FOR unifica.SeqProtocolo;

    -- O usuário vai para o contexto da sessão para que o trigger de histórico o registre.
    EXEC sys.sp_set_session_context @key = N'usuario', @value = @Usuario;

    INSERT INTO unifica.OrdemServico
        (Protocolo, ClienteId, ContratoId, Titulo, Descricao, Prioridade, StatusId,
         AbertaEmUtc, PrazoSlaUtc, AbertaPor, AtualizadaEmUtc)
    VALUES
        (unifica.fn_FormatarProtocolo(YEAR(@AbertaEmUtc), @Numero), @ClienteId, @ContratoId,
         @Titulo, @Descricao, @Prioridade, 1, @AbertaEmUtc, @PrazoSlaUtc, @Usuario, @AbertaEmUtc);

    SET @OrdemId = CAST(SCOPE_IDENTITY() AS bigint);
END;
GO

/* ---------- Alteração de status com concorrência otimista ------------------ */
CREATE OR ALTER PROCEDURE unifica.usp_AlterarStatusOrdem
    @OrdemId        bigint,
    @NovoStatusId   tinyint,
    @VersaoEsperada binary(8),
    @Usuario        nvarchar(100),
    @Alterou        bit OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Afetadas TABLE (OrdemId bigint);

    EXEC sys.sp_set_session_context @key = N'usuario', @value = @Usuario;

    -- Só altera se (1) a versão lida pelo cliente ainda é a atual e
    -- (2) a transição está cadastrada. Sem bloqueio durante a edição na tela.
    -- OUTPUT ... INTO registra as linhas alteradas sem depender de @@ROWCOUNT
    -- (a tabela tem trigger de histórico).
    UPDATE  o
    SET     o.StatusId        = @NovoStatusId,
            o.AtualizadaEmUtc = SYSUTCDATETIME(),
            o.FinalizadaEmUtc = CASE WHEN @NovoStatusId IN (5, 6) THEN SYSUTCDATETIME() ELSE o.FinalizadaEmUtc END
    OUTPUT  inserted.OrdemId INTO @Afetadas (OrdemId)
    FROM    unifica.OrdemServico AS o
    JOIN    unifica.TransicaoStatus AS t
            ON t.DeStatusId = o.StatusId AND t.ParaStatusId = @NovoStatusId
    WHERE   o.OrdemId = @OrdemId
      AND   o.Versao  = @VersaoEsperada;

    SET @Alterou = CASE WHEN EXISTS (SELECT 1 FROM @Afetadas) THEN 1 ELSE 0 END;
END;
GO

/* ---------- Apontamento de horas: transação + controle de concorrência ------ */
CREATE OR ALTER PROCEDURE unifica.usp_RegistrarApontamento
    @OrdemId      bigint,
    @Horas        decimal(5,2),
    @Descricao    nvarchar(500),
    @DataTrabalho date,
    @Usuario      nvarchar(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;   -- qualquer erro desfaz a transação inteira

    DECLARE @Tentativa int = 1,
            @ColaboradorId int, @ContratoId int, @StatusId tinyint, @Saldo decimal(9,2);

    WHILE @Tentativa <= 3
    BEGIN
        BEGIN TRY
            -- Variáveis zeradas a cada tentativa (SELECT sem linhas não as altera).
            SELECT @ColaboradorId = NULL, @ContratoId = NULL, @StatusId = NULL, @Saldo = NULL;

            BEGIN TRANSACTION;

            SELECT @ColaboradorId = ColaboradorId
            FROM   unifica.Colaborador
            WHERE  Login = @Usuario AND Ativo = 1;

            IF @ColaboradorId IS NULL
                THROW 50004, N'[UNIFICA:COLABORADOR] Usuário não cadastrado como colaborador ativo.', 1;

            SELECT @ContratoId = ContratoId, @StatusId = StatusId
            FROM   unifica.OrdemServico
            WHERE  OrdemId = @OrdemId;

            IF @ContratoId IS NULL
                THROW 50002, N'[UNIFICA:ORDEM] Ordem de serviço não encontrada.', 1;

            IF @StatusId <> 2
                THROW 50005, N'[UNIFICA:STATUS] Só é possível apontar horas em ordens em atendimento.', 1;

            -- Ordem fixa de acesso (Contrato → Apontamento) e UPDLOCK no saldo:
            -- dois técnicos apontando ao mesmo tempo no mesmo contrato são
            -- serializados aqui, sem leitura de saldo desatualizado.
            SELECT @Saldo = SaldoHoras
            FROM   unifica.Contrato WITH (UPDLOCK, ROWLOCK)
            WHERE  ContratoId = @ContratoId;

            IF @Saldo < @Horas
                THROW 50003, N'[UNIFICA:SALDO] Saldo de horas do contrato insuficiente.', 1;

            EXEC sys.sp_set_session_context @key = N'usuario', @value = @Usuario;

            UPDATE unifica.Contrato
            SET    SaldoHoras = SaldoHoras - @Horas
            WHERE  ContratoId = @ContratoId;

            INSERT INTO unifica.Apontamento (OrdemId, ColaboradorId, DataTrabalho, Horas, Descricao)
            VALUES (@OrdemId, @ColaboradorId, @DataTrabalho, @Horas, @Descricao);

            COMMIT TRANSACTION;
            RETURN;
        END TRY
        BEGIN CATCH
            IF XACT_STATE() <> 0
                ROLLBACK TRANSACTION;

            -- 1205 = vítima de deadlock: tenta de novo (até 3 vezes) após breve espera.
            IF ERROR_NUMBER() = 1205 AND @Tentativa < 3
            BEGIN
                SET @Tentativa += 1;
                WAITFOR DELAY '00:00:00.200';
            END
            ELSE
                THROW;   -- relança o erro original para a aplicação
        END CATCH;
    END;
END;
GO

/* ---------- Consultas ------------------------------------------------------ */
CREATE OR ALTER PROCEDURE unifica.usp_ObterOrdem
    @OrdemId bigint
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  o.OrdemId, o.Protocolo, o.ClienteId, Cliente = c.RazaoSocial, o.ContratoId,
            o.Titulo, o.Descricao, o.Prioridade, o.StatusId, o.AbertaEmUtc, o.PrazoSlaUtc,
            HorasApontadas = ISNULL(a.Horas, 0),
            o.Versao
    FROM    unifica.OrdemServico AS o
    JOIN    unifica.Cliente      AS c ON c.ClienteId = o.ClienteId
    OUTER APPLY (SELECT Horas = SUM(ap.Horas)
                 FROM   unifica.Apontamento AS ap
                 WHERE  ap.OrdemId = o.OrdemId) AS a
    WHERE   o.OrdemId = @OrdemId;
END;
GO

-- Listagem paginada com OFFSET/FETCH. Os filtros são opcionais; OPTION (RECOMPILE)
-- evita que o plano gerado para um filtro seja reaproveitado para outro
-- (parameter sniffing — LESSA, 2026, p. 254).
CREATE OR ALTER PROCEDURE unifica.usp_ListarOrdens
    @StatusId      tinyint = NULL,
    @ClienteId     int     = NULL,
    @Pagina        int     = 1,
    @TamanhoPagina int     = 20
AS
BEGIN
    SET NOCOUNT ON;

    IF @Pagina < 1 SET @Pagina = 1;
    IF @TamanhoPagina NOT BETWEEN 1 AND 100 SET @TamanhoPagina = 20;

    SELECT  o.OrdemId, o.Protocolo, Cliente = c.RazaoSocial, o.Titulo, o.Prioridade, o.StatusId,
            o.AbertaEmUtc, o.PrazoSlaUtc, sla.Situacao,
            TotalItens = COUNT(*) OVER ()
    FROM    unifica.OrdemServico AS o
    JOIN    unifica.Cliente      AS c ON c.ClienteId = o.ClienteId
    CROSS APPLY unifica.fn_SituacaoSla(o.AbertaEmUtc, o.PrazoSlaUtc, o.StatusId, SYSUTCDATETIME()) AS sla
    WHERE   (@StatusId  IS NULL OR o.StatusId  = @StatusId)
      AND   (@ClienteId IS NULL OR o.ClienteId = @ClienteId)
    ORDER BY o.PrazoSlaUtc, o.OrdemId
    OFFSET (@Pagina - 1) * @TamanhoPagina ROWS
    FETCH NEXT @TamanhoPagina ROWS ONLY
    OPTION (RECOMPILE);
END;
GO

CREATE OR ALTER PROCEDURE unifica.usp_PainelSla
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  Cliente,
            OrdensAbertas = COUNT(*),
            EmRisco       = SUM(CASE WHEN Situacao = 'EmRisco' THEN 1 ELSE 0 END),
            Violadas      = SUM(CASE WHEN Situacao = 'Violado' THEN 1 ELSE 0 END)
    FROM    unifica.vw_OrdensEmAberto
    GROUP BY Cliente
    ORDER BY Violadas DESC, EmRisco DESC, Cliente;
END;
GO
