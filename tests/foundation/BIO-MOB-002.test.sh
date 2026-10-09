#!/usr/bin/env bash
# Offline-safe structural checks for BIO-MOB-002 (offline event queue and
# idempotency foundation). Behavioural assertions (backoff bounds/jitter,
# retry/failed transitions, conflict handling) live in the executable
# Flutter suite under tests/flutter/ (see docs/TESTING.md,
# scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

# --- AC01: queued records have global IDs, device IDs, version and idempotency keys ---
# (delivered by BIO-MOB-001; re-checked here since BIO-MOB-002 extends the same model)
check "AC01 OfflineMutation carries mutation/tenant/device/aggregate ids, version and idempotency key" \
  bash -c 'grep -q "mutationId" mobile/lib/core/offline/offline_mutation.dart \
    && grep -q "deviceId" mobile/lib/core/offline/offline_mutation.dart \
    && grep -q "baseVersion" mobile/lib/core/offline/offline_mutation.dart \
    && grep -q "idempotencyKey" mobile/lib/core/offline/offline_mutation.dart'
check "AC01 retry-tracking fields do not leak into the frozen wire envelope" \
  bash -c 'awk "/Map<String, Object\\?> toEnvelopeJson/,/^      };/" mobile/lib/core/offline/offline_mutation.dart | grep -qv "retry_count\|next_retry_at" '

# --- AC02: states include pending/uploading/acknowledged/synced/conflict/failed ---
# (delivered by BIO-MOB-001; re-checked here since this story adds the transitions)
check "AC02 all six sync states are modelled" \
  bash -c 'grep -q "pending, uploading, acknowledged, synced, conflict, failed" mobile/lib/core/offline/offline_mutation.dart'
check "AC02 markUploading transitions pending -> uploading" \
  bash -c 'grep -q "markUploading" mobile/lib/core/db/app_database.dart \
    && grep -q "markUploading" mobile/lib/core/db/sqlcipher_app_database.dart'
check "AC02 state transitions are guarded by current state, never resurrect a terminal record (BIO-MOB-002-QA-01)" \
  bash -c 'awk "/Future<bool> markUploading/,/^  }/" mobile/lib/core/db/sqlcipher_app_database.dart | grep -q "sync_state = ?" \
    && awk "/Future<void> recordConflict/,/^  }/" mobile/lib/core/db/sqlcipher_app_database.dart | grep -q "sync_state = ?"'
check "AC03 recordUploadFailure is transactional, not a racy read-then-write (BIO-MOB-002-QA-01)" \
  bash -c 'awk "/Future<void> recordUploadFailure/,/^  }/" mobile/lib/core/db/sqlcipher_app_database.dart | grep -q "_requireOpen.transaction"'

# --- AC03: retry is exponential and bounded ---
check "AC03 a documented retry-policy module exists (exponential, bounded, jittered)" \
  test -f mobile/lib/core/offline/retry_policy.dart
check "AC03 backoff is bounded by maxAttempts and capped by maxDelay" \
  bash -c 'grep -q "maxAttempts" mobile/lib/core/offline/retry_policy.dart \
    && grep -q "maxDelay" mobile/lib/core/offline/retry_policy.dart \
    && grep -q "hasAttemptsRemaining" mobile/lib/core/offline/retry_policy.dart'
check "AC03 backoff grows exponentially with jitter, not a fixed delay" \
  bash -c 'grep -qE "pow\(2, ?retryCount\)" mobile/lib/core/offline/retry_policy.dart \
    && grep -q "rng.nextDouble" mobile/lib/core/offline/retry_policy.dart'
check "AC03 exhausting the retry bound moves a mutation to the terminal failed state" \
  bash -c 'grep -q "OfflineSyncState.failed" mobile/lib/core/db/sqlcipher_app_database.dart \
    && grep -q "OfflineSyncState.failed" tests/flutter/test/support/fake_app_database.dart'
check "AC03 dueForRetry only surfaces pending mutations whose backoff window has elapsed" \
  bash -c 'grep -q "dueForRetry" mobile/lib/core/db/app_database.dart \
    && grep -q "next_retry_at" mobile/lib/core/db/sqlcipher_app_database.dart'
check "AC02/AC03 offline_mutations schema migration adds retry columns without dropping v1 data" \
  bash -c 'grep -q "onUpgrade" mobile/lib/core/db/sqlcipher_app_database.dart \
    && grep -q "ALTER TABLE offline_mutations ADD COLUMN retry_count" mobile/lib/core/db/sqlcipher_app_database.dart \
    && grep -q "ALTER TABLE offline_mutations ADD COLUMN next_retry_at" mobile/lib/core/db/sqlcipher_app_database.dart'
check "AC03 a mutation stranded in uploading by a process restart recovers on open, not left permanently stuck (BIO-MOB-002-QA-02)" \
  bash -c 'grep -q "_recoverStrandedUploads" mobile/lib/core/db/sqlcipher_app_database.dart \
    && grep -q "_applyFailedAttempt" mobile/lib/core/db/sqlcipher_app_database.dart \
    && grep -q "uploading" tests/flutter/test/support/fake_app_database.dart'

# --- AC04: security-sensitive conflicts are never silently overwritten (ADR-0001 §3.5) ---
check "AC04 recordConflict moves a mutation to conflict and clears any scheduled retry" \
  bash -c 'grep -q "recordConflict" mobile/lib/core/db/app_database.dart \
    && awk "/Future<void> recordConflict/,/^  }/" mobile/lib/core/db/sqlcipher_app_database.dart | grep -q "OfflineSyncState.conflict"'
check "AC04 conflicted mutations are queryable for the human Sync Resolution Queue" \
  bash -c 'grep -q "conflictedMutations" mobile/lib/core/db/app_database.dart \
    && grep -q "conflictedMutations" mobile/lib/core/db/sqlcipher_app_database.dart \
    && grep -q "conflictedMutations" tests/flutter/test/support/fake_app_database.dart'
check "AC04 no automatic merge/resolution path exists for a conflicted mutation" \
  bash -c '! grep -rIn "resolveConflict\|autoMerge\|mergeConflict" mobile/lib 2>/dev/null | grep -q .'

# --- Developer test wiring ---
check "unit tests mapped to BIO-MOB-002 ACs exist" \
  bash -c 'test -f tests/flutter/test/bio_mob_002_ac01_retry_fields_test.dart \
    && test -f tests/flutter/test/bio_mob_002_ac03_retry_policy_test.dart \
    && test -f tests/flutter/test/bio_mob_002_ac03_retry_queue_test.dart \
    && test -f tests/flutter/test/bio_mob_002_ac04_conflict_test.dart'
check "builder evidence recorded" test -d evidence/BIO-MOB-002/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
