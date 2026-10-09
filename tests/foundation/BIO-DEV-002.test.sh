#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DEV-002 (device certification matrix).
# Behavioural assertions (registration, effective-status resolution, concurrency, audit,
# tenant override isolation through the real BIO-TEN-002 host channel) live in the
# executable xunit suites under tests/dotnet/ (see scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE_DIR=api/Biolog.Api/Features/DeviceCertification

# --- AC01: certification records the vendor/model/firmware/sdk combination, status, and test evidence ---
check "AC01 DeviceCertification record exists" test -f "$FEATURE_DIR/DeviceCertification.cs"
check "AC01 record carries Firmware/Sdk/TestEvidenceReference/Version" \
  bash -c "grep -q 'Firmware' '$FEATURE_DIR/DeviceCertification.cs' && grep -q 'Sdk' '$FEATURE_DIR/DeviceCertification.cs' && grep -q 'TestEvidenceReference' '$FEATURE_DIR/DeviceCertification.cs' && grep -q 'Version' '$FEATURE_DIR/DeviceCertification.cs'"
check "AC01 the five certification statuses are the ones the user story names" bash -c "
  grep -q 'Certified,' '$FEATURE_DIR/CertificationStatus.cs' &&
  grep -q 'Supported,' '$FEATURE_DIR/CertificationStatus.cs' &&
  grep -q 'Experimental,' '$FEATURE_DIR/CertificationStatus.cs' &&
  grep -q 'Deprecated,' '$FEATURE_DIR/CertificationStatus.cs' &&
  grep -q 'Unsupported,' '$FEATURE_DIR/CertificationStatus.cs'
"
check "AC01 the key validator rejects reserved composite-key separators" \
  bash -c "grep -q \"Contains('/')\" '$FEATURE_DIR/DeviceCertificationKeyValidator.cs' && grep -q \"Contains(':')\" '$FEATURE_DIR/DeviceCertificationKeyValidator.cs'"
check "AC01 the key validator is enforced at both the service and repository boundaries" bash -c "
  grep -q 'DeviceCertificationKeyValidator.Validate' '$FEATURE_DIR/DeviceCertificationService.cs' &&
  grep -q 'DeviceCertificationKeyValidator.Validate' '$FEATURE_DIR/InMemoryDeviceCertificationRepository.cs'
"
check "AC01 test evidence reference is validated, not merely stored blindly" \
  bash -c "grep -q 'ValidateTestEvidenceReference' '$FEATURE_DIR/DeviceCertificationService.cs'"
check "AC01 developer tests exist for the key validator" \
  test -f tests/dotnet/Biolog.UnitTests/DeviceCertification/DeviceCertificationKeyValidatorTests.cs

# --- AC02: unsupported combinations are visible to admins ---
check "AC02 the global matrix listing endpoint exists" \
  bash -c "grep -q 'ListGlobalAsync' '$FEATURE_DIR/DeviceCertificationService.cs'"
check "AC02 the listing supports filtering by status" \
  bash -c "grep -q 'MapGet(\"/\", async (string? status' api/Biolog.Api/Modules/DeviceCertificationModule.cs"
check "AC02 developer tests prove an Unsupported entry surfaces in the listing" \
  bash -c "grep -q 'surfaces_an_unsupported_entry' tests/dotnet/Biolog.UnitTests/DeviceCertification/DeviceCertificationServiceTests.cs"

# --- AC03: certification status is independent per tenant deployment when needed ---
check "AC03 the repository scopes storage by an explicit nullable tenant id" \
  bash -c "grep -q 'string? tenantId' \"$FEATURE_DIR/IDeviceCertificationRepository.cs\""
check "AC03 the service exposes a tenant-override registration path requiring a resolved tenant" \
  bash -c "grep -q 'RegisterTenantOverrideAsync' '$FEATURE_DIR/DeviceCertificationService.cs' && grep -q 'RequireTenant' '$FEATURE_DIR/DeviceCertificationService.cs'"
check "AC03 effective-status lookup prefers a tenant override over the global default" \
  bash -c "grep -q 'GetEffectiveAsync' '$FEATURE_DIR/DeviceCertificationService.cs'"
