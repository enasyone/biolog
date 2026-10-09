#!/usr/bin/env bash
# BIO-OPS-009: local backup/restore and disaster-recovery rehearsal.
#
# Runs the full rehearsal end to end against a throwaway, fully isolated
# copy of the BIOLOG local stack (its own docker compose project name and
# its own ports, so this never touches a developer's already-running
# default-named "biolog" stack):
#
#   1. Bring up an isolated mysql-control / mysql-tenant-asu / azurite stack.
#   2. Seed synthetic rows/blobs into each (AC01, AC03 - "photo/object
#      evidence" is synthesized here, since no application code writes
#      real blobs to Azurite yet; see docs/BACKUP-RESTORE-REHEARSAL.md).
#      Captures a full-row-content snapshot of each table at this point -
#      the thing restore is actually checked against (SY-OPS009-01: a
#      restore that preserves ids but corrupts other columns must FAIL).
#   3. Back up each MySQL service independently and the Azurite volume
#      (AC01). Each backup's own completion timestamp (from its manifest)
#      is what AC04's RPO is computed from later - not a reference point
#      shared loosely across stores.
#   4. Write MORE synthetic data (simulating ongoing production writes
#      after the backup) - this is the data the rehearsal will prove is
#      genuinely, boundedly lost.
#   5. Declare a disaster: stop and remove every original container and
#      volume - a real simulated total loss, not a restart.
#   6. Restore every backup into BRAND-NEW containers/volumes (AC02) -
#      restore-mysql.sh/restore-azurite.sh refuse a pre-existing volume
#      name outright rather than silently reusing it (SY-OPS009-02).
#   7. Verify restored data matches the pre-backup snapshot EXACTLY (full
#      row content, not just primary keys), including that the post-backup
#      writes are correctly ABSENT (AC01/AC03 - "restore is verified", not
#      just "restore ran").
#   8. Compute and record observed RPO (per store, from that store's own
#      backup-completion timestamp through the disaster) and RTO for this
#      one run, explicitly labeled as a local rehearsal result, never a
#      production SLO (AC04).
#   9. Tear down every rehearsal container/volume/network it created - on
#      ANY exit path (normal completion, a failed step, or an interrupt),
#      via an EXIT/INT/TERM trap, not just the happy path
#      (SY-OPS009-02: a failure or Ctrl-C must never leak restore
#      containers/volumes, which would otherwise block or silently poison
#      the next run).
#
# Usage: scripts/disaster-recovery-rehearsal.sh [--keep-artifacts]
#   --keep-artifacts   do not delete backups/ops009-rehearsal/ afterward
#                      (evidence JSON/markdown are always kept, under
#                      evidence/BIO-OPS-009/builder/ via a separate step)
#
# All data is synthetic (BIO-OPS-009 constraint: "Use synthetic data only").
# Requires Docker; run with the sandbox disabled if invoked by an agent -
# see docs/LOCAL-INFRASTRUCTURE.md's "dotnet build hangs" note for the same
# class of sandbox restriction on Docker socket access.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

COMPOSE_FILE="infrastructure/docker-compose.local.yml"
PROJECT="biolog-ops009-rehearsal"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
WORK_DIR="backups/ops009-rehearsal/$RUN_ID"
RESULT_JSON="$WORK_DIR/result.json"

# Standalone (non-compose-managed) restore resources created in Phase 6.
# Named once here so both the restore calls and cleanup_rehearsal_stack
# (and its EXIT/INT/TERM trap) always agree on exactly what to remove.
RESTORE_CONTROL_CONTAINER="ops009-rehearsal-control-restored"
RESTORE_ASU_CONTAINER="ops009-rehearsal-asu-restored"
RESTORE_AZURITE_CONTAINER="ops009-rehearsal-azurite-restored"
RESTORE_CONTROL_VOLUME="ops009_rehearsal_control_restored_${RUN_ID}"
RESTORE_ASU_VOLUME="ops009_rehearsal_asu_restored_${RUN_ID}"
RESTORE_AZURITE_VOLUME="ops009_rehearsal_azurite_restored_${RUN_ID}"

