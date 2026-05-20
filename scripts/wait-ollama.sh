#!/usr/bin/env sh
# Waits until the ollama service accepts `ollama list` (daemon ready inside container).

set -eu

FILES="$1"
ENVFILE="$2"
TRIES="${OLLAMA_READY_TRIES:-40}"
DELAY="${OLLAMA_READY_DELAY:-2}"

i=0
while [ "$i" -lt "$TRIES" ]; do
  if docker compose $FILES $ENVFILE exec -T ollama ollama list >/dev/null 2>&1; then
    exit 0
  fi
  i=$((i + 1))
  sleep "$DELAY"
done

echo 'ERROR: Ollama container did not become ready in time (`ollama list` still failing).' >&2
echo 'Check: docker compose logs ollama' >&2
exit 1
