#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
printf 'Email первого администратора: '
read -r ADMIN_EMAIL
[ -n "$ADMIN_EMAIL" ] || { echo 'Email не введён.' >&2; exit 1; }
export ADMIN_EMAIL
docker compose --profile tools run --rm bootstrap
echo 'Администратор создан. Пароль сгенерирован при setup; посмотреть его локально: cat .deploy/bootstrap_password'
