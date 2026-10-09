#!/usr/bin/env bash
# Developer tests for BIO-OPS-009, mapped to acceptance criteria.
# Run from anywhere: tests/foundation/BIO-OPS-009.test.sh
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

total=0
failed=0
skipped=0

check() {
  local name="$1"; shift
  total=$((total + 1))
  if "$@"; then
    echo "PASS  $name"
  else
    echo "FAIL  $name"
    failed=$((failed + 1))
  fi
}

skip() {
  local name="$1" reason="$2"
  skipped=$((skipped + 1))
  echo "SKIP  $name ($reason)"
}

# --- Portability regression: every new script here must parse (and,
# cheaply, actually run with -h-equivalent no-op where possible) under
# macOS's bash 3.2, same constraint every sibling infra script documents
# (no bash 4+ associative arrays, no unguarded empty-array expansion under
# `set -u`). ------------------------------------------------------------
scripts_are_present_executable_and_bash32_compatible() {
  command -v /bin/bash >/dev/null 2>&1 || return 0
  for f in scripts/backup-mysql.sh scripts/restore-mysql.sh \
           scripts/backup-azurite.sh scripts/restore-azurite.sh \
           scripts/disaster-recovery-rehearsal.sh; do
    [ -x "$f" ] || return 1
    /bin/bash -n "$f" || return 1
  done
  return 0
}
check "AC01-AC04: all BIO-OPS-009 scripts exist, are executable and parse under bash 3.2" \
  scripts_are_present_executable_and_bash32_compatible

azurite_rest_script_is_present_and_parses() {
  [ -x "scripts/azurite-rest.py" ] || return 1
  python3 -m py_compile scripts/azurite-rest.py
}
check "AC03: scripts/azurite-rest.py exists, is executable and compiles" azurite_rest_script_is_present_and_parses

# --- Documentation coverage: AC02 explicitly requires the restore
# procedure be documented; this guards against the doc silently drifting
# out of sync with what the scripts actually do. -------------------------
doc_covers_every_ac() {
  local doc="docs/BACKUP-RESTORE-REHEARSAL.md"
  [ -f "$doc" ] || return 1
  for ac in AC01 AC02 AC03 AC04; do
    grep -q "$ac" "$doc" || return 1
  done
  grep -q "disaster-recovery-rehearsal.sh" "$doc" || return 1
  return 0
}
check "AC02: docs/BACKUP-RESTORE-REHEARSAL.md documents all four ACs and the rehearsal command" doc_covers_every_ac

# --- AC01: backup-mysql.sh never produces a single combined dump - one
# dump file per service/database is a structural, not just behavioural,
# property worth protecting directly in the script text. -----------------
backup_mysql_dumps_one_database_at_a_time() {
  grep -q -- '--databases "\$database"' scripts/backup-mysql.sh
}
check "AC01: backup-mysql.sh dumps exactly one database per invocation (independent backups)" \
  backup_mysql_dumps_one_database_at_a_time

# --- AC02: restore scripts must always create a NEW volume, never reuse
# an existing one - this is the one property that makes "restore to new
# local containers" true rather than just restarting the original. ------
restore_scripts_always_create_a_new_volume() {
  grep -q 'docker volume create "\$volume"' scripts/restore-mysql.sh || return 1
  grep -q 'docker volume create "\$volume"' scripts/restore-azurite.sh || return 1
  return 0
}
check "AC02: restore-mysql.sh and restore-azurite.sh always create a fresh named volume" \
  restore_scripts_always_create_a_new_volume

restore_azurite_sets_explicit_location() {
  # Regression: Azurite defaults --location to its own CWD, not the
  # mounted volume, if omitted - confirmed empirically (see
  # docs/BACKUP-RESTORE-REHEARSAL.md) to silently open a brand-new empty
  # metadata database instead of the one just restored.
  grep -q -- '--location /data' scripts/restore-azurite.sh
}
check "AC02/AC03: restore-azurite.sh passes --location /data explicitly to the fresh container" \
  restore_azurite_sets_explicit_location

