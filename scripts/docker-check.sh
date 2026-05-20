#!/usr/bin/env sh
# Validates Docker CLI and daemon. Exits non-zero with guidance on failure.

if ! command -v docker >/dev/null 2>&1; then
  printf '%s\n' \
    'ERROR: The `docker` command was not found on your PATH.' \
    '' \
    'Install Docker Desktop or the Docker Engine, then reopen your terminal:' \
    '  https://docs.docker.com/engine/install/' \
    '  https://docs.docker.com/desktop/' >&2
  exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
  printf '%s\n' \
    'ERROR: `docker compose` is not available (Compose V2).' \
    '' \
    'Install the Docker Compose plugin that ships with Docker Desktop / recent Engine.' >&2
  exit 1
fi

if ! docker info >/dev/null 2>&1; then
  printf '%s\n' \
    'ERROR: Docker is installed but the daemon is not reachable (is Docker running?).' \
    '' \
    'Start Docker Desktop, or enable & start dockerd (`sudo service docker start` on Linux), then retry.' >&2
  exit 1
fi
