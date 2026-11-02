#!/bin/sh
set -eu
umask 077
cd /generated
if [ -f .initialized ]; then
    for file in ca.crt ca.key database_password database_admin_password database_connection database_ca.crt runner_key tls_password keyring_password keyring.pfx api.pfx runner.pfx db.crt db.key bootstrap_password; do
        [ -s "$file" ] || { echo "Отсутствует секрет $file. Восстановите резервную копию." >&2; exit 1; }
    done
    echo 'Секреты уже созданы; существующие значения сохранены.'
    exit 0
fi
if find . -maxdepth 1 -type f -print -quit | grep -q .; then
    echo 'Каталог секретов не пуст. Сначала проверьте его содержимое; ключи не перезаписываются.' >&2
    exit 1
fi
mkdir -p imported-keys
for secret in database_password database_admin_password runner_key tls_password keyring_password bootstrap_password; do
    openssl rand -hex 32 > "$secret"
done
openssl req -x509 -newkey rsa:3072 -nodes -keyout ca.key -out ca.crt -days 3650 -subj '/CN=Interview internal CA' -addext 'basicConstraints=critical,CA:TRUE' -addext 'keyUsage=critical,keyCertSign,cRLSign' >/dev/null 2>&1
for service in api runner db; do
    openssl req -newkey rsa:2048 -nodes -keyout "$service.key" -out "$service.csr" -subj "/CN=$service" >/dev/null 2>&1
    printf 'subjectAltName=DNS:%s,DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\n' "$service" > "$service.ext"
    openssl x509 -req -in "$service.csr" -CA ca.crt -CAkey ca.key -CAcreateserial -out "$service.crt" -days 825 -extfile "$service.ext" >/dev/null 2>&1
    if [ "$service" != db ]; then
        openssl pkcs12 -export -inkey "$service.key" -in "$service.crt" -certfile ca.crt -out "$service.pfx" -passout file:tls_password
    fi
done
openssl req -x509 -newkey rsa:3072 -nodes -keyout keyring.key -out keyring.crt -days 3650 -subj '/CN=Interview keyring' >/dev/null 2>&1
openssl pkcs12 -export -inkey keyring.key -in keyring.crt -out keyring.pfx -passout file:keyring_password
cp ca.crt database_ca.crt
printf 'Host=db;Port=5432;Database=interview;Username=interview;Password=%s;SSL Mode=VerifyFull;Root Certificate=/run/interview/database-ca.crt;\n' "$(cat database_password)" > database_connection
touch .initialized
echo 'Случайные секреты и сертификаты созданы. Их значения не выводятся.'
