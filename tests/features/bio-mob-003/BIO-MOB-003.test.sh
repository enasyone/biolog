#!/usr/bin/env bash
# Offline-safe structural checks for BIO-MOB-003's central offline-sync intake (the backend
# half of this story - see tests/foundation/BIO-MOB-003.test.sh for the mobile-side client
# checks). Behavioural assertions (device auth, idempotency, conflict routing, real HTTP
# pipeline) live in the executable xunit suites under tests/features/bio-mob-003/ and
# tests/dotnet/Biolog.IntegrationTests/OfflineSyncHostChannelTests.cs.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-mob-003
TESTS=tests/features/bio-mob-003

# --- Chioma's acceptance round 1 finding: an executable central endpoint must actually exist ---
check "a central offline-sync feature project exists" test -f "$FEATURE/Biolog.Features.OfflineSync.csproj"
check "project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.OfflineSync.csproj'"
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioMob003Module.cs && ! test -f '$FEATURE/BioMob003Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioMob003Module()' api/Biolog.Api/Program.cs"
check "the intake route matches what the mobile-side HttpSyncTransport already targets" \
  bash -c "grep -q '/api/v1/offline-mutations' '$FEATURE/Api/OfflineSyncEndpoints.cs' && grep -q '/api/v1/offline-mutations' mobile/lib/core/sync/sync_transport.dart"

# --- AC01: delta sync supports additions/updates/revocations, accepted centrally ---
check "the request DTO accepts operation and the arbitrary payload, matching toSyncDeltaJson()" \
  bash -c "grep -q 'operation' '$FEATURE/Api/OfflineMutationDtos.cs' && grep -q 'JsonElement Payload' '$FEATURE/Api/OfflineMutationDtos.cs'"
check "a real end-to-end test proves an addition, an update and a revocation are each accepted centrally" \
  bash -c "grep -q 'An_addition_is_accepted_centrally' '$TESTS/OfflineSyncEndpointsIntegrationTests.cs' &&
    grep -q 'An_update_against_the_now_current_version_is_accepted' '$TESTS/OfflineSyncEndpointsIntegrationTests.cs' &&
    grep -q 'A_revocation_is_accepted_centrally' '$TESTS/OfflineSyncEndpointsIntegrationTests.cs'"

# --- AC02: server acknowledgements are idempotent (ADR-0001 §3.4), proven for real ---
check "an idempotency store with claim/complete/release/get semantics exists" \
  bash -c "grep -q 'IIdempotencyStore' '$FEATURE/Domain/IdempotencyStore.cs' && grep -q 'Claim(' '$FEATURE/Domain/IdempotencyStore.cs'"
check "the endpoint requires Idempotency-Key, fail-closed" \
  bash -c "grep -q 'IdempotencyKeyRequired' '$FEATURE/Api/OfflineSyncEndpoints.cs'"
check "a real test proves a replay returns the original stored result with Idempotency-Replayed: true" \
  bash -c "grep -q 'Replaying_the_identical_request_with_the_same_idempotency_key_returns_the_original_stored_result' '$TESTS/OfflineSyncEndpointsIntegrationTests.cs'"
check "a real test proves reusing a key with different content is rejected, not executed again" \
  bash -c "grep -q 'Reusing_the_same_idempotency_key_with_different_content_is_rejected_not_executed_again' '$TESTS/OfflineSyncEndpointsIntegrationTests.cs'"

# --- AC03/AC04: critical conflicts enter the Sync Resolution Queue, no last-write-wins ---
check "a generic per-aggregate version ledger implements ADR-0001 §3.2 compare-and-set" \
  bash -c "grep -q 'IAggregateVersionLedger' '$FEATURE/Domain/AggregateVersionLedger.cs'"
check "a conflict is never merged - no merge/resolve path exists anywhere in this feature" \
  bash -c "! grep -rIn 'ResolveConflict\|AutoMerge\|MergeConflict\|LastWriteWins' $FEATURE 2>/dev/null | grep -q ."
check "a real test proves a stale base_version is rejected with the current version echoed back" \
  bash -c "grep -q 'A_stale_base_version_is_rejected_with_a_concurrency_conflict_carrying_the_current_version' '$TESTS/OfflineSyncEndpointsIntegrationTests.cs'"
check "a real test proves a conflict never advances the ledger or commits anything" \
  bash -c "grep -q 'A_conflict_never_advances_the_ledger_or_commits_anything_no_merge' '$TESTS/OfflineSyncEndpointsIntegrationTests.cs'"

# --- AC05: reconnect storms do not create duplicates, server side too ---
check "a real test proves two racing updates against the same aggregate do not both succeed" \
  bash -c "grep -q 'Two_updates_racing_the_same_established_aggregate' '$TESTS/OfflineSyncEndpointsIntegrationTests.cs'"

# --- Kemi's guardrail: tenant_id is a body claim, never trusted for routing (ADR-0002 Decision 1.1) ---
check "device authentication resolves the tenant, never trusting a request-supplied one" \
  bash -c "grep -q 'IDeviceTenantResolutionGateway' '$FEATURE/Api/DeviceAuthenticator.cs'"
check "the service cross-validates the payload's tenant_id claim against the independently resolved tenant" \
  bash -c "grep -q 'TenantMismatch' '$FEATURE/OfflineMutationIntakeService.cs'"
check "a real test proves a forged payload tenant_id is rejected, not routed" \
  bash -c "grep -q 'A_payload_tenant_id_that_does_not_match' '$TESTS/OfflineSyncEndpointsIntegrationTests.cs'"
check "device authentication requires both a device id and a verified device secret" \
  bash -c "grep -q 'DeviceIdHeaderName' '$FEATURE/Api/DeviceAuthenticator.cs' && grep -q 'credentialStore.Validate' '$FEATURE/Api/DeviceAuthenticator.cs'"
check "a device must also be ACTIVE, resolved via the shared device registry, not a per-feature fake in production" \
  bash -c "grep -q 'DeviceStatus.Active' api/Biolog.Api/Modules/BioMob003Module.cs"
check "the cross-tenant device lookup this endpoint depends on is internal, not a public escape hatch" \
  bash -c "grep -q 'internal interface ICrossTenantDeviceLookup' api/Biolog.Api/Features/DeviceRegistry/ICrossTenantDeviceLookup.cs"
check "no device secret is committed to checked-in app configuration" \
  bash -c "! grep -rq 'OfflineSync.*DeviceCredentials\|DeviceCredentials.*OfflineSync' api/Biolog.Api/appsettings.json api/Biolog.Api/appsettings.Development.json 2>/dev/null"

# --- Cross-feature dependency wiring ---
check "no ProjectReference to any other feature project (all cross-feature access is through this project's own gateways)" \
  bash -c "! grep -q '<ProjectReference' '$FEATURE/Biolog.Features.OfflineSync.csproj'"

# --- Developer test wiring ---
check "unit-level integration tests exist for the real HTTP endpoint" test -f "$TESTS/OfflineSyncEndpointsIntegrationTests.cs"
check "a real packaged-host channel test exists (real device registration, not merely an injected test double)" \
  test -f tests/dotnet/Biolog.IntegrationTests/OfflineSyncHostChannelTests.cs
check "all fixtures marked SYNTHETIC" bash -c \
  "grep -q SYNTHETIC '$TESTS/OfflineSyncEndpointsIntegrationTests.cs'"
check "builder evidence recorded" test -d evidence/BIO-MOB-003/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
