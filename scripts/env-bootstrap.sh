#!/usr/bin/env sh
# Ensures `.env` exists (idempotent copy from `.env.example`).

set -eu
ROOT="$(CDPATH="" cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if [ ! -f .env ]; then
  if [ ! -f .env.example ]; then
    echo 'ERROR: .env is missing and .env.example could not be found.' >&2
    exit 1
  fi
  cp .env.example .env
  echo 'Created .env from .env.example — review OLLAMA_* and port variables before relying on defaults.'
fi
