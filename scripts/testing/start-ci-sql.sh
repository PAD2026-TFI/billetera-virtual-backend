#!/usr/bin/env bash
set -euo pipefail
: "${GITHUB_ENV:?Este script prepara exclusivamente el SQL descartable de GitHub Actions.}"

sql_test_password="Dev$(openssl rand -hex 24)!"
sql_test_jwt="$(openssl rand -base64 32)"
sql_test_connection="Server=127.0.0.1,1433;Database=WalletIntegrationTests;User ID=sa;Password=${sql_test_password};Encrypt=True;TrustServerCertificate=True"
# Sólo el certificado autofirmado del contenedor local; no cambio la confianza de servicios externos.
printf '::add-mask::%s\n' "$sql_test_password"
printf '::add-mask::%s\n' "$sql_test_jwt"
printf '::add-mask::%s\n' "$sql_test_connection"
{
    printf 'MSSQL_SA_PASSWORD=%s\n' "$sql_test_password"
    printf 'WALLET_TEST_CONNECTION=%s\n' "$sql_test_connection"
    printf 'ConnectionStrings__DefaultConnection=%s\n' "$sql_test_connection"
    printf 'Jwt__Key=%s\n' "$sql_test_jwt"
} >> "$GITHUB_ENV"
export MSSQL_SA_PASSWORD="$sql_test_password"
export SQLCMDPASSWORD="$sql_test_password"
docker run --name wallet-test-sql --detach \
    --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_MEMORY_LIMIT_MB=2048 \
    --env MSSQL_SA_PASSWORD --publish 127.0.0.1:1433:1433 \
    mcr.microsoft.com/mssql/server:2022-latest
for attempt in {1..60}; do
    if docker exec --env SQLCMDPASSWORD wallet-test-sql /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -C -b -l 2 -Q 'SELECT 1' >/dev/null 2>&1; then
        echo 'SQL Server de pruebas disponible.'
        exit 0
    fi
    sleep 2
done
echo 'SQL Server de pruebas no alcanzó readiness.' >&2
exit 1
