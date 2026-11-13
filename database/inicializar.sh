#!/bin/bash
# Cria o banco Unifica e executa os scripts em ordem. Usado pelo docker compose
# e pelo pipeline de CI. Para no primeiro erro (-b).
set -euo pipefail

SERVIDOR="${SERVIDOR:-sqlserver}"
# -I liga QUOTED_IDENTIFIER (o padrão do sqlcmd é desligado).
SQLCMD=(/opt/mssql-tools18/bin/sqlcmd -C -I -S "$SERVIDOR" -U sa -P "$SA_SENHA" -b)

"${SQLCMD[@]}" -Q "IF DB_ID('Unifica') IS NULL CREATE DATABASE Unifica;"

# Se a estrutura já existe (ex.: segundo "docker compose up"), não executa de novo.
JA_EXISTE=$("${SQLCMD[@]}" -d Unifica -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables WHERE name = 'OrdemServico';")
if [ "$(echo "$JA_EXISTE" | tr -d '[:space:]')" != "0" ]; then
  echo ">> Banco Unifica já inicializado."
  exit 0
fi

for script in /scripts/0*.sql /scripts/99_testes_smoke.sql; do
  echo ">> Executando $(basename "$script")"
  "${SQLCMD[@]}" -d Unifica -i "$script"
done
