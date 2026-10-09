#!/usr/bin/env bash
# Reset the BIOLOG local Docker infrastructure (BIO-FND-003-AC04).
#
# Stops the stack and deletes its named volumes, so the next `infra-up.sh`
# starts from empty databases/queues/blob storage/mail. Named volumes make
# this deterministic: each service's data lives in exactly one known
# `biolog_*` volume (see infrastructure/docker-compose.local.yml), so a
# reset can target them by name instead of guessing at anonymous volumes.
#
# Usage:
#   scripts/infra-reset.sh                 # stop everything, wipe every volume
#   scripts/infra-reset.sh mysql-tenant-asu redis   # wipe just these services
#
# Deliberately avoids bash 4+ associative arrays: macOS ships bash 3.2 as
# /bin/bash (no `declare -A` support), and `#!/usr/bin/env bash` resolves to
# that on any machine without a newer bash earlier in PATH.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

COMPOSE_FILE="infrastructure/docker-compose.local.yml"
ENV_FILE="infrastructure/.env"
ALL_SERVICES="mysql-control mysql-tenant-asu mysql-tenant-grace redis rabbitmq azurite mailpit"

compose_args=(-f "$COMPOSE_FILE")
[ -f "$ENV_FILE" ] && compose_args+=(--env-file "$ENV_FILE")

volume_for_service() {
  case "$1" in
    mysql-control) echo "biolog_mysql_control" ;;
    mysql-tenant-asu) echo "biolog_mysql_tenant_asu" ;;
    mysql-tenant-grace) echo "biolog_mysql_tenant_grace" ;;
    redis) echo "biolog_redis_data" ;;
    rabbitmq) echo "biolog_rabbitmq_data" ;;
    azurite) echo "biolog_azurite_data" ;;
    mailpit) echo "biolog_mailpit_data" ;;
    *) echo "" ;;
  esac
}

# Removes $1's volume if it exists, treating "the volume was never created
# because that service was never started" as a legitimate no-op rather than
# a failure. Any OTHER Docker failure (BIO-FND-003-QA-02: cleanup must not
# be silently swallowed) is fatal.
#
# BIO-FND-003-QA-03: `docker volume inspect` exiting non-zero is NOT on its
# own proof the volume is absent - it also exits non-zero on a permission
# error, a daemon that's gone away, or any other transport failure. Only
# Docker's own "no such volume" diagnostic on stderr confirms genuine
# absence; every other inspect failure must fail closed here, not be
# silently treated the same as "nothing to clean up".
remove_volume_if_present() {
  local volume="$1" inspect_err inspect_rc=0
  inspect_err="$(docker volume inspect "$volume" 2>&1 >/dev/null)" || inspect_rc=$?
  if [ "$inspect_rc" -eq 0 ]; then
    if ! docker volume rm -f "$volume"; then
      echo "infra-reset: failed to remove volume $volume" >&2
      return 1
    fi
    return 0
  fi
  if printf '%s' "$inspect_err" | grep -qi "no such volume"; then
    return 0
  fi
  echo "infra-reset: could not confirm whether volume $volume exists: ${inspect_err:-docker volume inspect failed}" >&2
  return 1
}

if [ "$#" -eq 0 ]; then
  echo "infra-reset: stopping the stack and removing every named volume"
  if ! docker compose "${compose_args[@]}" --profile multi-tenant down; then
    echo "infra-reset: failed to stop the stack" >&2
    exit 1
  fi
  for service in $ALL_SERVICES; do
    remove_volume_if_present "$(volume_for_service "$service")" || exit 1
  done
  echo "infra-reset: done (all volumes removed)"
  exit 0
fi

count=0
for service in "$@"; do
  volume="$(volume_for_service "$service")"
  if [ -z "$volume" ]; then
    echo "infra-reset: unknown service '$service' (known: $ALL_SERVICES)" >&2
    exit 2
  fi
  echo "infra-reset: stopping $service and removing its volume $volume"
  if ! docker compose "${compose_args[@]}" --profile multi-tenant stop "$service"; then
    echo "infra-reset: failed to stop $service" >&2
    exit 1
  fi
  if ! docker compose "${compose_args[@]}" --profile multi-tenant rm -f "$service"; then
    echo "infra-reset: failed to remove the container for $service" >&2
    exit 1
  fi
  remove_volume_if_present "$volume" || exit 1
  count=$((count + 1))
done
echo "infra-reset: done ($count service volume(s) removed)"