backup_azurite_stops_container_before_snapshot() {
  # Regression: Azurite's LokiJS metadata store only flushes to disk on a
  # graceful stop, not on every write - a tar taken from a live volume can
  # silently capture a stale/empty metadata snapshot (confirmed empirically
  # against a real instance; see docs/BACKUP-RESTORE-REHEARSAL.md).
  grep -q 'docker stop' scripts/backup-azurite.sh
}
check "AC03: backup-azurite.sh stops the Azurite container before snapshotting its volume" \
  backup_azurite_stops_container_before_snapshot

# --- AC04: the rehearsal's own result must disclose it is not a
# production SLO, the same disclosure pattern BIO-OPS-007 uses for its own
# local performance numbers. ---------------------------------------------
rehearsal_result_discloses_non_slo() {
  grep -q 'NOT a production SLO' scripts/disaster-recovery-rehearsal.sh
}
check "AC04: the rehearsal result JSON explicitly disclaims production SLO status" \
  rehearsal_result_discloses_non_slo

rehearsal_is_isolated_from_the_default_stack() {
  # Regression: this must never run under the compose file's own pinned
  # `name: biolog`, or it would adopt/recreate a developer's already
  # running default stack instead of an isolated throwaway one.
  grep -q -- '-p "\$PROJECT"' scripts/disaster-recovery-rehearsal.sh || return 1
  grep -q 'PROJECT="biolog-ops009-rehearsal"' scripts/disaster-recovery-rehearsal.sh || return 1
  return 0
}
check "AC04: the rehearsal runs under its own isolated compose project name" \
  rehearsal_is_isolated_from_the_default_stack

# --- Fixed-input regression test for scripts/azurite-rest.py's Shared Key
# signing, independent of Docker: protects the two non-obvious fixes
# (doubled account name in CanonicalizedResource; explicit Content-Type
# matching what urllib actually sends) against being silently reverted.
# The expected signature was captured from a real Azurite instance that
# accepted it (its own --debug log reported "Signature 1 matched" for this
# exact request shape). -----------------------------------------------
azurite_rest_signing_matches_known_good_vector() {
  python3 - <<'PY'
import importlib.util
import sys

spec = importlib.util.spec_from_file_location("azurite_rest", "scripts/azurite-rest.py")
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

headers = {"x-ms-date": "Thu, 08 Oct 2026 05:21:55 GMT", "x-ms-version": "2021-08-06"}
sig = mod._sign("GET", "/ops009-test/test-blob.txt", {}, headers, 0, "")
expected = "VzPJRxbJ+aXkljPTE4EK0byxqw87ekaF5nee/HQRXbY="
sys.exit(0 if sig == expected else 1)
PY
}
check "AC03: azurite-rest.py Shared Key signing matches a known-good vector from a real Azurite accept" \
  azurite_rest_signing_matches_known_good_vector

# --- Round-2 review findings (SY-OPS009-01/02, FEMI-OPS009-01/02) -------

verification_checks_full_row_content_not_just_ids() {
  # Regression for SY-OPS009-01: a restore that preserves primary keys but
  # corrupts/truncates every other column must FAIL, not silently pass an
  # ids-only check. Protects both that the SELECTs include non-key columns
  # AND that the comparison is against a captured pre-backup snapshot, not
  # a hard-coded literal that could drift from the actual seed data.
  grep -q "SELECT id, name FROM biolog_control.rehearsal_tenants" scripts/disaster-recovery-rehearsal.sh || return 1
  grep -q "SELECT id, label FROM biolog_asu.rehearsal_persons" scripts/disaster-recovery-rehearsal.sh || return 1
  grep -q "control_rows_at_backup" scripts/disaster-recovery-rehearsal.sh || return 1
  grep -q "asu_rows_at_backup" scripts/disaster-recovery-rehearsal.sh || return 1
  grep -q '"\$control_rows_restored" = "\$control_rows_at_backup"' scripts/disaster-recovery-rehearsal.sh || return 1
  return 0
}
check "AC01/SY-OPS009-01: restore verification compares full row content against a pre-backup snapshot" \
  verification_checks_full_row_content_not_just_ids

