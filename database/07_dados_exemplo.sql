/* =============================================================================
   07 — Dados de exemplo (somente ambientes de desenvolvimento e homologação)
   Empresas e pessoas fictícias.
   ============================================================================= */
SET NOCOUNT ON;
-- Obrigatórios para índices filtrados: procedures e triggers guardam estas
-- opções no momento da criação.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

INSERT INTO unifica.Cliente (RazaoSocial, Cnpj, EmailContato) VALUES
    (N'Comercial Horizonte Ltda.',   '11222333000144', N'ti@horizonte.example'),
    (N'Clínica Vida Plena S/S',      '22333444000155', N'suporte@vidaplena.example'),
    (N'Transportes Rota Certa S.A.', '33444555000166', N'operacoes@rotacerta.example');

INSERT INTO unifica.Contrato (ClienteId, Nivel, HorasContratadas, SaldoHoras, VigenciaInicio, VigenciaFim) VALUES
    (1, 1,  40,  40, '2026-01-01', '2027-12-31'),
    (2, 2,  80,  80, '2026-01-01', '2027-12-31'),
    (3, 3, 160, 160, '2026-01-01', '2027-12-31');

INSERT INTO unifica.Colaborador (Nome, Login, Email, Equipe) VALUES
    (N'Ana Souza',      N'ana.souza@alunostech.example',      N'ana.souza@alunostech.example',      N'Suporte N2'),
    (N'Bruno Lima',     N'bruno.lima@alunostech.example',     N'bruno.lima@alunostech.example',     N'Infraestrutura'),
    (N'Carla Menezes',  N'carla.menezes@alunostech.example',  N'carla.menezes@alunostech.example',  N'Desenvolvimento');
GO
