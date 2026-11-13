/* =============================================================================
   Plataforma Unifica — Alunos Tech
   01 — Estrutura (tabelas, chaves, restrições)
   Banco alvo: Azure SQL Database / SQL Server 2022
   Seção 5.1 do PIM.
   ============================================================================= */
SET NOCOUNT ON;
-- Obrigatórios para índices filtrados: procedures e triggers guardam estas
-- opções no momento da criação.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- READ COMMITTED com versionamento de linhas: leituras não esperam bloqueios
-- exclusivos de escrita (LESSA, 2026, p. 196). No Azure SQL já vem ligado; no SQL
-- Server local precisa ser ativado (exige acesso exclusivo ao banco).
IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()) = 0
BEGIN
    DECLARE @sql nvarchar(300) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME())
        + N' SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;';
    EXEC (@sql);
END
GO

IF SCHEMA_ID(N'unifica') IS NULL
    EXEC (N'CREATE SCHEMA unifica AUTHORIZATION dbo;');
GO

/* ---------- Tabelas de apoio ---------------------------------------------- */
CREATE TABLE unifica.StatusOrdem
(
    StatusId    tinyint      NOT NULL CONSTRAINT PK_StatusOrdem PRIMARY KEY,
    Nome        nvarchar(30) NOT NULL CONSTRAINT UQ_StatusOrdem_Nome UNIQUE,
    Finalizador bit          NOT NULL CONSTRAINT DF_StatusOrdem_Finalizador DEFAULT (0)
);

-- Transições permitidas: a mesma regra do domínio C#, garantida também no banco.
CREATE TABLE unifica.TransicaoStatus
(
    DeStatusId   tinyint NOT NULL CONSTRAINT FK_Transicao_De   REFERENCES unifica.StatusOrdem (StatusId),
    ParaStatusId tinyint NOT NULL CONSTRAINT FK_Transicao_Para REFERENCES unifica.StatusOrdem (StatusId),
    CONSTRAINT PK_TransicaoStatus PRIMARY KEY (DeStatusId, ParaStatusId)
);

CREATE SEQUENCE unifica.SeqProtocolo AS int START WITH 1 INCREMENT BY 1 CACHE 50;

/* ---------- Cadastros ------------------------------------------------------ */
CREATE TABLE unifica.Cliente
(
    ClienteId   int           NOT NULL IDENTITY(1, 1) CONSTRAINT PK_Cliente PRIMARY KEY,
    RazaoSocial nvarchar(150) NOT NULL,
    Cnpj        char(14)      NOT NULL CONSTRAINT UQ_Cliente_Cnpj UNIQUE,
    EmailContato nvarchar(150) NOT NULL,
    Ativo       bit           NOT NULL CONSTRAINT DF_Cliente_Ativo DEFAULT (1),
    CriadoEmUtc datetime2(0)  NOT NULL CONSTRAINT DF_Cliente_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Cliente_Cnpj CHECK (Cnpj NOT LIKE '%[^0-9]%')
);

CREATE TABLE unifica.Contrato
(
    ContratoId       int          NOT NULL IDENTITY(1, 1) CONSTRAINT PK_Contrato PRIMARY KEY,
    ClienteId        int          NOT NULL CONSTRAINT FK_Contrato_Cliente REFERENCES unifica.Cliente (ClienteId),
    Nivel            tinyint      NOT NULL,      -- 1 Essencial, 2 Profissional, 3 Corporativo
    HorasContratadas decimal(9,2) NOT NULL,
    SaldoHoras       decimal(9,2) NOT NULL,
    VigenciaInicio   date         NOT NULL,
    VigenciaFim      date         NOT NULL,
    Ativo            bit          NOT NULL CONSTRAINT DF_Contrato_Ativo DEFAULT (1),
    Versao           rowversion   NOT NULL,
    CONSTRAINT CK_Contrato_Nivel    CHECK (Nivel BETWEEN 1 AND 3),
    CONSTRAINT CK_Contrato_Saldo    CHECK (SaldoHoras >= 0 AND SaldoHoras <= HorasContratadas),
    CONSTRAINT CK_Contrato_Vigencia CHECK (VigenciaFim > VigenciaInicio)
);

CREATE TABLE unifica.Colaborador
(
    ColaboradorId int           NOT NULL IDENTITY(1, 1) CONSTRAINT PK_Colaborador PRIMARY KEY,
    Nome          nvarchar(120) NOT NULL,
    Login         nvarchar(100) NOT NULL CONSTRAINT UQ_Colaborador_Login UNIQUE,
    Email         nvarchar(150) NOT NULL CONSTRAINT UQ_Colaborador_Email UNIQUE,
    Equipe        nvarchar(60)  NOT NULL,
    Ativo         bit           NOT NULL CONSTRAINT DF_Colaborador_Ativo DEFAULT (1)
);

