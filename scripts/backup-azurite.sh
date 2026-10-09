#!/usr/bin/env bash
# Back up the BIOLOG local Azurite (Blob Storage emulator) volume - the
# object store BIO-OPS-009-AC03's "photo/object evidence restore" rehearses
# against. Backs up at the volume level (a tar of its data directory)
# rather than per-blob, since that is how a real disaster-recovery
# rehearsal for object storage actually works: you recover the storage
# account's data, not individually-replayed blob uploads.
#
# Usage: scripts/backup-azurite.sh [--out DIR] [--volume NAME] [--container NAME]
#   --out DIR       directory to write the tarball into (default: backups/azurite)
#   --volume NAME   volume to back up (default: biolog_azurite_data, the
#                   volume infrastructure/docker-compose.local.yml names)
#   --container NAME  the running Azurite container bound to that volume.
#                   When given, it is gracefully stopped before the tar
#                   snapshot and restarted immediately after.
#
# --container is not optional in practice if the container is running:
# confirmed directly against a live Azurite instance that its on-disk
# __azurite_db_blob__.json/__azurite_db_queue__.json metadata files only
# reflect newly created containers/blobs AFTER a graceful stop (its LokiJS
# persistence layer flushes on clean shutdown, not on every write) - a tar
# taken from a volume whose Azurite container is still running can
# silently back up a STALE or outright empty metadata snapshot even though
# the actual blob bytes are already on disk, because the index pointing to
# them was never flushed. This is the same "quiesce before you snapshot"
# requirement any backup of a stateful, write-buffering service has.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

out_dir="backups/azurite"
volume="biolog_azurite_data"
container=""
was_running=0
container_restart_confirmed=0

# FEMI-OPS009-01 (round 3): the round-2 fix only covered the archive
# command itself returning a nonzero exit code (guarded with `archive_rc`
# below). It did NOT cover the shell process receiving SIGINT/SIGTERM (or
# otherwise being killed) between `docker stop` and the restart later in
# this script - in that case bash exits immediately without ever reaching
# the restart code, leaving Azurite stopped: the exact outage class this
# story is supposed to prevent, just triggered a different way. This
# EXIT/INT/TERM trap is the "finally" block that covers that gap: it
# restarts and confirms readiness on ANY exit path, not only the one the
# script's own linear flow reaches. It is a no-op once the normal restart
# below has already confirmed readiness (`container_restart_confirmed=1`),
# so it never does redundant work on the happy path.
attempt_restart_and_confirm_ready() {
  local c="$1" deadline ready port
  echo "backup-azurite: restarting $c" >&2
  docker start "$c" >/dev/null 2>&1 || return 1
  deadline=$((SECONDS + 30))
  ready=0
  port="$(docker inspect -f '{{(index (index .NetworkSettings.Ports "10000/tcp") 0).HostPort}}' "$c" 2>/dev/null || true)"
  while [ "$SECONDS" -lt "$deadline" ]; do
    if [ -n "$port" ] && curl -s -o /dev/null "http://127.0.0.1:${port}/devstoreaccount1" 2>/dev/null; then
      ready=1
      break
    fi
    sleep 1
  done
  [ "$ready" -eq 1 ]
}

restart_on_exit_if_not_already_confirmed() {
  if [ "$was_running" -eq 1 ] && [ "$container_restart_confirmed" -ne 1 ] && [ -n "$container" ]; then
    echo "backup-azurite: EXIT/signal handler - the script is exiting without having confirmed $container back up, attempting restart now" >&2
    if attempt_restart_and_confirm_ready "$container"; then
      echo "backup-azurite: EXIT/signal handler - $container confirmed healthy" >&2
    else
      echo "backup-azurite: EXIT/signal handler - could not confirm $container healthy - manual intervention required" >&2
    fi
  fi
}
# Installed before the container is ever stopped (see below), so a signal
# arriving at any point from here onward - including mid-stop, mid-archive,
# or between the stop and the normal restart call - is covered.
trap restart_on_exit_if_not_already_confirmed EXIT
# Bound separately from EXIT: a trap bound directly to INT/TERM does not
# terminate the script on its own (it replaces the signal's default fatal
# action; the script resumes after the handler returns unless the handler
# itself calls exit) - found this the same way while fixing
# SY-OPS009-02 in scripts/disaster-recovery-rehearsal.sh. Calling exit here
# cascades into the EXIT trap above, so cleanup still runs exactly once.
trap 'exit 130' INT
trap 'exit 143' TERM

while [ "$#" -gt 0 ]; do
  case "$1" in
    --out) out_dir="$2"; shift 2 ;;
    --volume) volume="$2"; shift 2 ;;
    --container) container="$2"; shift 2 ;;
    *) echo "backup-azurite: unknown argument '$1'" >&2; exit 2 ;;
  esac
done

if ! docker volume inspect "$volume" >/dev/null 2>&1; then
  echo "backup-azurite: volume '$volume' does not exist (is Azurite running? scripts/infra-up.sh --wait)" >&2
  exit 1
fi

if [ -n "$container" ]; then
  if docker inspect -f '{{.State.Running}}' "$container" 2>/dev/null | grep -q true; then
    was_running=1
    echo "backup-azurite: stopping $container to flush its metadata store before snapshotting"
    docker stop -t 10 "$container" >/dev/null
  fi
fi

# The archive step's own exit status is captured without letting `set -e`
# act on it immediately, so the restart below always runs regardless of
# whether archiving succeeded (FEMI-OPS009-01, round 2).
archive_rc=0
mkdir -p "$out_dir" || archive_rc=$?
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
tarball="$out_dir/azurite-${timestamp}.tar.gz"
manifest_file="${tarball}.manifest.json"

if [ "$archive_rc" -eq 0 ]; then
  echo "backup-azurite: archiving volume $volume -> $tarball"
  # Mounted read-only: a backup must never be able to mutate the volume it
  # is backing up, even by accident.
  docker run --rm \
    -v "${volume}:/data:ro" \
    -v "$(pwd)/$out_dir:/backup" \
    busybox tar czf "/backup/$(basename "$tarball")" -C /data . || archive_rc=$?
fi

if [ "$was_running" -eq 1 ]; then
  if attempt_restart_and_confirm_ready "$container"; then
    container_restart_confirmed=1
  else
    echo "backup-azurite: $container restart/readiness did not succeed - treating as failure, not a warning" >&2
    exit 1
  fi
fi

if [ "$archive_rc" -ne 0 ]; then
  echo "backup-azurite: archive step failed (exit $archive_rc) - $container was restarted, but no backup was produced" >&2
  exit "$archive_rc"
fi

size="$(wc -c < "$tarball" | tr -d ' ')"
digest="$(shasum -a 256 "$tarball" | awk '{print $1}')"
completed_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "$manifest_file" <<EOF
{
  "volume": "$volume",
  "tarball": "$(basename "$tarball")",
  "bytes": $size,
  "sha256": "$digest",
  "backup_completed_at": "$completed_at"
}
EOF
echo "backup-azurite: done ($size bytes, sha256 $digest, completed $completed_at)"
