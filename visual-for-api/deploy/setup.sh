#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
umask 077
if [ ! -f .env ]; then
    cp .env.example .env
    printf '\nLOCAL_UID=%s\nLOCAL_GID=%s\n' "$(id -u)" "$(id -g)" >> .env
fi
mkdir -p .deploy
chmod 0700 .deploy
docker compose --profile tools build setup
docker compose --profile tools run --rm setup
echo 'Секреты готовы. Настрой DOMAIN в .env, затем выполни docker compose --profile tools build sandbox и docker compose up --build -d.'
