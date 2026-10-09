#!/usr/bin/env bash
# Offline-safe structural checks for BIO-MOB-004 (mobile identity/card/face
# verification flows). Behavioural assertions (online/offline routing,
# device-role gating, sanitised decision shape) live in the executable
# Flutter suite under tests/flutter/; server-side behaviour (device auth,
# in-process consumption of BIO-ACC-002's decision service, role gate,
# override, idempotency pass-through) lives in tests/features/bio-mob-004/
# (see docs/TESTING.md, scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

# --- AC01: flows show identity/status/decision without exposing unnecessary biometric internals ---
check "AC01 the sanitised decision result type carries no raw biometric evidence/score property (declarations only, not the explanatory comments about their absence)" \
  bash -c '! grep -v "^\s*///" api/features/bio-mob-004/Domain/IAccessDecisionGateway.cs | grep -qE "(IReadOnlyList<BiometricEvidenceEntry>|double\??[[:space:]]+Score)"'
check "AC01 the module adapter never reads BIO-ACC-002's own biometric evidence when translating a decision (code, not the explanatory comments about not doing so)" \
  bash -c '! grep -v "^\s*///" api/Biolog.Api/Modules/BioMob004Module.cs | grep -q "\.BiometricEvidence"'
check "AC01 the mobile outcome type declares no evidence/score field (code, not the explanatory comment header)" \
  bash -c '! grep -v "^//" mobile/lib/core/verification/verification_outcome.dart | grep -qi "score\|biometricEvidence"'

# --- AC02: offline state is visible ---
check "AC02 VerificationService's path is driven by the caller's own connectivity state, never a hidden fallback" \
  bash -c 'grep -q "ConnectivityState connectivity" mobile/lib/core/verification/verification_service.dart'
check "AC02 the offline path reuses BIO-ACC-003's already-built revocation-cache check, not a new mechanism" \
  bash -c 'grep -q "AccessCacheService" mobile/lib/core/verification/verification_service.dart'
check "AC02 the verify screen labels which path produced the result and flags a stale cache" \
  bash -c 'grep -q "VerificationSource.offlineCache" mobile/lib/features/verify/verify_screen.dart \
    && grep -q "stale" mobile/lib/features/verify/verify_screen.dart'

# --- AC03: actions obey role permissions ---
check "AC03 a closed device-role enum exists, distinct from BIO-IDN-006's own human RoleType" \
  bash -c 'grep -q "enum VerificationDeviceRole" api/features/bio-mob-004/Domain/VerificationDeviceRole.cs'
check "AC03 the default role for an unassigned device is Officer (least-privilege default)" \
  bash -c 'grep -q "VerificationDeviceRole.Officer" api/features/bio-mob-004/Domain/VerificationDeviceRole.cs'
check "AC03 only Supervisor may override a computed deny; both roles may execute/read history" \
  bash -c 'grep -q "OverrideDeny => role == VerificationDeviceRole.Supervisor" api/features/bio-mob-004/Domain/VerificationDeviceRole.cs'
check "AC03 an override requires a mandatory reason_code, mirroring BIO-MOB-005's DecisionReasonRequired" \
  bash -c 'grep -q "OverrideReasonRequired" api/features/bio-mob-004/VerificationService.cs \
    && grep -q "OverrideReasonRequired" api/features/bio-mob-004/Domain/VerificationError.cs'
check "AC03 the device-role assignment write is gated by real human RBAC (ADR-0006 Decision 3), not device auth" \
  bash -c 'grep -q "ManageDeviceRolePermissionKey" api/features/bio-mob-004/Api/VerificationEndpoints.cs \
    && grep -q "provenanceGate.Require" api/features/bio-mob-004/Api/VerificationEndpoints.cs'
check "AC03 the deviceId->role mapping lives entirely in this story's own owned path, DeviceRegistry's own Device record is never touched" \
  bash -c '! grep -rq "VerificationDeviceRole\|IVerificationDeviceRoleRepository" api/Biolog.Api/Features/DeviceRegistry/'
check "AC03 override/history buttons are hidden, not merely disabled, until this device's own role is known" \
  bash -c 'grep -q "canOverride" mobile/lib/features/verify/verify_screen.dart \
    && grep -q "canReadHistory" mobile/lib/features/verify/verify_screen.dart'

# --- AC04: verification events sync idempotently ---
check "AC04 the decision endpoint requires an Idempotency-Key header, mirroring ADR-0001 S3.4" \
  bash -c 'grep -q "IdempotencyKeyHeaderName" api/features/bio-mob-004/Api/VerificationEndpoints.cs \
    && grep -q "IdempotencyKeyRequired" api/features/bio-mob-004/Domain/VerificationError.cs'
check "AC04 the idempotency key is passed straight through to BIO-ACC-002's own already-reviewed idempotency guarantee, not duplicated" \
  bash -c 'grep -q "IdempotencyKey = idempotencyKey" api/features/bio-mob-004/VerificationService.cs \
    && grep -q "IdempotencyKey = query.IdempotencyKey" api/Biolog.Api/Modules/BioMob004Module.cs'
check "AC04 the mobile sync client sends the same Idempotency-Key header convention every other write endpoint in this app uses" \
  bash -c 'grep -q "Idempotency-Key" mobile/lib/core/verification/verification_sync_client.dart'

# --- AC05: reconnect sync uploads packages exactly once; mobile surface connected ---
check "AC05 no ProjectReference exists from this feature to BIO-ACC-002 or DeviceRegistry - consumed only through this feature's own gateway interfaces" \
  bash -c '! grep -q "<ProjectReference" api/features/bio-mob-004/Biolog.Features.MobileVerification.csproj'
check "AC05 BIO-ACC-002's own internal AccessDecisionService is consumed via a manual factory, never the reflection-based DI activator" \
  bash -c 'grep -q "AccessDecisionService Build(string tenantId)" api/Biolog.Api/Modules/BioMob004Module.cs \
    && grep -q "new FixedTenantProvider(tenantId)" api/Biolog.Api/Modules/BioMob004Module.cs'
check "AC05 the mobile verify screen (the approved MobileService surface) is wired to real VerificationService/VerificationSyncClient calls, not a placeholder" \
  bash -c '! grep -q "FeaturePlaceholder" mobile/lib/features/verify/verify_screen.dart \
    && grep -q "VerificationService" mobile/lib/features/verify/verify_screen.dart \
    && grep -q "VerificationSyncClient" mobile/lib/features/verify/verify_screen.dart'
check "AC05 the module is registered into the host and depends on DeviceRegistry, AccessDecisions, Tenant and Rbac" \
  bash -c 'grep -q "new BioMob004Module()" api/Biolog.Api/Program.cs \
    && grep -q "\"DeviceRegistry\", \"AccessDecisions\", \"Tenant\", \"Rbac\"" api/Biolog.Api/Modules/BioMob004Module.cs'

# --- Developer test wiring ---
check "unit/integration tests mapped to BIO-MOB-004 ACs exist (mobile)" \
  bash -c 'test -f tests/flutter/test/bio_mob_004_ac01_ac03_ac04_sync_client_test.dart \
    && test -f tests/flutter/test/bio_mob_004_ac02_verification_service_test.dart'
check "unit/integration tests mapped to BIO-MOB-004 ACs exist (backend)" \
  test -f tests/features/bio-mob-004/VerificationEndpointsIntegrationTests.cs
check "builder evidence recorded" test -d evidence/BIO-MOB-004/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