mutation_in_restored_data_is_caught_by_the_comparison_method() {
  # Direct demonstration of the SY-OPS009-01 mutation scenario, without
  # needing Docker: the exact string-equality check Phase 7 uses correctly
  # distinguishes a row set with a corrupted non-key column from the
  # pre-backup snapshot, even though both have identical ids.
  local snapshot restored
  snapshot="$(printf '1\tsynthetic-tenant-asu\n2\tsynthetic-tenant-grace')"
  restored="$(printf '1\tsynthetic-tenant-asu\n2\tCORRUPTED-VALUE')"
  [ "$snapshot" != "$restored" ]
}
check "SY-OPS009-01: a corrupted non-key column is caught even though ids match" \
  mutation_in_restored_data_is_caught_by_the_comparison_method

disaster_recovery_rehearsal_traps_exit_int_term() {
  # Regression for SY-OPS009-02: a bare `trap cleanup_rehearsal_stack INT
  # TERM` does NOT terminate the script on its own (a custom trap replaces
  # the signal's default fatal action; the script keeps running after the
  # handler returns unless the handler itself calls exit) - INT/TERM must
  # each explicitly exit, and cleanup must be bound to EXIT so it still
  # runs exactly once either way.
  grep -q "^trap cleanup_rehearsal_stack EXIT" scripts/disaster-recovery-rehearsal.sh || return 1
  grep -qE "trap '.*exit [0-9]+.*' INT" scripts/disaster-recovery-rehearsal.sh || return 1
  grep -qE "trap '.*exit [0-9]+.*' TERM" scripts/disaster-recovery-rehearsal.sh || return 1
  return 0
}
check "SY-OPS009-02: INT/TERM traps explicitly exit (not just a bare handler that lets the script continue)" \
  disaster_recovery_rehearsal_traps_exit_int_term

restore_scripts_refuse_a_pre_existing_volume() {
  # Regression for SY-OPS009-02: `docker volume create` silently succeeds
  # (reuses) for an existing name - both restore scripts must check first
  # and refuse, not just rely on the orchestrator's own cleanup trap
  # (defense in depth for a direct/standalone invocation).
  grep -q 'docker volume inspect "\$volume"' scripts/restore-mysql.sh || return 1
  grep -q "already exists" scripts/restore-mysql.sh || return 1
  grep -q 'docker volume inspect "\$volume"' scripts/restore-azurite.sh || return 1
  grep -q "already exists" scripts/restore-azurite.sh || return 1
  return 0
}
check "SY-OPS009-02: restore-mysql.sh and restore-azurite.sh refuse a pre-existing volume name" \
  restore_scripts_refuse_a_pre_existing_volume

backup_azurite_restarts_even_if_archive_fails() {
  # Regression for FEMI-OPS009-01: the archive step's own exit status must
  # be captured (not let `set -e` abort immediately) so the restart is
  # always attempted, and a failed restart must itself be fatal, not just
  # a warning.
  grep -q "archive_rc=" scripts/backup-azurite.sh || return 1
  grep -q 'docker start "\$c"' scripts/backup-azurite.sh || return 1
  grep -q "treating as failure, not a warning" scripts/backup-azurite.sh || return 1
  return 0
}
check "FEMI-OPS009-01: backup-azurite.sh always attempts a restart even if the archive step fails" \
  backup_azurite_restarts_even_if_archive_fails

