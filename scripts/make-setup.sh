#!/usr/bin/env sh
# Idempotent Docker stack bootstrap: env file, compose up, wait for Ollama, pull models.

set -eu
ROOT="$(CDPATH="" cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

sh scripts/docker-check.sh
sh scripts/env-bootstrap.sh

echo ''
echo 'Checking Docker GPU support for Ollama (optional NVIDIA pass-through) …'
sh scripts/configure-ollama-gpu-choice.sh

DOCKER_COMPOSE_FILES="-f docker-compose.yml -f docker-compose.dev.yml"
GPU_COMPOSE_FILE=""
if [ -f .env ]; then
  _gpu="$(grep '^OLLAMA_USE_GPU=' .env 2>/dev/null | tail -n1 | sed 's/^[^=]*=//' | tr -d '\r')"
  case "$_gpu" in
    1|true|TRUE|True|yes|YES|Yes|y|Y) GPU_COMPOSE_FILE="-f docker-compose.gpu.yml" ;;
  esac
fi
COMPOSE_FILE_ARGS="$DOCKER_COMPOSE_FILES $GPU_COMPOSE_FILE"
ENVFILE_ARG="--env-file .env"

echo 'Pulling/updating pinned base images (postgres, ollama, rabbitmq) …'
docker compose $COMPOSE_FILE_ARGS $ENVFILE_ARG pull postgres ollama rabbitmq || true

echo ''
echo 'Building application images …'
docker compose $COMPOSE_FILE_ARGS $ENVFILE_ARG build

echo ''
echo 'Starting containers …'
docker compose $COMPOSE_FILE_ARGS $ENVFILE_ARG up -d

echo ''
echo 'Waiting for Ollama to accept commands …'
sh scripts/wait-ollama.sh "$COMPOSE_FILE_ARGS" "$ENVFILE_ARG"

echo ''
sh scripts/ollama-pull-models.sh "$COMPOSE_FILE_ARGS" "$ENVFILE_ARG"

OLLAMA_CHAT_MODEL_VALUE="llama3.2"
if [ -f .env ]; then
  OL=$(grep '^OLLAMA_CHAT_MODEL=' .env 2>/dev/null | sed 's/^OLLAMA_CHAT_MODEL=//' | tr -d '\r' || true)
  if [ -n "$OL" ]; then
    OLLAMA_CHAT_MODEL_VALUE="$OL"
  fi
fi

OLLAMA_GPU_NOTE='Ollama: CPU (set OLLAMA_USE_GPU=1 in .env + NVIDIA toolkit if you add GPU later)'
if [ -n "$GPU_COMPOSE_FILE" ]; then
  OLLAMA_GPU_NOTE='Ollama: NVIDIA GPU overlay enabled (docker-compose.gpu.yml)'
fi

WP="${WEB_PORT:-8080}"
if [ -f .env ]; then
  W="$(grep '^WEB_PORT=' .env 2>/dev/null | sed 's/^WEB_PORT=//' | tr -d '\r' || true)"
  if [ -n "$W" ]; then WP="$W"; fi
fi

OLLAMA_PT="${OLLAMA_PORT:-11434}"
if [ -f .env ]; then
  O="$(grep '^OLLAMA_PORT=' .env 2>/dev/null | sed 's/^OLLAMA_PORT=//' | tr -d '\r' || true)"
  if [ -n "$O" ]; then OLLAMA_PT="$O"; fi
fi

API_PT="${API_PORT:-5022}"
if [ -f .env ]; then
  A="$(grep '^API_PORT=' .env 2>/dev/null | sed 's/^API_PORT=//' | tr -d '\r' || true)"
  if [ -n "$A" ]; then API_PT="$A"; fi
fi

PG_PT="${POSTGRES_PORT:-5432}"
if [ -f .env ]; then
  P="$(grep '^POSTGRES_PORT=' .env 2>/dev/null | sed 's/^POSTGRES_PORT=//' | tr -d '\r' || true)"
  if [ -n "$P" ]; then PG_PT="$P"; fi
fi

printf '\n%s\n' \
  '--- SmartNarrator is up ---' \
  "  SPA (with /api proxy):  http://127.0.0.1:${WP}" \
  "  API (direct, dev):       http://127.0.0.1:${API_PT}" \
  "  Ollama (direct, dev):   http://127.0.0.1:${OLLAMA_PT}" \
  "  Postgres (host dev):      localhost:${PG_PT}" \
  '' \
  "  API configured Ollama model (OLLAMA_CHAT_MODEL): ${OLLAMA_CHAT_MODEL_VALUE}" \
  "  ${OLLAMA_GPU_NOTE}" \
  '' \
  'To change analysis model: edit .env (OLLAMA_CHAT_MODEL + optionally OLLAMA_PULL_MODELS),' \
  '  then run `make deploy` so the api container picks up env changes.' \
