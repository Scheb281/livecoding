#!/bin/sh
set -eu
umask 077

# Секреты читаются при старте, а не запекаются в Docker-образ или compose.yml.
mkdir -p /run/interview
cp /run/secrets/cluster_ca /usr/local/share/ca-certificates/interview.crt
update-ca-certificates >/dev/null
cp /run/secrets/service_certificate /run/interview/service.pfx
chown app:app /run/interview /run/interview/service.pfx
export Kestrel__Certificates__Default__Path=/run/interview/service.pfx
export Kestrel__Certificates__Default__Password="$(cat /run/secrets/tls_password)"
export Runner__ApiKey="$(cat /run/secrets/runner_key)"

if [ -f /run/secrets/database_connection ]; then
    cp /run/secrets/database_ca /run/interview/database-ca.crt
    chmod 0444 /run/interview/database-ca.crt
    export ConnectionStrings__Database="$(cat /run/secrets/database_connection)"
    cp /run/secrets/keyring_certificate /run/interview/keyring.pfx
    chown app:app /run/interview/keyring.pfx
    export Security__KeyEncryptionCertificatePath=/run/interview/keyring.pfx
    export Security__KeyEncryptionCertificatePassword="$(cat /run/secrets/keyring_password)"
    export Security__KeysDirectory=/var/lib/interview/keys
    mkdir -p "$Security__KeysDirectory"
    # Импорт — только в пустой том; существующие ключи не заменяем.
    if ! find "$Security__KeysDirectory" -name 'key-*.xml' -print -quit | grep -q .; then
        for key in /imported-keys/key-*.xml; do
            [ -f "$key" ] || continue
            cp "$key" "$Security__KeysDirectory/"
        done
    fi
    chown -R app:app "$Security__KeysDirectory"
    if [ -f /run/secrets/bootstrap_password ]; then
        export Bootstrap__Password="$(cat /run/secrets/bootstrap_password)"
    fi
fi

if [ -S /var/run/docker.sock ]; then
    # Только доверенный runner получает доступ к Docker daemon.
    socket_gid="$(stat -c '%g' /var/run/docker.sock)"
    socket_group="$(getent group "$socket_gid" | cut -d: -f1 || true)"
    if [ -z "$socket_group" ]; then
        groupadd -g "$socket_gid" docker-host
        socket_group=docker-host
    fi
    usermod -aG "$socket_group" app
fi

exec gosu app "$@"
