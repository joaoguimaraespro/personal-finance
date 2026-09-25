#!/bin/sh
# Least privilege: the migrator owns the schemas (DDL), the app can only read/write rows, backups can only read.
# Grants live in 20-grants.sql so a restore can re-apply them.
set -eu
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<SQL
CREATE ROLE finance_migrator LOGIN PASSWORD '${FINANCE_MIGRATOR_PASSWORD}';
CREATE ROLE finance_app LOGIN PASSWORD '${FINANCE_APP_PASSWORD}';
CREATE ROLE finance_backup LOGIN PASSWORD '${FINANCE_BACKUP_PASSWORD}';
REVOKE ALL ON DATABASE ${POSTGRES_DB} FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CONNECT ON DATABASE ${POSTGRES_DB} TO finance_migrator, finance_app, finance_backup;
GRANT CREATE ON DATABASE ${POSTGRES_DB} TO finance_migrator;
SQL
