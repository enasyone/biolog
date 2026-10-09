#!/usr/bin/env bash
# Back up one or more BIOLOG local MySQL services, each independently
# (BIO-OPS-009-AC01: "Control-plane and tenant backups are independently
# restorable" - a precondition for that is that they are independently
# BACKED UP in the first place, one dump per service/database, never a
# combined dump that would force restoring everything together).
#
# Usage: scripts/backup-mysql.sh [--out DIR] [--project NAME] [service...]
#   --out DIR      directory to write dumps into (default: backups/mysql)
#   --project NAME docker compose project name to target (default: the
#                  compose file's own pinned `name: biolog` - pass this
#                  when backing up an isolated rehearsal stack started
#                  under a different project name, e.g. from
#                  scripts/disaster-recovery-rehearsal.sh, so this never
#                  touches a developer's already-running default stack)
#   service...     one or more of mysql-control, mysql-tenant-asu,
#                  mysql-tenant-grace (default: every one of these that is
#                  currently running under the targeted project)
#
# Each dump is written as <out>/<service>-<database>-<timestamp>.sql.gz,
# alongside a <...>.manifest.json recording the service, database, dump
# byte size and sha256, and the UTC timestamp backup completed - the
# manifest's timestamp is what scripts/disaster-recovery-rehearsal.sh treats
# as the RPO reference point for that service (BIO-OPS-009-AC04).
#
# Deliberately avoids bash 4+ associative arrays (macOS ships bash 3.2 as
# /bin/bash; see docs/LOCAL-INFRASTRUCTURE.md and every sibling infra
# script for the same constraint).
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

COMPOSE_FILE="infrastructure/docker-compose.local.yml"
ENV_FILE="infrastructure/.env"
OUT_DIR="backups/mysql"

# Mirrors infra-up.sh's own --env-file handling for `docker compose`
# commands, but mysqldump runs via `docker exec` (not compose), which never
# sees infrastructure/.env's substitutions - so the root password is read
# into this shell directly, the same KEY=VALUE file format
# infrastructure/.env.example documents.
if [ -f "$ENV_FILE" ]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
fi
MYSQL_ROOT_PASSWORD="${MYSQL_ROOT_PASSWORD:-localroot}"

database_for_service() {
  case "$1" in
    mysql-control) echo "biolog_control" ;;
    mysql-tenant-asu) echo "biolog_asu" ;;
    mysql-tenant-grace) echo "biolog_grace" ;;
    *) echo "" ;;
  esac
}

services=""
out_dir="$OUT_DIR"
project=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --out) out_dir="$2"; shift 2 ;;
    --project) project="$2"; shift 2 ;;
    mysql-control|mysql-tenant-asu|mysql-tenant-grace) services="$services $1"; shift ;;
    *) echo "backup-mysql: unknown argument '$1'" >&2; exit 2 ;;
  esac
done

compose_args=(-f "$COMPOSE_FILE")
[ -f "$ENV_FILE" ] && compose_args+=(--env-file "$ENV_FILE")
[ -n "$project" ] && compose_args=(-p "$project" "${compose_args[@]}")

if [ -z "$services" ]; then
  for candidate in mysql-control mysql-tenant-asu mysql-tenant-grace; do
    cid="$(docker compose "${compose_args[@]}" --profile multi-tenant ps -q "$candidate" 2>/dev/null)"
    [ -n "$cid" ] && services="$services $candidate"
  done
fi

if [ -z "$services" ]; then
  echo "backup-mysql: no running mysql-* service found (is the stack up? scripts/infra-up.sh --wait)" >&2
  exit 1
fi

mkdir -p "$out_dir"
backed_up=0
for service in $services; do
  database="$(database_for_service "$service")"
  if [ -z "$database" ]; then
    echo "backup-mysql: unknown service '$service'" >&2
    exit 2
  fi
  cid="$(docker compose "${compose_args[@]}" --profile multi-tenant ps -q "$service" 2>/dev/null)"
  if [ -z "$cid" ]; then
    echo "backup-mysql: $service is not running, skipping" >&2
    continue
  fi

  timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
  dump_file="$out_dir/${service}-${database}-${timestamp}.sql.gz"
  manifest_file="${dump_file}.manifest.json"

  echo "backup-mysql: dumping $service ($database) -> $dump_file"
  if ! docker compose "${compose_args[@]}" --profile multi-tenant exec -T "$service" \
      mysqldump -u root -p"$MYSQL_ROOT_PASSWORD" --single-transaction --databases "$database" \
      | gzip -9 > "$dump_file"; then
    echo "backup-mysql: mysqldump failed for $service" >&2
    rm -f "$dump_file"
    exit 1
  fi

  size="$(wc -c < "$dump_file" | tr -d ' ')"
  digest="$(shasum -a 256 "$dump_file" | awk '{print $1}')"
  completed_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  cat > "$manifest_file" <<EOF
{
  "service": "$service",
  "database": "$database",
  "dump_file": "$(basename "$dump_file")",
  "bytes": $size,
  "sha256": "$digest",
  "backup_completed_at": "$completed_at"
}
EOF
  echo "backup-mysql: $service done ($size bytes, sha256 $digest, completed $completed_at)"
  backed_up=$((backed_up + 1))
done

echo "backup-mysql: $backed_up service(s) backed up independently into $out_dir"
