/* =============================================================================
   06 — Segurança (seção 5.5 do PIM)
   Princípio do menor privilégio: a API não lê nem grava tabelas diretamente;
   só executa procedures. Relatórios só leem a view.
   ============================================================================= */
SET NOCOUNT ON;
-- Obrigatórios para índices filtrados: procedures e triggers guardam estas
-- opções no momento da criação.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DATABASE_PRINCIPAL_ID(N'app_unifica') IS NULL
    CREATE ROLE app_unifica;
IF DATABASE_PRINCIPAL_ID(N'relatorios_unifica') IS NULL
    CREATE ROLE relatorios_unifica;
GO

-- A API executa procedures do schema. Como procedures e tabelas têm o mesmo dono
-- (dbo), o encadeamento de propriedade permite que a procedure acesse as tabelas
-- sem que o papel tenha SELECT/INSERT/UPDATE nelas.
GRANT EXECUTE ON SCHEMA::unifica TO app_unifica;

-- Relatórios: somente leitura da view de ordens em aberto.
GRANT SELECT ON unifica.vw_OrdensEmAberto TO relatorios_unifica;
GO

-- Mascaramento dinâmico (LGPD): quem não tem UNMASK vê o dado mascarado.
ALTER TABLE unifica.Colaborador ALTER COLUMN Email ADD MASKED WITH (FUNCTION = 'email()');
ALTER TABLE unifica.Cliente     ALTER COLUMN EmailContato ADD MASKED WITH (FUNCTION = 'email()');
ALTER TABLE unifica.Cliente     ALTER COLUMN Cnpj ADD MASKED WITH (FUNCTION = 'partial(0,"XXXXXXXX",6)');
GO

/* ---------- Usuários --------------------------------------------------------
   No Azure SQL, a API entra com a identidade gerenciada do AKS (sem senha em
   arquivo de configuração). Executar conectado como administrador Entra ID:

   CREATE USER [id-unifica-api] FROM EXTERNAL PROVIDER;
   ALTER ROLE app_unifica ADD MEMBER [id-unifica-api];

   No ambiente local (docker compose) é usado um login SQL apenas para testes,
   criado pelo script de inicialização com senha vinda de variável de ambiente.
   --------------------------------------------------------------------------- */
