#!/usr/bin/env sh
# Fast iteration: rebuild API + SPA images and recreate those services.

set -eu
ROOT="$(CDPATH="" cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

sh scripts/docker-check.sh

if [ ! -f .env ]; then
  echo 'ERROR: .env is missing. Run `make setup` first (or copy .env.example).' >&2
  exit 1
fi

DOCKER_COMPOSE_FILES="-f docker-compose.yml -f docker-compose.dev.yml"
ENVFILE_ARG="--env-file .env"

# Git Bash / MSYS rewrite Unix-looking paths into "C:/Program Files/..." which breaks Docker (split on spaces).
DOCKER_COMPOSE=""
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    DOCKER_COMPOSE='env MSYS_NO_PATHCONV=1 docker compose'
    ;;
  *)
    DOCKER_COMPOSE='docker compose'
    ;;
esac

echo 'Building api, spa, worker …'
$DOCKER_COMPOSE $DOCKER_COMPOSE_FILES $ENVFILE_ARG build api spa worker

echo ''
echo 'Ensuring Postgres is up …'
$DOCKER_COMPOSE $DOCKER_COMPOSE_FILES $ENVFILE_ARG up -d postgres

echo 'Waiting for Postgres (pg_isready) …'
i=0
while [ "$i" -lt 90 ]; do
  if $DOCKER_COMPOSE $DOCKER_COMPOSE_FILES $ENVFILE_ARG exec -T postgres pg_isready -U postgres >/dev/null 2>&1; then
    break
  fi
  i=$((i + 1))
  sleep 1
done
if ! $DOCKER_COMPOSE $DOCKER_COMPOSE_FILES $ENVFILE_ARG exec -T postgres pg_isready -U postgres >/dev/null 2>&1; then
  echo 'ERROR: Postgres did not become ready in time.' >&2
  exit 1
fi

echo 'Waiting for RabbitMQ (ping) …'
j=0
while [ "$j" -lt 90 ]; do
  if $DOCKER_COMPOSE $DOCKER_COMPOSE_FILES $ENVFILE_ARG exec -T rabbitmq rabbitmq-diagnostics -q ping >/dev/null 2>&1; then
    break
  fi
  j=$((j + 1))
  sleep 1
done
if ! $DOCKER_COMPOSE $DOCKER_COMPOSE_FILES $ENVFILE_ARG exec -T rabbitmq rabbitmq-diagnostics -q ping >/dev/null 2>&1; then
  echo 'ERROR: RabbitMQ did not become ready in time.' >&2
  exit 1
fi

echo ''
echo 'Applying database migrations …'
# Bundle matches docker-compose.yml Postgres defaults; override if you change DB credentials.
DB_CONN="${SMARTNARRATOR_DB_CONNECTION:-Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=smartnarrator}"
$DOCKER_COMPOSE $DOCKER_COMPOSE_FILES $ENVFILE_ARG run --rm --no-deps \
  --entrypoint /app/db-migrate \
  api \
  --connection "$DB_CONN" \
  || {
    echo 'ERROR: Database migrations failed.' >&2
    exit 1
  }

echo ''
echo 'Recreating api, spa, worker with new images …'
$DOCKER_COMPOSE $DOCKER_COMPOSE_FILES $ENVFILE_ARG up -d --force-recreate api spa worker

WP="${WEB_PORT:-8080}"
if [ -f .env ]; then
  W="$(grep '^WEB_PORT=' .env 2>/dev/null | sed 's/^WEB_PORT=//' | tr -d '\r' || true)"
  if [ -n "$W" ]; then WP="$W"; fi
fi

printf '\n%s\n' \
  'Deploy complete.' \
  "  SPA: http://127.0.0.1:${WP}" \
  ''
