#!/usr/bin/env bash
# Offline-safe structural checks for BIO-MOB-003 (central/offline
# synchronization and conflict queue). Behavioural assertions (idempotent-ack
# handling, conflict routing, reconnect-storm dedup) live in the executable
# Flutter suite under tests/flutter/ (see docs/TESTING.md,
# scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

# --- AC01: delta sync supports additions/updates/revocations ---
check "AC01 OfflineMutationOperation models addition/update/revocation" \
  bash -c 'grep -q "enum OfflineMutationOperation { addition, update, revocation }" mobile/lib/core/offline/offline_mutation.dart'
check "AC01 the sync delta payload carries operation and payload without touching the frozen envelope" \
  bash -c 'grep -q "toSyncDeltaJson" mobile/lib/core/offline/offline_mutation.dart \
    && awk "/Map<String, Object\\?> toEnvelopeJson/,/^      };/" mobile/lib/core/offline/offline_mutation.dart | grep -qv "operation\|payload"'
check "AC01 the offline_mutations schema migration adds the operation column without dropping prior data" \
  bash -c 'grep -q "ALTER TABLE offline_mutations ADD COLUMN operation" mobile/lib/core/db/sqlcipher_app_database.dart'
check "AC01 SyncEngine gives revocation deltas priority over other pending mutations (ADR-0001 §3.5)" \
  bash -c 'grep -q "_prioritized" mobile/lib/core/sync/sync_engine.dart \
    && grep -q "OfflineMutationOperation.revocation" mobile/lib/core/sync/sync_engine.dart'
check "AC01 HttpSyncTransport is a real implementation, not a permanent throw (BIO-MOB-003-FEMI-01)" \
  bash -c '! (awk "/class HttpSyncTransport/,/^}/" mobile/lib/core/sync/sync_transport.dart | grep -q "^\s*throw UnimplementedError")'
check "AC01/AC05 SyncEngine is wired into the live mobile app, not only sync-package tests (BIO-MOB-003-FEMI-01)" \
  bash -c 'grep -l "SyncEngine\|HttpSyncTransport\|attachTo(" mobile/lib/core/session/session_controller.dart >/dev/null 2>&1'

# --- AC02: server acknowledgements are idempotent ---
check "AC02 SyncAck models a replayed acknowledgement distinctly from a fresh one" \
  bash -c 'grep -q "replayed" mobile/lib/core/sync/sync_ack.dart'
check "AC02 SyncEngine handles a replayed acknowledgement identically to a fresh one (no branch on replayed)" \
  bash -c '! grep -q "\.replayed" mobile/lib/core/sync/sync_engine.dart'
check "AC02 markAcknowledged is guarded by current state, mirroring every other queue transition" \
  bash -c 'awk "/Future<void> markAcknowledged/,/^  }/" mobile/lib/core/db/sqlcipher_app_database.dart | grep -q "sync_state = ?"'

# --- AC03: critical conflicts enter Sync Resolution Queue ---
check "AC03 SyncEngine routes a conflict acknowledgement to recordConflict, not a merge path" \
  bash -c 'grep -q "SyncAckOutcome.conflict" mobile/lib/core/sync/sync_engine.dart \
    && grep -q "recordConflict" mobile/lib/core/sync/sync_engine.dart'
check "AC03 conflicted mutations remain queryable for the human Sync Resolution Queue" \
  bash -c 'grep -q "conflictedMutations" mobile/lib/core/db/app_database.dart'

# --- AC04: last-write-wins is not used for security-sensitive data ---
check "AC04 no automatic merge/resolution path exists anywhere in the sync engine" \
  bash -c '! grep -rIn "resolveConflict\|autoMerge\|mergeConflict\|lastWriteWins" mobile/lib/core/sync 2>/dev/null | grep -q .'

# --- AC05: reconnect storms do not create duplicates ---
check "AC05 SyncEngine has an in-process single-flight guard against overlapping passes" \
  bash -c 'grep -q "_inFlight" mobile/lib/core/sync/sync_engine.dart'
check "AC05 markUploading reports whether it actually claimed the mutation, so a pass never uploads one it does not own" \
  bash -c 'grep -q "Future<bool> markUploading" mobile/lib/core/db/app_database.dart \
    && grep -q "Future<bool> markUploading" mobile/lib/core/db/sqlcipher_app_database.dart \
    && grep -q "Future<bool> markUploading" tests/flutter/test/support/fake_app_database.dart'
check "AC05 SyncEngine can attach to the live connectivity signal to react to a reconnect" \
  bash -c 'grep -q "attachTo" mobile/lib/core/sync/sync_engine.dart \
    && grep -q "ConnectivityState.online" mobile/lib/core/sync/sync_engine.dart'
check "AC05 SessionController attaches SyncEngine before starting connectivity, so the first reading is never missed (BIO-MOB-003-FEMI-02)" \
  bash -c 'attach_line=$(grep -n "_syncEngine.attachTo(_connectivity)" mobile/lib/core/session/session_controller.dart | head -1 | cut -d: -f1); \
    start_line=$(grep -n "await _connectivity.start()" mobile/lib/core/session/session_controller.dart | head -1 | cut -d: -f1); \
    [ -n "$attach_line" ] && [ -n "$start_line" ] && [ "$attach_line" -lt "$start_line" ]'
check "AC05 HttpSyncTransport enforces a request deadline, not an unbounded wait (BIO-MOB-003-FEMI-03)" \
  bash -c 'grep -q "\.timeout(_requestTimeout)" mobile/lib/core/sync/sync_transport.dart'

# --- Developer test wiring ---
check "unit tests mapped to BIO-MOB-003 ACs exist" \
  bash -c 'test -f tests/flutter/test/bio_mob_003_ac01_delta_operations_test.dart \
    && test -f tests/flutter/test/bio_mob_003_ac02_idempotent_ack_test.dart \
    && test -f tests/flutter/test/bio_mob_003_ac03_ac04_conflict_test.dart \
    && test -f tests/flutter/test/bio_mob_003_ac05_reconnect_storm_test.dart \
    && test -f tests/flutter/test/bio_mob_003_femi01_http_sync_transport_test.dart \
    && test -f tests/flutter/test/bio_mob_003_femi01_session_wiring_test.dart \
    && test -f tests/flutter/test/bio_mob_003_femi02_femi03_test.dart'
check "builder evidence recorded" test -d evidence/BIO-MOB-003/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