keep_artifacts=0
for arg in "$@"; do
  case "$arg" in
    --keep-artifacts) keep_artifacts=1 ;;
    *) echo "disaster-recovery-rehearsal: unknown argument '$arg'" >&2; exit 2 ;;
  esac
done

mkdir -p "$WORK_DIR"

# Dedicated, uncommon ports - never infrastructure/.env's values, never the
# compose defaults - so this never collides with (or recreates) whatever a
# developer already has running (same technique BIO-FND-003's own live
# smoke test in tests/foundation/BIO-FND-003.test.sh uses).
export MYSQL_ROOT_PASSWORD="localroot"
export MYSQL_CONTROL_PORT=48061
export MYSQL_TENANT_ASU_PORT=48062
export AZURITE_BLOB_PORT=48000
export AZURITE_QUEUE_PORT=48001
export REDIS_PORT=48079
export RABBITMQ_AMQP_PORT=48072
export RABBITMQ_MGMT_PORT=48073
export MAILPIT_SMTP_PORT=48025
export MAILPIT_UI_PORT=48026

compose() { docker compose -p "$PROJECT" -f "$COMPOSE_FILE" "$@"; }

# SY-OPS009-02: this is the ONLY place that tears down rehearsal
# resources, and it runs on every exit path via the trap below - not just
# when fail() happens to call it, and not only at the bottom of a
# successful run. A failure, an interrupt (Ctrl-C) or a normal finish all
# converge here, so the standalone restore containers/volumes created in
# Phase 6 can never leak and block (or silently poison, via a stale
# same-named volume) the next run.
cleanup_rehearsal_stack() {
  echo "disaster-recovery-rehearsal: cleaning up standalone restore containers/volumes"
  docker rm -f ops009-rehearsal-control-restored ops009-rehearsal-asu-restored ops009-rehearsal-azurite-restored >/dev/null 2>&1 || true
  docker volume rm -f "$RESTORE_CONTROL_VOLUME" "$RESTORE_ASU_VOLUME" "$RESTORE_AZURITE_VOLUME" >/dev/null 2>&1 || true
  echo "disaster-recovery-rehearsal: tearing down isolated stack (project $PROJECT)"
  compose down -v --remove-orphans >/dev/null 2>&1 || true
}
# A trap bound directly to INT/TERM replaces the signal's DEFAULT fatal
# action - the script does not actually stop unless the handler itself
# calls exit, and cleanup_rehearsal_stack doesn't. Binding cleanup only to
# EXIT (which fires for every termination path, including one reached via
# `exit` inside a signal handler) and having INT/TERM explicitly exit is
# what makes Ctrl-C/SIGTERM actually stop the rehearsal AND still run
# cleanup exactly once, rather than silently resuming the script after the
# interrupt handler returns.
trap cleanup_rehearsal_stack EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

fail() {
  echo "disaster-recovery-rehearsal: FAILED - $1" >&2
  exit 1
}

echo "=== Phase 1: bring up an isolated rehearsal stack (project $PROJECT) ==="
compose up -d mysql-control mysql-tenant-asu azurite || fail "could not start isolated stack"

deadline=$((SECONDS + 180))
pending="start"
while [ "$SECONDS" -lt "$deadline" ] && [ -n "$pending" ]; do
  pending=""
  for svc in mysql-control mysql-tenant-asu azurite; do
    cid="$(compose ps -q "$svc" 2>/dev/null)"
    [ -z "$cid" ] && { pending="$pending $svc"; continue; }
    status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck{{end}}' "$cid" 2>/dev/null)"
    [ "$status" != "healthy" ] && pending="$pending $svc"
  done
  [ -n "$pending" ] && sleep 2
done
[ -n "$pending" ] && fail "services never became healthy:$pending"
echo "disaster-recovery-rehearsal: isolated stack healthy"

