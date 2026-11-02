#!/bin/sh
set -eu
# Пароль читается psql из файла и не передаётся аргументом процесса.
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set ON_ERROR_STOP=1 <<'SQL'
\set app_password `cat /tmp/interview-tls/app-password`
CREATE ROLE interview LOGIN PASSWORD :'app_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
ALTER DATABASE interview OWNER TO interview;
ALTER SCHEMA public OWNER TO interview;
SQL
