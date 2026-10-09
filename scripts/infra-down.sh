#!/usr/bin/env bash
# Stop the BIOLOG local Docker infrastructure without deleting its data.
# Usage: scripts/infra-down.sh
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

COMPOSE_FILE="infrastructure/docker-compose.local.yml"
ENV_FILE="infrastructure/.env"

compose_args=(-f "$COMPOSE_FILE")
[ -f "$ENV_FILE" ] && compose_args+=(--env-file "$ENV_FILE")

# --profile multi-tenant is included so `down` also stops mysql-tenant-grace
# when it was previously started; docker compose down is a no-op for
# services that were never created.
docker compose "${compose_args[@]}" --profile multi-tenant down