# The official mysql image restarts mysqld once after first-boot
# initialization; Docker's healthcheck can (rarely) report "healthy" right
# at that restart boundary, making the very next connection attempt fail
# even though the service is genuinely up moments later. Retrying a few
# times is the correct operational response to that, not a one-shot fail.
mysql_exec() {
  local service="$1" db="$2" sql="$3" attempt out rc
  for attempt in 1 2 3 4 5; do
    if out="$(docker compose -p "$PROJECT" -f "$COMPOSE_FILE" exec -T "$service" \
        mysql -u root -p"$MYSQL_ROOT_PASSWORD" "$db" -e "$sql" 2>&1)"; then
      return 0
    fi
    rc=$?
    echo "mysql_exec: $service attempt $attempt/5 failed, retrying in 3s: $out" >&2
    sleep 3
  done
  echo "$out" >&2
  return "$rc"
}

# SY-OPS009-01: reads back the full row content (not just ids) for a
# table, via mysql_exec's own retry-safe connection, so Phase 7 compares
# the restored data against this exact snapshot - a restore that preserves
# primary keys but corrupts/truncates any other column must FAIL this
# comparison, not silently pass an ids-only check.
mysql_select_rows() {
  local service="$1" db="$2" sql="$3" attempt out rc
  for attempt in 1 2 3 4 5; do
    if out="$(docker compose -p "$PROJECT" -f "$COMPOSE_FILE" exec -T "$service" \
        mysql -u root -p"$MYSQL_ROOT_PASSWORD" -N "$db" -e "$sql" 2>/dev/null)"; then
      printf '%s' "$out"
      return 0
    fi
    rc=$?
    sleep 3
  done
  return "$rc"
}

echo "=== Phase 2: seed synthetic pre-backup data ==="
mysql_exec mysql-control biolog_control "
    CREATE TABLE IF NOT EXISTS rehearsal_tenants (id INT PRIMARY KEY, name VARCHAR(64));
    INSERT INTO rehearsal_tenants (id, name) VALUES (1, 'synthetic-tenant-asu'), (2, 'synthetic-tenant-grace');
  " || fail "could not seed mysql-control"

mysql_exec mysql-tenant-asu biolog_asu "
    CREATE TABLE IF NOT EXISTS rehearsal_persons (id INT PRIMARY KEY, label VARCHAR(64));
    INSERT INTO rehearsal_persons (id, label) VALUES (1, 'synthetic-person-1'), (2, 'synthetic-person-2'), (3, 'synthetic-person-3');
  " || fail "could not seed mysql-tenant-asu"

control_rows_at_backup="$(mysql_select_rows mysql-control biolog_control \
  "SELECT id, name FROM biolog_control.rehearsal_tenants ORDER BY id;")" || fail "could not capture pre-backup snapshot (control)"
asu_rows_at_backup="$(mysql_select_rows mysql-tenant-asu biolog_asu \
  "SELECT id, label FROM biolog_asu.rehearsal_persons ORDER BY id;")" || fail "could not capture pre-backup snapshot (tenant-asu)"

mkdir -p "$WORK_DIR/fixtures"
printf 'synthetic photo evidence fixture #1 - not a real photo, BIO-OPS-009 rehearsal %s\n' "$RUN_ID" > "$WORK_DIR/fixtures/evidence-1.bin"
printf 'synthetic photo evidence fixture #2 - not a real photo, BIO-OPS-009 rehearsal %s\n' "$RUN_ID" > "$WORK_DIR/fixtures/evidence-2.bin"
python3 scripts/azurite-rest.py --port "$AZURITE_BLOB_PORT" put-container --container ops009-evidence || fail "could not create blob container"
python3 scripts/azurite-rest.py --port "$AZURITE_BLOB_PORT" put-blob --container ops009-evidence --blob evidence-1.bin --file "$WORK_DIR/fixtures/evidence-1.bin" || fail "could not seed blob 1"
python3 scripts/azurite-rest.py --port "$AZURITE_BLOB_PORT" put-blob --container ops009-evidence --blob evidence-2.bin --file "$WORK_DIR/fixtures/evidence-2.bin" || fail "could not seed blob 2"
evidence_1_sha="$(shasum -a 256 "$WORK_DIR/fixtures/evidence-1.bin" | awk '{print $1}')"
evidence_2_sha="$(shasum -a 256 "$WORK_DIR/fixtures/evidence-2.bin" | awk '{print $1}')"
echo "disaster-recovery-rehearsal: seeded 2 rows (control), 3 rows (tenant-asu), 2 blobs (azurite); captured full-row pre-backup snapshots"

