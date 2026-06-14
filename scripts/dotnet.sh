#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if command -v dotnet >/dev/null 2>&1; then
  exec dotnet "$@"
fi

exec "$ROOT_DIR/.tools/dotnet/dotnet" "$@"
