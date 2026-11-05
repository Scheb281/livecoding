#!/bin/sh
set -eu
mkdir -p /tmp/interview-tls
cp /run/secrets/database_certificate /tmp/interview-tls/server.crt
cp /run/secrets/database_key /tmp/interview-tls/server.key
cp /run/secrets/database_password /tmp/interview-tls/app-password
chown -R postgres:postgres /tmp/interview-tls
chmod 0600 /tmp/interview-tls/server.key
chmod 0600 /tmp/interview-tls/app-password
exec /usr/local/bin/docker-entrypoint.sh "$@"
