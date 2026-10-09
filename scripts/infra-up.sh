#!/usr/bin/env bash
# Start the BIOLOG local Docker infrastructure (BIO-FND-003-AC01/AC02).
#
# Usage: scripts/infra-up.sh [--multi-tenant] [--wait]
#   --multi-tenant  also start mysql-tenant-grace, the second isolated
#                   tenant database, to validate cross-tenant isolation.
#   --wait          block until every started service reports healthy
#                   (calls scripts/wait-for-infra.sh); otherwise returns as
#                   soon as containers are created, before they are ready.
#
# Deliberately avoids expanding a possibly-empty bash array under `set -u`:
# macOS's default /bin/bash (3.2) treats `"${arr[@]}"` on an empty array as
# an unbound-variable error, unlike bash 4+. `multi_tenant`/`profile_flag`
# below are plain strings for this reason, not arrays.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

COMPOSE_FILE="infrastructure/docker-compose.local.yml"
ENV_FILE="infrastructure/.env"
multi_tenant=0
wait_after=0

for arg in "$@"; do
  case "$arg" in
    --multi-tenant) multi_tenant=1 ;;
    --wait) wait_after=1 ;;
    *) echo "infra-up: unknown argument '$arg'" >&2; exit 2 ;;
  esac
done

compose_args=(-f "$COMPOSE_FILE")
[ -f "$ENV_FILE" ] && compose_args+=(--env-file "$ENV_FILE")

profile_flag=""
[ "$multi_tenant" -eq 1 ] && profile_flag="--profile multi-tenant"

docker compose "${compose_args[@]}" $profile_flag up -d

if [ "$wait_after" -eq 1 ]; then
  if [ "$multi_tenant" -eq 1 ]; then
    scripts/wait-for-infra.sh --multi-tenant
  else
    scripts/wait-for-infra.sh
  fi
fi