backup_azurite_traps_exit_int_term_for_interrupted_backups() {
  # Regression for FEMI-OPS009-01, round 3: the round-2 fix only covered
  # the archive command returning a nonzero exit code - it did not cover
  # the shell process itself being killed (SIGINT/SIGTERM) between
  # `docker stop` and the restart, which left Azurite stopped with no
  # restart ever attempted (confirmed live: the container stays stopped
  # without this trap). A trap bound directly to INT/TERM does not
  # terminate the script on its own, so INT/TERM must each explicitly
  # exit (cascading into the EXIT trap, which does the actual restart).
  grep -q "^trap restart_on_exit_if_not_already_confirmed EXIT" scripts/backup-azurite.sh || return 1
  grep -qE "trap '.*exit [0-9]+.*' INT" scripts/backup-azurite.sh || return 1
  grep -qE "trap '.*exit [0-9]+.*' TERM" scripts/backup-azurite.sh || return 1
  grep -q "container_restart_confirmed" scripts/backup-azurite.sh || return 1
  return 0
}
check "FEMI-OPS009-01 round 3: backup-azurite.sh traps EXIT/INT/TERM to restart an interrupted backup's container" \
  backup_azurite_traps_exit_int_term_for_interrupted_backups

rpo_is_computed_from_manifest_timestamps_not_hardcoded() {
  # Regression for FEMI-OPS009-02: RPO must be derived from each store's
  # own backup-completion timestamp (already written to its manifest by
  # backup-mysql.sh/backup-azurite.sh) through the disaster, not a
  # hard-coded count of the rehearsal's own fixture rows.
  grep -q "backup_completed_at" scripts/disaster-recovery-rehearsal.sh || return 1
  grep -q "manifest_field()" scripts/disaster-recovery-rehearsal.sh || return 1
  grep -q "control_rpo_seconds=" scripts/disaster-recovery-rehearsal.sh || return 1
  grep -q "disaster_declared_at_epoch - \$(epoch_of" scripts/disaster-recovery-rehearsal.sh || return 1
  return 0
}
check "FEMI-OPS009-02: observed RPO is computed from real per-store backup-completion timestamps" \
  rpo_is_computed_from_manifest_timestamps_not_hardcoded