check "AC03 the tenant-scoped endpoint group is a separate route from the control-plane one" \
  bash -c "grep -q '/api/v1/tenant/device-certifications' api/Biolog.Api/Modules/DeviceCertificationModule.cs && grep -q '/api/v1/device-certifications' api/Biolog.Api/Modules/DeviceCertificationModule.cs"
check "AC03 integration tests prove tenant-override precedence through the real BIO-TEN-002 host channel" \
  bash -c "grep -q 'RegisterAndVerifyDomainAsync' tests/dotnet/Biolog.IntegrationTests/DeviceCertificationTests.cs && grep -q 'takes_precedence_over_the_global_default' tests/dotnet/Biolog.IntegrationTests/DeviceCertificationTests.cs"

# --- AC04: changes are audited ---
check "AC04 a dedicated certification audit sink exists (not a reuse of DeviceRegistry's IAuditSink)" \
  test -f "$FEATURE_DIR/Audit/ICertificationAuditSink.cs"
check "AC04 the audit sink exposes ExistsAsync (ADR-0003 Decision 7 element 4)" \
  bash -c "grep -q 'ExistsAsync' \"$FEATURE_DIR/Audit/ICertificationAuditSink.cs\""
check "AC04 registration is audited before the repository commit" \
  bash -c "grep -q 'await AuditAsync(auditEvent, cancellationToken)' '$FEATURE_DIR/DeviceCertificationService.cs'"
check "AC04 status/evidence updates are audited and concurrency-checked" \
  bash -c "grep -q 'DeviceCertificationConcurrencyConflictException' '$FEATURE_DIR/DeviceCertificationService.cs'"
check "AC04 per-aggregate locking prevents the BIO-DEV-001-QA-02 class of false-success audit" \
  test -f "$FEATURE_DIR/DeviceCertificationLockRegistry.cs"
check "AC04 an ambiguous audit write past the point of no return uses CancellationToken.None" \
  bash -c "grep -q 'CancellationToken.None' '$FEATURE_DIR/DeviceCertificationService.cs'"
check "AC04 module maps an audit_unavailable problem response" \
  bash -c "grep -q 'audit_unavailable' api/Biolog.Api/Modules/DeviceCertificationModule.cs"

# --- Module wiring ---
check "DeviceCertificationModule exists and depends on Tenant" \
  bash -c "grep -q 'DependsOn => new\[\] { \"Tenant\" }' api/Biolog.Api/Modules/DeviceCertificationModule.cs"
check "DeviceCertificationModule is registered in Program.cs" \
  bash -c "grep -q 'new DeviceCertificationModule()' api/Biolog.Api/Program.cs"
check "DeviceCertificationModule registers its own audit sink, not a reuse of DeviceRegistryModule's IAuditSink" \
  bash -c "grep -q 'ICertificationAuditSink' api/Biolog.Api/Modules/DeviceCertificationModule.cs && ! grep -q 'AddSingleton<Biolog.Api.Audit.IAuditSink' api/Biolog.Api/Modules/DeviceCertificationModule.cs"

# --- Developer test wiring ---
check "unit tests exist for the service" test -f tests/dotnet/Biolog.UnitTests/DeviceCertification/DeviceCertificationServiceTests.cs
check "unit tests exist for the repository" test -f tests/dotnet/Biolog.UnitTests/DeviceCertification/InMemoryDeviceCertificationRepositoryTests.cs
check "integration tests exist through the real composed host" test -f tests/dotnet/Biolog.IntegrationTests/DeviceCertificationTests.cs
check "quality.sh runs this harness" bash -c "grep -q 'tests/foundation/BIO-DEV-002.test.sh' scripts/quality.sh"
check "docs/DEVICE-CERTIFICATION.md documents the feature" test -f docs/DEVICE-CERTIFICATION.md
check "builder evidence recorded" test -d evidence/BIO-DEV-002/builder
check "governance traceability spec recorded" test -f governance/stories/BIO-DEV-002.json

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
