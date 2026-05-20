#!/usr/bin/env sh
# After .env exists: probe Docker+NVIDIA GPU access, then set OLLAMA_USE_GPU for compose overlays.

set -eu
ROOT="$(CDPATH="" cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

ENV_FILE=".env"

dotenv_upsert_bool() {
  _key="$1"
  _val="$2"
  _file="$3"
  _tmp="${_file}.tmp.$$"
  (grep -v "^${_key}=" "$_file" || true) > "$_tmp"
  printf '%s=%s\n' "$_key" "$_val" >> "$_tmp"
  mv "$_tmp" "$_file"
}

docker_gpu_probe() {
  docker info >/dev/null 2>&1 || return 1
  docker run --rm --gpus all alpine:3.19 true >/dev/null 2>&1
}

[ -f "$ENV_FILE" ] || {
  echo 'ERROR: configure-ollama-gpu-choice.sh expects .env (run env-bootstrap first).' >&2
  exit 1
}

if grep -q '^OLLAMA_USE_GPU=' "$ENV_FILE" 2>/dev/null; then
  _cur="$(grep '^OLLAMA_USE_GPU=' "$ENV_FILE" | tail -n1 | sed 's/^[^=]*=//' | tr -d '\r')"
  printf '%s\n' "Keeping existing .env OLLAMA_USE_GPU=${_cur}"
  exit 0
fi

if ! docker_gpu_probe; then
  printf '%s\n' \
    'Docker cannot run containers with NVIDIA GPUs (--gpus all probe failed).' \
    '  Common fixes: install NVIDIA drivers, install/configure NVIDIA Container Toolkit,' \
    '  restart Docker. Ollama will use CPU until GPU works — see README.' \
    ''
  dotenv_upsert_bool OLLAMA_USE_GPU 0 "$ENV_FILE"
  printf '%s\n' 'Recorded OLLAMA_USE_GPU=0 in .env (CPU). Edit to 1 after fixing GPU + toolkit.'
  exit 0
fi

printf '%s\n' 'Docker successfully accessed an NVIDIA GPU (appropriate for default Ollama LLMs).'

_choice=0
if [ -t 0 ]; then
  printf '%s' 'Enable GPU acceleration for the Ollama container? [Y/n] '
  read -r _reply || true
  _reply="$(printf '%s' "${_reply:-Y}" | tr '[:upper:]' '[:lower:]')"
  case "$_reply" in
    ''|y|yes) _choice=1 ;;
    *) _choice=0 ;;
  esac
else
  if [ "${CI:-}" = true ] || [ "${CI:-}" = 1 ]; then
    printf '%s\n' \
      'Non-interactive CI: leaving OLLAMA_USE_GPU unset (CPU path).' \
      '  Add OLLAMA_USE_GPU=1 to .env when runners have NVIDIA GPU support.'
    exit 0
  fi
  printf '%s\n' 'Non-interactive setup: enabling GPU for Ollama (OLLAMA_USE_GPU=1). Set OLLAMA_USE_GPU=0 in .env to force CPU.'
  _choice=1
fi

dotenv_upsert_bool OLLAMA_USE_GPU "$_choice" "$ENV_FILE"
printf '%s\n' "Recorded OLLAMA_USE_GPU=${_choice} in .env."
