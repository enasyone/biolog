#!/usr/bin/env bash
# Block until every started BIOLOG infrastructure container reports healthy
# (BIO-FND-003-AC03: "bootstrap waits for readiness").
#
# Usage: scripts/wait-for-infra.sh [--multi-tenant] [--timeout SECONDS]
#
# Deliberately avoids expanding a possibly-empty bash array under `set -u`:
# macOS's default /bin/bash (3.2) treats `"${arr[@]}"` on an empty array as
# an unbound-variable error, unlike bash 4+. `profile_flag` below is a plain
# string for this reason, not an array.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

COMPOSE_FILE="infrastructure/docker-compose.local.yml"
ENV_FILE="infrastructure/.env"
timeout=180
profile_flag=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --multi-tenant) profile_flag="--profile multi-tenant"; shift ;;
    --timeout) timeout="$2"; shift 2 ;;
    *) echo "wait-for-infra: unknown argument '$1'" >&2; exit 2 ;;
  esac
done

compose_args=(-f "$COMPOSE_FILE")
[ -f "$ENV_FILE" ] && compose_args+=(--env-file "$ENV_FILE")

services="$(docker compose "${compose_args[@]}" $profile_flag config --services)"
if [ -z "$services" ]; then
  echo "wait-for-infra: no services resolved from $COMPOSE_FILE" >&2
  exit 2
fi

deadline=$((SECONDS + timeout))
pending=""
while [ "$SECONDS" -lt "$deadline" ]; do
  pending=""
  for service in $services; do
    cid="$(docker compose "${compose_args[@]}" $profile_flag ps -q "$service" 2>/dev/null)"
    if [ -z "$cid" ]; then
      pending="$pending $service(not-started)"
      continue
    fi
    status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck{{end}}' "$cid" 2>/dev/null || echo "unknown")"
    # BIO-FND-003-QA-01: every service in this compose file declares a
    # healthcheck by design, so a service reporting "no-healthcheck" at
    # runtime means the health contract is missing or broken - readiness
    # must fail/timeout on that, not silently treat it as ready.
    if [ "$status" != "healthy" ]; then
      pending="$pending $service($status)"
    fi
  done
  if [ -z "$pending" ]; then
    echo "wait-for-infra: all services ready ($services)"
    exit 0
  fi
  sleep 2
done

echo "wait-for-infra: timed out after ${timeout}s waiting on:$pending" >&2
exit 1
