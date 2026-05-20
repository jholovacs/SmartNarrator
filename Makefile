# SmartNarrator — Docker workflow
# Requires: Docker daemon + Compose v2 (`docker compose`).
# Run from repo root on macOS/Linux or Git Bash/WSL on Windows.

.PHONY: setup deploy docker-check

docker-check:
	@sh "$(CURDIR)/scripts/docker-check.sh"

# First-time / full stack: create .env, build all services, start stack, pull Ollama models.
setup:
	@sh "$(CURDIR)/scripts/make-setup.sh"

# Iterative rebuild: only api + SPA images recreated and services restarted (DB/Ollama left running).
deploy:
	@sh "$(CURDIR)/scripts/make-deploy.sh"
