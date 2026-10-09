#!/usr/bin/env bash
# Restore a mysqldump backup into a BRAND-NEW, standalone MySQL container
# and a brand-new named volume - never the existing compose-managed
# container/volume (BIO-OPS-009-AC02: "Restore to new local containers is
# documented" - documented here AND actually exercised, not just written
# down; see docs/BACKUP-RESTORE-REHEARSAL.md for the narrative walkthrough).
#
# Usage:
#   scripts/restore-mysql.sh --dump FILE.sql.gz --container NAME --volume NAME --port PORT [--password PW]
#
# Deliberately does not reuse infrastructure/docker-compose.local.yml at
# all: a genuine disaster-recovery rehearsal must prove data comes back on
# a container/volume that never existed before, not merely that the
# original compose service restarts cleanly.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

dump="" container="" volume="" port="" password="${MYSQL_ROOT_PASSWORD:-localroot}"
timeout="${RESTORE_MYSQL_TIMEOUT:-120}"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --dump) dump="$2"; shift 2 ;;
    --container) container="$2"; shift 2 ;;
    --volume) volume="$2"; shift 2 ;;
    --port) port="$2"; shift 2 ;;
    --password) password="$2"; shift 2 ;;
    *) echo "restore-mysql: unknown argument '$1'" >&2; exit 2 ;;
  esac
done

if [ -z "$dump" ] || [ -z "$container" ] || [ -z "$volume" ] || [ -z "$port" ]; then
  echo "restore-mysql: --dump, --container, --volume and --port are all required" >&2
  exit 2
fi
[ -f "$dump" ] || { echo "restore-mysql: dump file '$dump' not found" >&2; exit 2; }

start_epoch="$(date -u +%s)"

# SY-OPS009-02: `docker volume create` succeeds (and silently reuses
# whatever is already there) for an existing name - that would make
# "always restores to a brand-new volume" a happy-path-only claim. Refuse
# outright rather than silently adopting stale data from an interrupted
# prior run (cleanup_rehearsal_stack's EXIT/INT/TERM trap in
# disaster-recovery-rehearsal.sh is the normal guard against that ever
# happening; this is the defense-in-depth backstop for every other caller,
# including a direct standalone invocation of this script).
if docker volume inspect "$volume" >/dev/null 2>&1; then
  echo "restore-mysql: volume '$volume' already exists - refusing to reuse it, pass a different --volume name" >&2
  exit 1
fi
echo "restore-mysql: creating fresh volume $volume"
docker volume create "$volume" >/dev/null

echo "restore-mysql: starting fresh container $container (image mysql:8.4, port $port) bound to $volume"
docker run -d --name "$container" \
  -e "MYSQL_ROOT_PASSWORD=$password" \
  -p "${port}:3306" \
  -v "${volume}:/var/lib/mysql" \
  mysql:8.4 >/dev/null

echo "restore-mysql: waiting for $container to accept connections (timeout ${timeout}s)"
# The official mysql image restarts mysqld once after first-boot
# initialization. mysqladmin ping can succeed once against the transient
# bootstrap instance, report healthy, then fail again briefly across the
# restart boundary - so this requires 3 CONSECUTIVE successful pings,
# spaced out, before trusting readiness, rather than trusting the first one.
deadline=$((SECONDS + timeout))
consecutive=0
ready=0
while [ "$SECONDS" -lt "$deadline" ]; do
  if docker exec "$container" mysqladmin ping -h localhost -p"$password" --silent >/dev/null 2>&1; then
    consecutive=$((consecutive + 1))
    if [ "$consecutive" -ge 3 ]; then
      ready=1
      break
    fi
  else
    consecutive=0
  fi
  sleep 2
done
if [ "$ready" -ne 1 ]; then
  echo "restore-mysql: $container never became stably ready within ${timeout}s" >&2
  exit 1
fi

echo "restore-mysql: restoring $dump into $container"
restored=0
for attempt in 1 2 3 4 5; do
  if gunzip -c "$dump" | docker exec -i "$container" mysql -u root -p"$password"; then
    restored=1
    break
  fi
  echo "restore-mysql: restore attempt $attempt/5 failed, retrying in 3s" >&2
  sleep 3
done
if [ "$restored" -ne 1 ]; then
  echo "restore-mysql: mysql restore failed after 5 attempts" >&2
  exit 1
fi

end_epoch="$(date -u +%s)"
elapsed=$((end_epoch - start_epoch))
echo "restore-mysql: done - $container ready on port $port, restore took ${elapsed}s (fresh volume $volume, fresh container, never the original)"
