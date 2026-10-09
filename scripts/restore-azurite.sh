#!/usr/bin/env bash
# Restore an Azurite volume tarball into a BRAND-NEW named volume and a
# fresh Azurite container (BIO-OPS-009-AC02/AC03) - never the original
# volume/container, same rule as scripts/restore-mysql.sh.
#
# Usage:
#   scripts/restore-azurite.sh --tarball FILE.tar.gz --volume NAME --container NAME --port PORT
#
# Starting the container only proves the service comes back up; it does
# NOT by itself prove the restored blobs are byte-for-byte intact - that
# verification is scripts/disaster-recovery-rehearsal.sh's job, via
# scripts/azurite-rest.py get-blob + a sha256 compare against the manifest
# recorded at backup time.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

tarball="" volume="" container="" port=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --tarball) tarball="$2"; shift 2 ;;
    --volume) volume="$2"; shift 2 ;;
    --container) container="$2"; shift 2 ;;
    --port) port="$2"; shift 2 ;;
    *) echo "restore-azurite: unknown argument '$1'" >&2; exit 2 ;;
  esac
done

if [ -z "$tarball" ] || [ -z "$volume" ] || [ -z "$container" ] || [ -z "$port" ]; then
  echo "restore-azurite: --tarball, --volume, --container and --port are all required" >&2
  exit 2
fi
[ -f "$tarball" ] || { echo "restore-azurite: tarball '$tarball' not found" >&2; exit 2; }

start_epoch="$(date -u +%s)"
tarball_abs="$(cd "$(dirname "$tarball")" && pwd)/$(basename "$tarball")"

# SY-OPS009-02: same guard as restore-mysql.sh - `docker volume create`
# silently succeeds against an existing name, which would make "always a
# brand-new volume" a happy-path-only claim. Refuse outright rather than
# risk adopting stale data left by an interrupted prior run.
if docker volume inspect "$volume" >/dev/null 2>&1; then
  echo "restore-azurite: volume '$volume' already exists - refusing to reuse it, pass a different --volume name" >&2
  exit 1
fi
echo "restore-azurite: creating fresh volume $volume"
docker volume create "$volume" >/dev/null

echo "restore-azurite: extracting $tarball into $volume"
docker run --rm \
  -v "${volume}:/data" \
  -v "$(dirname "$tarball_abs"):/backup:ro" \
  busybox tar xzf "/backup/$(basename "$tarball_abs")" -C /data

echo "restore-azurite: starting fresh container $container (port $port) bound to $volume"
# --location /data is mandatory here: Azurite defaults location to its
# process's current working directory, NOT the mounted volume path, if
# --location is omitted (confirmed via `azurite-blob --help`) - without it,
# a freshly started container silently opens a brand-new, empty metadata
# database instead of the one just restored into /data, and every blob
# lookup afterward fails with ContainerNotFound even though the restored
# files are sitting right there on disk.
docker run -d --name "$container" \
  -p "${port}:10000" \
  -v "${volume}:/data" \
  mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0 --location /data >/dev/null

echo "restore-azurite: waiting for $container to accept connections"
deadline=$((SECONDS + 60))
ready=0
while [ "$SECONDS" -lt "$deadline" ]; do
  if curl -s -o /dev/null "http://127.0.0.1:${port}/devstoreaccount1" 2>/dev/null; then
    ready=1
    break
  fi
  sleep 1
done
if [ "$ready" -ne 1 ]; then
  echo "restore-azurite: $container never accepted connections within 60s" >&2
  exit 1
fi

end_epoch="$(date -u +%s)"
elapsed=$((end_epoch - start_epoch))
echo "restore-azurite: done - $container ready on port $port, restore took ${elapsed}s (fresh volume $volume, fresh container, never the original)"