# --- Live end-to-end rehearsal: only runs if Docker's daemon is reachable
# (same gate tests/foundation/BIO-FND-003.test.sh's own live smoke test
# uses). This is the real proof the story asks for - not a structural
# stand-in - so it is not skipped lightly; it fully tears down everything
# it creates regardless of outcome. --------------------------------------
if docker info >/dev/null 2>&1; then
  check "AC01-AC04 (live): full disaster-recovery rehearsal passes end to end" bash -c '
    rm -rf backups/ops009-rehearsal
    scripts/disaster-recovery-rehearsal.sh --keep-artifacts >/tmp/biolog-ops009-test-output.log 2>&1
    rc=$?
    result_file="$(ls backups/ops009-rehearsal/*/result.json 2>/dev/null | head -1)"
    ok=0
    if [ "$rc" -eq 0 ] && [ -n "$result_file" ] && grep -q "\"verification_result\": \"PASS\"" "$result_file"; then
      ok=1
    fi
    rm -rf backups/ops009-rehearsal
    [ "$ok" -eq 1 ]
  '
  check "SY-OPS009-02 (live): a failure after Phase 6 still cleans up every standalone restore resource" bash -c '
    rm -rf backups/ops009-rehearsal
    BIOLOG_OPS009_INJECT_FAILURE_AFTER_PHASE6=1 scripts/disaster-recovery-rehearsal.sh --keep-artifacts >/tmp/biolog-ops009-failinject-output.log 2>&1
    rc=$?
    stray=0
    docker ps -a --format "{{.Names}}" | grep -q "^ops009-rehearsal-control-restored$" && stray=1
    docker volume ls --format "{{.Name}}" | grep -q "ops009_rehearsal_control_restored_" && stray=1
    rm -rf backups/ops009-rehearsal
    [ "$rc" -ne 0 ] && [ "$stray" -eq 0 ]
  '

  check "FEMI-OPS009-01 (live): a failed Azurite archive step still restarts the container" bash -c '
    docker rm -f femi-ops009-01-test >/dev/null 2>&1
    docker volume rm -f femi-ops009-01-test-vol >/dev/null 2>&1
    docker volume create femi-ops009-01-test-vol >/dev/null
    docker run -d --name femi-ops009-01-test -p 48998:10000 -v femi-ops009-01-test-vol:/data \
      mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0 --location /data >/dev/null
    sleep 2
    mkdir -p "${TMPDIR:-/tmp}/femi-ops009-01-readonly"
    chmod 500 "${TMPDIR:-/tmp}/femi-ops009-01-readonly"
    scripts/backup-azurite.sh --out "${TMPDIR:-/tmp}/femi-ops009-01-readonly/nested" \
      --volume femi-ops009-01-test-vol --container femi-ops009-01-test >/dev/null 2>&1
    archive_rc=$?
    running="$(docker inspect -f "{{.State.Running}}" femi-ops009-01-test 2>/dev/null)"
    chmod 700 "${TMPDIR:-/tmp}/femi-ops009-01-readonly" 2>/dev/null
    rm -rf "${TMPDIR:-/tmp}/femi-ops009-01-readonly"
    docker rm -f femi-ops009-01-test >/dev/null 2>&1
    docker volume rm -f femi-ops009-01-test-vol >/dev/null 2>&1
    [ "$archive_rc" -ne 0 ] && [ "$running" = "true" ]
  '

  check "FEMI-OPS009-01 round 3 (live): SIGTERM between stop and restart still restarts the container" bash -c '
    docker rm -f femi-ops009-01-sig-test >/dev/null 2>&1
    docker volume rm -f femi-ops009-01-sig-test-vol >/dev/null 2>&1
    docker volume create femi-ops009-01-sig-test-vol >/dev/null
    docker run -d --name femi-ops009-01-sig-test -p 48995:10000 -v femi-ops009-01-sig-test-vol:/data \
      mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0 --location /data >/dev/null
    sleep 2
    rm -rf backups/femi-ops009-01-sig-out
    scripts/backup-azurite.sh --out backups/femi-ops009-01-sig-out \
      --volume femi-ops009-01-sig-test-vol --container femi-ops009-01-sig-test \
      >"${TMPDIR:-/tmp}/biolog-ops009-sigterm-test.log" 2>&1 &
    bpid=$!
    caught=0
    for i in $(seq 1 50); do
      grep -q "stopping femi-ops009-01-sig-test" "${TMPDIR:-/tmp}/biolog-ops009-sigterm-test.log" 2>/dev/null && { caught=1; break; }
      sleep 0.1
    done
    kill -TERM "$bpid" 2>/dev/null
    wait "$bpid" 2>/dev/null
    sleep 1
    running="$(docker inspect -f "{{.State.Running}}" femi-ops009-01-sig-test 2>/dev/null)"
    rm -rf backups/femi-ops009-01-sig-out
    docker rm -f femi-ops009-01-sig-test >/dev/null 2>&1
    docker volume rm -f femi-ops009-01-sig-test-vol >/dev/null 2>&1
    [ "$caught" -eq 1 ] && [ "$running" = "true" ]
  '
else
  skip "AC01-AC04 (live): full disaster-recovery rehearsal passes end to end" \
    "Docker daemon not reachable on this machine - see docs/BACKUP-RESTORE-REHEARSAL.md"
  skip "SY-OPS009-02 (live): a failure after Phase 6 still cleans up every standalone restore resource" \
    "Docker daemon not reachable on this machine"
  skip "FEMI-OPS009-01 (live): a failed Azurite archive step still restarts the container" \
    "Docker daemon not reachable on this machine"
  skip "FEMI-OPS009-01 round 3 (live): SIGTERM between stop and restart still restarts the container" \
    "Docker daemon not reachable on this machine"
fi

echo "=============================="
echo "$((total - failed))/$total checks passed, $skipped skipped"
[ "$failed" -eq 0 ]
