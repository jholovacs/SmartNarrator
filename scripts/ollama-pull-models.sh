#!/usr/bin/env sh
# Idempotent `ollama pull` for each model in OLLAMA_PULL_MODELS (space-separated).
# Loads .env when present so values can override defaults.

set -eu

FILES="$1"
ENVFILE="$2"
ROOT="$(CDPATH="" cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

DEFAULT_MODELS='llama3.2:latest mistral:7b'

# Do NOT `source .env` here — unquoted `KEY=a b` is parsed as assignment + command `b`.
OLLAMA_PULL_MODELS=""
if [ -f .env ]; then
  _line="$(grep '^[[:space:]]*OLLAMA_PULL_MODELS[[:space:]]*=' .env 2>/dev/null | tail -n1)"
  if [ -n "$_line" ]; then
    OLLAMA_PULL_MODELS="$(printf '%s' "$_line" | sed -e 's/^[^=]*=[[:space:]]*//' | tr -d '\r')"
    case "$OLLAMA_PULL_MODELS" in
      \"*)
        _t="${OLLAMA_PULL_MODELS#\"}"
        OLLAMA_PULL_MODELS="${_t%\"}"
        ;;
      \'*)
        _t="${OLLAMA_PULL_MODELS#\'}"
        OLLAMA_PULL_MODELS="${_t%\'}"
        ;;
    esac
  fi
fi
OLLAMA_PULL_MODELS="${OLLAMA_PULL_MODELS:-$DEFAULT_MODELS}"

echo "OLLAMA_PULL_MODELS: $OLLAMA_PULL_MODELS"
echo ''

for raw in $OLLAMA_PULL_MODELS; do
  m="$(printf '%s' "$raw" | tr -d '\r')"
  case "$m" in
    ''|\#*) continue ;;
  esac
  printf 'Pulling Ollama model: %s (skip if already present)\n' "$m"
  if ! docker compose $FILES $ENVFILE exec -T ollama ollama pull "$m"; then
    echo "WARNING: Pull failed for '$m'. Check disk space / network. Continuing with next model." >&2
  fi
  echo ''
done

echo 'Recommended: set OLLAMA_CHAT_MODEL in .env to the model name SmartNarrator.Api should call'
echo '(must match how Ollama lists it — often e.g. llama3.2 or llama3.2:latest depending on pull).'