echo "=== Phase 3: independent backups ==="
bash scripts/backup-mysql.sh --out "$WORK_DIR/backups/mysql" --project "$PROJECT" mysql-control mysql-tenant-asu || fail "mysql backup failed"
azurite_cid="$(compose ps -q azurite 2>/dev/null)"
bash scripts/backup-azurite.sh --out "$WORK_DIR/backups/azurite" --volume "${PROJECT}_azurite_data" --container "$azurite_cid" || fail "azurite backup failed"

control_dump="$(ls "$WORK_DIR"/backups/mysql/mysql-control-*.sql.gz 2>/dev/null | head -1)"
asu_dump="$(ls "$WORK_DIR"/backups/mysql/mysql-tenant-asu-*.sql.gz 2>/dev/null | head -1)"
azurite_tarball="$(ls "$WORK_DIR"/backups/azurite/azurite-*.tar.gz 2>/dev/null | head -1)"
[ -n "$control_dump" ] && [ -n "$asu_dump" ] && [ -n "$azurite_tarball" ] || fail "expected backup artifacts missing after backup phase"

# FEMI-OPS009-02: AC04's RPO must be measured from each store's own actual
# backup-completion timestamp (already written to its manifest), not a
# single timestamp sampled loosely across all three stores and not a
# hard-coded row count that only re-confirms the rehearsal's own fixture.
manifest_field() { python3 -c "import json,sys; print(json.load(open(sys.argv[1]))[sys.argv[2]])" "$1" "$2"; }
control_backup_completed_at="$(manifest_field "${control_dump}.manifest.json" backup_completed_at)" || fail "missing control backup manifest timestamp"
asu_backup_completed_at="$(manifest_field "${asu_dump}.manifest.json" backup_completed_at)" || fail "missing tenant-asu backup manifest timestamp"
azurite_backup_completed_at="$(manifest_field "${azurite_tarball}.manifest.json" backup_completed_at)" || fail "missing azurite backup manifest timestamp"

echo "=== Phase 4: simulate ongoing writes AFTER the backup (the honest RPO loss window) ==="
post_backup_write_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
mysql_exec mysql-control biolog_control \
  "INSERT INTO rehearsal_tenants (id, name) VALUES (99, 'post-backup-tenant-LOST');" || fail "could not simulate post-backup write (control)"
mysql_exec mysql-tenant-asu biolog_asu \
  "INSERT INTO rehearsal_persons (id, label) VALUES (99, 'post-backup-person-LOST');" || fail "could not simulate post-backup write (tenant-asu)"
printf 'synthetic photo evidence fixture #3 (post-backup, should be LOST) %s\n' "$RUN_ID" > "$WORK_DIR/fixtures/evidence-3.bin"
python3 scripts/azurite-rest.py --port "$AZURITE_BLOB_PORT" put-blob --container ops009-evidence --blob evidence-3.bin --file "$WORK_DIR/fixtures/evidence-3.bin" || fail "could not simulate post-backup blob write"
echo "disaster-recovery-rehearsal: wrote 1 more row to each MySQL db and 1 more blob, all AFTER the backup completed"

echo "=== Phase 5: declare disaster - destroy every original container and volume ==="
disaster_declared_at_epoch="$(date -u +%s)"
disaster_declared_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
compose down -v --remove-orphans || fail "could not tear down the original stack to simulate disaster"
echo "disaster-recovery-rehearsal: original stack and its volumes are gone"

echo "=== Phase 6: restore into brand-new containers/volumes ==="
restore_control_port=48161
restore_asu_port=48162
restore_azurite_port=48100

bash scripts/restore-mysql.sh --dump "$control_dump" --container "$RESTORE_CONTROL_CONTAINER" \
  --volume "$RESTORE_CONTROL_VOLUME" --port "$restore_control_port" || fail "mysql-control restore failed"
bash scripts/restore-mysql.sh --dump "$asu_dump" --container "$RESTORE_ASU_CONTAINER" \
  --volume "$RESTORE_ASU_VOLUME" --port "$restore_asu_port" || fail "mysql-tenant-asu restore failed"