/* ---------- Ordens de serviço --------------------------------------------- */
CREATE TABLE unifica.OrdemServico
(
    OrdemId         bigint         NOT NULL IDENTITY(1, 1) CONSTRAINT PK_OrdemServico PRIMARY KEY,
    Protocolo       varchar(20)    NOT NULL CONSTRAINT UQ_OrdemServico_Protocolo UNIQUE,
    ClienteId       int            NOT NULL CONSTRAINT FK_Ordem_Cliente  REFERENCES unifica.Cliente (ClienteId),
    ContratoId      int            NOT NULL CONSTRAINT FK_Ordem_Contrato REFERENCES unifica.Contrato (ContratoId),
    Titulo          nvarchar(150)  NOT NULL,
    Descricao       nvarchar(2000) NULL,
    Prioridade      tinyint        NOT NULL,
    StatusId        tinyint        NOT NULL CONSTRAINT DF_Ordem_Status DEFAULT (1)
                                   CONSTRAINT FK_Ordem_Status REFERENCES unifica.StatusOrdem (StatusId),
    AbertaEmUtc     datetime2(0)   NOT NULL CONSTRAINT DF_Ordem_AbertaEm DEFAULT (SYSUTCDATETIME()),
    PrazoSlaUtc     datetime2(0)   NOT NULL,
    FinalizadaEmUtc datetime2(0)   NULL,
    AbertaPor       nvarchar(100)  NOT NULL,
    AtualizadaEmUtc datetime2(0)   NOT NULL CONSTRAINT DF_Ordem_AtualizadaEm DEFAULT (SYSUTCDATETIME()),
    Versao          rowversion     NOT NULL,   -- controle otimista de concorrência
    CONSTRAINT CK_Ordem_Prioridade CHECK (Prioridade BETWEEN 1 AND 4),
    CONSTRAINT CK_Ordem_Prazo      CHECK (PrazoSlaUtc > AbertaEmUtc)
);

CREATE TABLE unifica.Apontamento
(
    ApontamentoId   bigint        NOT NULL IDENTITY(1, 1) CONSTRAINT PK_Apontamento PRIMARY KEY,
    OrdemId         bigint        NOT NULL CONSTRAINT FK_Apontamento_Ordem REFERENCES unifica.OrdemServico (OrdemId),
    ColaboradorId   int           NOT NULL CONSTRAINT FK_Apontamento_Colaborador REFERENCES unifica.Colaborador (ColaboradorId),
    DataTrabalho    date          NOT NULL,
    Horas           decimal(5,2)  NOT NULL,
    Descricao       nvarchar(500) NOT NULL,
    RegistradoEmUtc datetime2(0)  NOT NULL CONSTRAINT DF_Apontamento_Registrado DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Apontamento_Horas CHECK (Horas > 0 AND Horas <= 12)
);

/* ---------- Histórico e auditoria (preenchidos por triggers) --------------- */
CREATE TABLE unifica.HistoricoStatusOrdem
(
    HistoricoId    bigint        NOT NULL IDENTITY(1, 1) CONSTRAINT PK_HistoricoStatus PRIMARY KEY,
    OrdemId        bigint        NOT NULL CONSTRAINT FK_Historico_Ordem REFERENCES unifica.OrdemServico (OrdemId),
    StatusAnterior tinyint       NULL,
    StatusNovo     tinyint       NOT NULL,
    AlteradoEmUtc  datetime2(0)  NOT NULL CONSTRAINT DF_Historico_Alterado DEFAULT (SYSUTCDATETIME()),
    AlteradoPor    nvarchar(100) NOT NULL
);

CREATE TABLE unifica.LogAuditoria
(
    AuditoriaId     bigint        NOT NULL IDENTITY(1, 1) CONSTRAINT PK_LogAuditoria PRIMARY KEY,
    Tabela          sysname       NOT NULL,
    Operacao        char(1)       NOT NULL,   -- I, U, D
    Chave           nvarchar(50)  NOT NULL,
    DadosAnteriores nvarchar(max) NULL,       -- JSON
    DadosNovos      nvarchar(max) NULL,       -- JSON
    Usuario         nvarchar(100) NOT NULL,
    RegistradoEmUtc datetime2(0)  NOT NULL CONSTRAINT DF_Auditoria_Registrado DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Auditoria_Operacao CHECK (Operacao IN ('I', 'U', 'D'))
);
GO

/* ---------- Dados de domínio fixos ---------------------------------------- */
INSERT INTO unifica.StatusOrdem (StatusId, Nome, Finalizador) VALUES
    (1, N'Aberta', 0), (2, N'Em atendimento', 0), (3, N'Aguardando cliente', 0),
    (4, N'Resolvida', 0), (5, N'Encerrada', 1), (6, N'Cancelada', 1);

INSERT INTO unifica.TransicaoStatus (DeStatusId, ParaStatusId) VALUES
    (1, 2), (1, 6),
    (2, 3), (2, 4), (2, 6),
    (3, 2), (3, 6),
    (4, 5), (4, 2);
GO
