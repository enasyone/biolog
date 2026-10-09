#!/usr/bin/env bash
# Offline-safe structural checks for BIO-ACC-003 (offline deny-list and
# cached access decisions). Behavioural assertions (cache merge, offline
# deny decision, stale-cache flagging, exactly-once upload) live in the
# executable Flutter suite under tests/flutter/; server-side behaviour
# (device auth, idempotent intake, tenant-scoped reads, the AC06 bridge)
# lives in tests/features/bio-acc-003/ (see docs/TESTING.md,
# scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

# --- AC01: edge cache is minimum necessary data ---
check "AC01 RevocationEntry carries only person id and revoked-at" \
  bash -c 'grep -q "class RevocationEntry" mobile/lib/core/access/revocation_entry.dart \
    && grep -q "personId" mobile/lib/core/access/revocation_entry.dart \
    && grep -q "revokedAt" mobile/lib/core/access/revocation_entry.dart'
check "AC01 the backend gateway maps down to person id + revoked-at only, not the full Person aggregate" \
  bash -c 'grep -q "record RevocationEntry(string PersonId, string RevokedAt)" api/features/bio-acc-003/Domain/IIdentityRevocationFeedGateway.cs'

# --- AC02: revocation deltas have priority (additive, monotonic cache) ---
check "AC02 the local cache merge is additive (INSERT OR REPLACE), never a destructive rebuild" \
  bash -c 'grep -q "ConflictAlgorithm.replace" mobile/lib/core/db/sqlcipher_app_database.dart'
check "AC02 the server delta feed is cursor-based and inclusive, matching the deny-list feed precedent" \
  bash -c 'grep -q "CompareOrdinal(p.UpdatedAt, since) >= 0" api/features/bio-idn-001/Domain/InMemoryPersonRepository.cs'
check "AC02 device-authenticated revocation reads never trust the ambient/host tenant channel (ADR-0002 Decision 7 pattern)" \
  bash -c 'grep -q "ICrossTenantPersonRevocationLookup" api/Biolog.Api/Modules/BioAcc003Module.cs'

# --- AC03: old card presentation logs locally and denies once revocation is cached ---
check "AC03 checkPresentation makes no network call - only AppDatabase reads/writes" \
  bash -c '! grep -q "http\." mobile/lib/core/access/access_cache_service.dart'
check "AC03 every presentation is durably logged locally regardless of outcome" \
  bash -c 'grep -q "recordPresentationEvent" mobile/lib/core/access/access_cache_service.dart'
check "AC03 a cached revocation produces the DENIED_REVOKED_CACHED reason code" \
  bash -c 'grep -q "reasonDeniedRevokedCached" mobile/lib/core/access/access_cache_service.dart'

# --- AC04: stale-cache policy is explicit ---
check "AC04 a documented staleness threshold exists and is not buried inline" \
  test -f mobile/lib/core/access/stale_cache_policy.dart
check "AC04 staleness is recorded as an explicit, durable flag on every logged decision" \
  bash -c 'grep -q "final bool stale" mobile/lib/core/access/presentation_event.dart'
check "AC04 staleness never appears in the allow/deny outcome computation itself" \
  bash -c 'line=$(grep -n "outcome: revoked ?" mobile/lib/core/access/access_cache_service.dart); \
    echo "$line" | grep -q "revoked ? PresentationOutcome.denied" && ! echo "$line" | grep -q stale'

# --- AC05: reconnect sync uploads events exactly once ---
check "AC05 the presentation-event upload uses the event id as the Idempotency-Key header" \
  bash -c 'grep -q "_idempotencyHeader.*event.eventId" mobile/lib/core/access/access_cache_sync_client.dart'
check "AC05 the server intake claims an idempotency record before committing, mirroring BIO-MOB-003" \
  bash -c 'grep -q "idempotencyStore.Claim" api/features/bio-acc-003/AccessCacheService.cs \
    && grep -q "IdempotencyKeyReuse" api/features/bio-acc-003/AccessCacheService.cs'
check "AC05 a fingerprint mismatch on a reused key is rejected, not silently replayed" \
  bash -c 'grep -q "ComputeFingerprint" api/features/bio-acc-003/AccessCacheService.cs'

# --- AC06: AccessService is connected through a live implementation ---
# Note: governance/ui-integration-map.json (BIO-ACC-003 -> surface web, port AccessService,
# route /access-control) lives in the orchestration control pack, not this repository's own
# tracked tree - not something a structural check here can read; verified once by reading it
# directly during design (see evidence/BIO-ACC-003/builder/SUMMARY.md).
check "AC06 the web live adapter route has a real matching backend endpoint" \
  bash -c 'grep -q "/api/v1/access/view-access-events" web/features/access/live.ts \
    && grep -q "/api/v1/access/view-access-events" api/features/bio-acc-003/Api/AccessCacheEndpoints.cs'
check "AC06 web/next.config.mjs rewrites the connected route to the real API host" \
  bash -c 'grep -q "/api/v1/access/view-access-events" web/next.config.mjs'
check "AC06 the approved access-control page/route/ports remain in place, untouched by this story" \
  bash -c 'test -f web/features/access/access-page.tsx \
    && grep -q "accessPort\|accessViewAccessEventsPort" web/features/access/access-page.tsx'

# --- Developer test wiring ---
check "unit/integration tests mapped to BIO-ACC-003 ACs exist (mobile)" \
  bash -c 'test -f tests/flutter/test/bio_acc_003_ac01_ac02_revocation_cache_test.dart \
    && test -f tests/flutter/test/bio_acc_003_ac03_ac04_offline_decision_test.dart \
    && test -f tests/flutter/test/bio_acc_003_ac05_sync_client_test.dart'
check "unit/integration tests mapped to BIO-ACC-003 ACs exist (backend)" \
  test -f tests/features/bio-acc-003/AccessCacheEndpointsIntegrationTests.cs
check "builder evidence recorded" test -d evidence/BIO-ACC-003/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