bash scripts/restore-azurite.sh --tarball "$azurite_tarball" --container "$RESTORE_AZURITE_CONTAINER" \
  --volume "$RESTORE_AZURITE_VOLUME" --port "$restore_azurite_port" || fail "azurite restore failed"

# Test-only hook (SY-OPS009-02 regression coverage): lets
# tests/foundation/BIO-OPS-009.test.sh deterministically exercise the
# cleanup-on-failure path right after every standalone restore resource
# exists, without needing to corrupt a real backup to force a failure.
# Never set in a real rehearsal run.
if [ "${BIOLOG_OPS009_INJECT_FAILURE_AFTER_PHASE6:-0}" = "1" ]; then
  fail "test-injected failure after Phase 6 (BIOLOG_OPS009_INJECT_FAILURE_AFTER_PHASE6=1)"
fi

echo "=== Phase 7: verify restored data (full row content, and that post-backup writes are correctly absent) ==="
verify_failures=0

control_rows_restored="$(docker exec "$RESTORE_CONTROL_CONTAINER" mysql -u root -p"$MYSQL_ROOT_PASSWORD" -N -e \
  "SELECT id, name FROM biolog_control.rehearsal_tenants ORDER BY id;" 2>/dev/null)"
if [ "$control_rows_restored" = "$control_rows_at_backup" ]; then
  echo "VERIFY PASS  mysql-control restored rows match the pre-backup snapshot exactly (full row content, not just ids; post-backup row 99 correctly absent)"
else
  echo "VERIFY FAIL  mysql-control restored rows differ from the pre-backup snapshot"
  echo "             expected: $(printf '%s' "$control_rows_at_backup" | tr '\n' '|')"
  echo "             actual:   $(printf '%s' "$control_rows_restored" | tr '\n' '|')"
  verify_failures=$((verify_failures + 1))
fi

asu_rows_restored="$(docker exec "$RESTORE_ASU_CONTAINER" mysql -u root -p"$MYSQL_ROOT_PASSWORD" -N -e \
  "SELECT id, label FROM biolog_asu.rehearsal_persons ORDER BY id;" 2>/dev/null)"
if [ "$asu_rows_restored" = "$asu_rows_at_backup" ]; then
  echo "VERIFY PASS  mysql-tenant-asu restored rows match the pre-backup snapshot exactly (full row content, not just ids; post-backup row 99 correctly absent)"
else
  echo "VERIFY FAIL  mysql-tenant-asu restored rows differ from the pre-backup snapshot"
  echo "             expected: $(printf '%s' "$asu_rows_at_backup" | tr '\n' '|')"
  echo "             actual:   $(printf '%s' "$asu_rows_restored" | tr '\n' '|')"
  verify_failures=$((verify_failures + 1))
fi

restored_blob_1_sha="$(python3 scripts/azurite-rest.py --port "$restore_azurite_port" get-blob --container ops009-evidence --blob evidence-1.bin --out "$WORK_DIR/fixtures/evidence-1-restored.bin" 2>&1 | grep -o 'sha256=[0-9a-f]*' | cut -d= -f2)"
restored_blob_2_sha="$(python3 scripts/azurite-rest.py --port "$restore_azurite_port" get-blob --container ops009-evidence --blob evidence-2.bin --out "$WORK_DIR/fixtures/evidence-2-restored.bin" 2>&1 | grep -o 'sha256=[0-9a-f]*' | cut -d= -f2)"
if [ "$restored_blob_1_sha" = "$evidence_1_sha" ] && [ "$restored_blob_2_sha" = "$evidence_2_sha" ]; then
  echo "VERIFY PASS  azurite restored blobs 1 and 2 match their pre-backup sha256 exactly"
else
  echo "VERIFY FAIL  azurite restored blob hashes did not match (1: $restored_blob_1_sha vs $evidence_1_sha, 2: $restored_blob_2_sha vs $evidence_2_sha)"
  verify_failures=$((verify_failures + 1))
fi

blob_3_status="present"
python3 scripts/azurite-rest.py --port "$restore_azurite_port" get-blob --container ops009-evidence --blob evidence-3.bin --out /dev/null >/dev/null 2>&1 || blob_3_status="absent"
if [ "$blob_3_status" = "absent" ]; then
  echo "VERIFY PASS  azurite post-backup blob evidence-3.bin is correctly absent from the restore"
