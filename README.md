# Unifica — Plataforma de Serviços da Alunos Tech

Protótipo funcional do **PIM VIII** (CST em Análise e Desenvolvimento de Sistemas — UNIP).
A Alunos Tech é uma empresa **fictícia**; clientes, pessoas e valores são dados de exemplo.

A plataforma centraliza as **ordens de serviço (OS)** dos clientes corporativos: abertura com
prazo de SLA calculado pelo nível do contrato, fila de atendimento, mudança de status com
histórico, apontamento de horas com débito do saldo contratado e painel de SLA.

## Estrutura

| Pasta | Conteúdo | Disciplina |
|---|---|---|
| `src/AlunosTech.Unifica.Dominio` | Entidade `OrdemServico`, regras de transição, políticas de SLA (Strategy + Factory) | Programação Aplicada em .NET |
| `src/AlunosTech.Unifica.Aplicacao` | Casos de uso (`ServicoOrdens`), DTOs (records), contrato do repositório | Programação Aplicada em .NET |
| `src/AlunosTech.Unifica.Infraestrutura` | Repositório SQL (só stored procedures, parâmetros tipados) e repositório em memória | .NET + Banco de Dados |
| `src/AlunosTech.Unifica.Api` | API REST versionada (`/api/v1`), autenticação Entra ID, sondas de saúde | .NET + Cloud |
| `src/AlunosTech.Unifica.Desktop.Nucleo` | ViewModels (MVVM) e cliente HTTP do módulo desktop | Programação Aplicada em .NET |
| `src/AlunosTech.Unifica.Desktop` | Telas WPF da central de serviços (Windows) | Programação Aplicada em .NET |
| `tests/AlunosTech.Unifica.Testes` | Testes MSTest: domínio, casos de uso, ViewModel e API (WebApplicationFactory) | .NET |
| `database/` | Scripts T-SQL: tabelas, functions, views, procedures, triggers, índices, segurança, testes de fumaça | Programação de Banco de Dados |
| `deploy/infra/main.bicep` | Infraestrutura como código no Azure (AKS, ACR, Azure SQL, Key Vault, Log Analytics, orçamento) | Cloud Computing e DevOps |
| `deploy/k8s/` | Manifestos Kubernetes (kustomize) para homologação e produção | Cloud Computing e DevOps |
| `.github/workflows/ci-cd.yml` | Pipeline CI/CD: build, testes, banco, IaC, imagem, deploy com aprovação | Cloud Computing e DevOps |
| `Dockerfile`, `docker-compose.yml` | Imagem da API e ambiente local completo | Cloud Computing e DevOps |

## Como executar

**Somente a API, sem banco** (repositório em memória e autenticação de desenvolvimento):

```bash
dotnet run --project src/AlunosTech.Unifica.Api
# Em outro terminal:
curl -X POST http://localhost:5003/api/v1/ordens \
  -H "X-Usuario: ana.souza@alunostech.example" -H "X-Papeis: Tecnico" \
  -H "Content-Type: application/json" \
  -d '{"contratoId":3,"titulo":"Queda do link da filial","prioridade":4}'
```

**Ambiente completo com SQL Server** (requer Docker):

```bash
cp .env.exemplo .env       # defina SA_SENHA
docker compose up --build  # cria o banco, roda os scripts e os testes de fumaça, sobe a API
```

**Testes:** `dotnet test tests/AlunosTech.Unifica.Testes`

**Módulo desktop:** abrir a solução no Visual Studio 2026 (Windows), configurar
`src/AlunosTech.Unifica.Desktop/appsettings.json` e executar o projeto `AlunosTech.Unifica.Desktop`.

## Endpoints (v1)

| Método e rota | Papel exigido | Descrição |
|---|---|---|
| `GET /api/v1/ordens?status=&pagina=&tamanho=` | autenticado | Fila paginada, ordenada pelo prazo do SLA |
| `GET /api/v1/ordens/{id}` | autenticado | Detalhe, horas apontadas, versão e próximos status |
| `POST /api/v1/ordens` | Atendimento, Técnico ou Gestor | Abre a OS e calcula o prazo de SLA |
| `PATCH /api/v1/ordens/{id}/status` | Atendimento, Técnico ou Gestor | Muda o status (controle otimista pela versão) |
| `POST /api/v1/ordens/{id}/apontamentos` | Técnico | Registra horas e debita o saldo do contrato |
| `GET /api/v1/painel/sla` | Gestor | Ordens em aberto, em risco e violadas por cliente |
| `GET /saude/vivo`, `/saude/pronto` | anônimo | Sondas do Kubernetes |

## O que foi verificado e o que depende do ambiente

- **Compilado e testado** (.NET 10, analisadores com avisos tratados como erro): Domínio,
  Aplicação, Infraestrutura, API e Desktop.Nucleo. 36 testes de domínio, casos de uso e ViewModel
  aprovados, e o fluxo da API exercitado por HTTP (abrir → atender → apontar, conflito de versão,
  401/403, painel de SLA).
- **Depende de Windows:** o projeto WPF `AlunosTech.Unifica.Desktop` (telas XAML) só compila no
  Windows ou com `-p:EnableWindowsTargeting=true` — o pipeline compila assim.
- **Depende de SQL Server:** os scripts de `database/` são executados de verdade pelo job `banco`
  do pipeline (SQL Server em contêiner), incluindo `99_testes_smoke.sql`.
- **Depende de assinatura Azure:** `deploy/infra/main.bicep` e os jobs de deploy. O job `infra`
  do pipeline compila o Bicep e renderiza os manifestos a cada execução.

## Configuração do pipeline no GitHub

1. Variáveis do repositório: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
   (aplicativo do Entra ID com credencial federada para o GitHub — sem senha).
2. Ambientes `homologacao` e `producao`; em `producao`, marcar **Required reviewers**.

Sem as variáveis do Azure, os jobs de build, testes, banco e IaC rodam normalmente e os jobs de
imagem e deploy são pulados.
