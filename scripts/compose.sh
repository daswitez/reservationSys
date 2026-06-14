#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker/docker-compose.yml"
cd "$ROOT_DIR"

if [[ -f "$ROOT_DIR/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "$ROOT_DIR/.env"
  set +a
fi

if docker compose version >/dev/null 2>&1; then
  exec docker compose -p reservas-mvp -f "$COMPOSE_FILE" "$@"
fi

exec docker-compose -p reservas-mvp -f "$COMPOSE_FILE" "$@"