else
  echo "VERIFY FAIL  azurite post-backup blob evidence-3.bin should be absent from the restore but was found"
  verify_failures=$((verify_failures + 1))
fi

restore_verified_at_epoch="$(date -u +%s)"
restore_verified_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
rto_seconds=$((restore_verified_at_epoch - disaster_declared_at_epoch))

# FEMI-OPS009-02: observed RPO per store, computed from that store's own
# backup-completion timestamp through the moment the disaster was
# declared - not a hard-coded count of the rehearsal's own fixture rows.
epoch_of() { python3 -c "
import datetime, sys
ts = datetime.datetime.strptime(sys.argv[1], '%Y-%m-%dT%H:%M:%SZ').replace(tzinfo=datetime.timezone.utc)
print(int(ts.timestamp()))
" "$1"; }
control_rpo_seconds=$(( disaster_declared_at_epoch - $(epoch_of "$control_backup_completed_at") ))
asu_rpo_seconds=$(( disaster_declared_at_epoch - $(epoch_of "$asu_backup_completed_at") ))
azurite_rpo_seconds=$(( disaster_declared_at_epoch - $(epoch_of "$azurite_backup_completed_at") ))

for pair in "mysql-control:$control_rpo_seconds" "mysql-tenant-asu:$asu_rpo_seconds" "azurite:$azurite_rpo_seconds"; do
  name="${pair%%:*}" seconds="${pair##*:}"
  if [ "$seconds" -ge 0 ]; then
    echo "VERIFY PASS  $name observed RPO is a sane non-negative duration (${seconds}s between its backup completing and the disaster)"
  else
    echo "VERIFY FAIL  $name observed RPO came out negative (${seconds}s) - backup-completion timestamp is after the disaster, which cannot be real"
    verify_failures=$((verify_failures + 1))
  fi
done

echo "=== Phase 8: record observed RPO/RTO (this single local run only) ==="
cat > "$RESULT_JSON" <<EOF
{
  "story": "BIO-OPS-009",
  "run_id": "$RUN_ID",
  "disclaimer": "Observed on a single local laptop run. NOT a production SLO or capacity claim - see BIO-OPS-007 for the same disclosure pattern applied to its own local performance numbers.",
  "post_backup_write_at": "$post_backup_write_at",
  "disaster_declared_at": "$disaster_declared_at",
  "restore_verified_at": "$restore_verified_at",
  "observed_rto_seconds": $rto_seconds,
  "observed_rpo": {
    "mysql_control": {"backup_completed_at": "$control_backup_completed_at", "rpo_seconds": $control_rpo_seconds, "lost_rows": 1, "lost_row_ids": [99]},
    "mysql_tenant_asu": {"backup_completed_at": "$asu_backup_completed_at", "rpo_seconds": $asu_rpo_seconds, "lost_rows": 1, "lost_row_ids": [99]},
    "azurite": {"backup_completed_at": "$azurite_backup_completed_at", "rpo_seconds": $azurite_rpo_seconds, "lost_blobs": 1, "lost_blob_names": ["evidence-3.bin"]}
  },
  "verification_failures": $verify_failures,
  "verification_result": "$([ "$verify_failures" -eq 0 ] && echo PASS || echo FAIL)"
}
EOF
echo "disaster-recovery-rehearsal: result written to $RESULT_JSON"
cat "$RESULT_JSON"

if [ "$keep_artifacts" -eq 0 ]; then
  cp "$RESULT_JSON" "/tmp/biolog-ops009-last-result.json" 2>/dev/null || true
  rm -rf "$WORK_DIR"
  echo "disaster-recovery-rehearsal: scratch artifacts removed ($WORK_DIR); result also copied to /tmp/biolog-ops009-last-result.json"
fi

echo "=== Phase 9: tear down (via EXIT trap) ==="
[ "$verify_failures" -eq 0 ] || exit 1
echo "disaster-recovery-rehearsal: PASS (observed RTO ${rto_seconds}s - see result JSON for full detail)"
