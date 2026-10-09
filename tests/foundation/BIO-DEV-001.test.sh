#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DEV-001 (device registry foundation).
# Behavioural assertions (tenant isolation, concurrency, audit emission, HTTP wiring) live
# in the executable xunit suites under tests/dotnet/ (see docs/DEVICE-REGISTRY.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

# --- AC01: device records include serial/vendor/model/firmware/SDK/site/network/last-seen/status ---
check "AC01 Device record exists with the required fields" \
  bash -c 'f=api/Biolog.Api/Features/DeviceRegistry/Device.cs; test -f "$f" && grep -q "Serial" "$f" && grep -q "Vendor" "$f" && grep -q "Model" "$f" && grep -q "Firmware" "$f" && grep -q "Sdk" "$f" && grep -q "Site" "$f" && grep -q "Network" "$f" && grep -q "LastSeenAt" "$f" && grep -q "Status" "$f"'
check "AC01 device id is the canonical dev_ identifier (ADR-0001)" \
  bash -c 'grep -q "NewDeviceId" api/Biolog.Api/Features/DeviceRegistry/InMemoryDeviceRepository.cs && grep -q "\"dev\"" api/Biolog.Api/Identifiers/CanonicalId.cs'
check "AC01 device status is a defined vocabulary, not a free string" \
  test -f api/Biolog.Api/Features/DeviceRegistry/DeviceStatus.cs
check "AC01 mutable device aggregate carries a version (ADR-0001 Decision 3)" \
  bash -c 'grep -q "int Version" api/Biolog.Api/Features/DeviceRegistry/Device.cs'

# --- AC02: device credentials are referenced, not stored in plaintext ---
check "AC02 device carries a credential reference, never a secret field" \
  bash -c 'grep -q "CredentialReference" api/Biolog.Api/Features/DeviceRegistry/Device.cs && ! grep -qiE "credential(secret|password|key)" api/Biolog.Api/Features/DeviceRegistry/Device.cs'
check "AC02 no code path resolves a credential reference into a secret value" \
  bash -c '! grep -rqi "ResolveCredential\|DecryptCredential" api/Biolog.Api/Features/DeviceRegistry/'

# --- AC03: device status changes are audited ---
check "AC03 an audit envelope exists, matching ADR-0003 field names" \
  bash -c 'f=api/Biolog.Api/Audit/AuditEvent.cs; test -f "$f" && grep -q "EventId" "$f" && grep -q "TenantId" "$f" && grep -q "Actor" "$f" && grep -q "Target" "$f" && grep -q "Outcome" "$f" && grep -q "CorrelationId" "$f" && grep -q "OccurredAt" "$f" && grep -q "ReceivedAt" "$f"'
check "AC03 a status change writes an audit event before returning" \
  bash -c 'grep -q "_auditSink.RecordAsync" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs'
check "AC03 audit action names the specific event, not a generic label" \
  bash -c 'grep -q "device.status_changed" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs'
check "AC03 audit write failure is not swallowed (ADR-0002 Decision 5 hard dependency)" \
  bash -c 'grep -q "await _auditSink.RecordAsync" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs'

# --- AC04: tenant isolation applies ---
check "AC04 the service reads the resolved tenant context, never a caller-supplied tenant id" \
  bash -c 'grep -q "ITenantContextAccessor" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs && ! grep -qE "string tenantId|TenantId,\s*$" api/Biolog.Api/Features/DeviceRegistry/DeviceStatusChangeRequest.cs api/Biolog.Api/Features/DeviceRegistry/DeviceRegistrationRequest.cs'
check "AC04 a missing tenant context fails closed rather than defaulting" \
  test -f api/Biolog.Api/Tenancy/TenantContextMissingException.cs
check "AC04 the repository asserts a record's own tenant_id on every read/write" \
  bash -c 'grep -c "device.TenantId != tenant.TenantId\|existing.TenantId != tenant.TenantId" api/Biolog.Api/Features/DeviceRegistry/InMemoryDeviceRepository.cs | grep -qE "^[2-9]|^[1-9][0-9]"'
check "AC04 cross-tenant access returns not-found, never a forbidden/exists signal" \
  bash -c '! grep -qi "Forbid\|403" api/Biolog.Api/Modules/DeviceRegistryModule.cs'

# --- QA-01: a real positive path must exist through the composed host, not only the fail-closed one ---
check "QA-01 integration tests prove a resolved-tenant success path, not only the failure path" \
  bash -c 'grep -q "resolved_tenant" tests/dotnet/Biolog.IntegrationTests/DeviceRegistryTests.cs'
check "QA-01 the positive-path test double swaps ITenantContextAccessor via DI, never edits production tenant middleware" \
  bash -c 'grep -q "services.RemoveAll<ITenantContextAccessor>" tests/dotnet/Biolog.IntegrationTests/DeviceRegistryTests.cs && grep -q "AddScoped<ITenantContextAccessor" tests/dotnet/Biolog.IntegrationTests/DeviceRegistryTests.cs'
check "QA-01 fail-closed integration coverage is kept, not dropped in favor of the positive-path proof" \
  bash -c 'grep -q "fails_closed_with_no_resolved_tenant_context" tests/dotnet/Biolog.IntegrationTests/DeviceRegistryTests.cs'
check "QA-01 the specific blocking dependency (BIO-TEN-002) is named, not left vague" \
  bash -c 'grep -q "BIO-TEN-002" docs/DEVICE-REGISTRY.md'
check "QA-01 a tracked follow-up obligation is recorded for once BIO-TEN-002 ships" \
  bash -c 'grep -qi "tracked follow-up" docs/DEVICE-REGISTRY.md'

# --- QA-02: an audit-sink failure must not leave an unaudited committed status change ---
check "QA-02 the audit write happens before the repository commit, not after with a rollback" \
  bash -c 'audit=$(grep -n "await AuditAsync(auditEvent, cancellationToken)" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs | head -1 | cut -d: -f1); commit=$(grep -n "_repository.UpdateStatusAsync(tenant, deviceId, newStatus" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs | head -1 | cut -d: -f1); [ -n "$audit" ] && [ -n "$commit" ] && [ "$audit" -lt "$commit" ]'
check "QA-02 no compensating rollback exists - audit-before-commit removes the race by construction" \
  bash -c '! grep -q "CompensateAsync" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs'
check "QA-02 a stale version is rejected before the audit write, matching the pre-existing no-audit-for-invalid-attempt behaviour" \
  bash -c 'grep -q "throw new DeviceConcurrencyConflictException(current.Version)" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs'
check "QA-02 a per-device lock serializes the whole pre-check-audit-commit sequence (audit-before-commit alone was insufficient)" \
  bash -c 'test -f api/Biolog.Api/Features/DeviceRegistry/DeviceLockRegistry.cs && grep -q "_lockRegistry.AcquireAsync" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs'
check "QA-02 the lock registry is a singleton shared across every request, not scoped per DeviceService instance" \
  bash -c 'grep -q "AddSingleton<DeviceLockRegistry>" api/Biolog.Api/Modules/DeviceRegistryModule.cs'

# --- QA-03: caller cancellation after the audit write must not abandon the repository commit ---
check "QA-03 the repository commit no longer honours the caller's cancellation token" \
  bash -c 'grep -q "_repository.UpdateStatusAsync(tenant, deviceId, newStatus, expectedVersion, CancellationToken.None)" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs'

# --- ADR-0003 Decision 7 element 4: an ambiguous audit-write throw must be verified, not assumed ---
check "IAuditSink exposes ExistsAsync for verifying an ambiguous RecordAsync throw" \
  bash -c 'grep -q "Task<bool> ExistsAsync" api/Biolog.Api/Audit/IAuditSink.cs'
check "InMemoryAuditSink implements ExistsAsync" \
  bash -c 'grep -q "public Task<bool> ExistsAsync" api/Biolog.Api/Audit/InMemoryAuditSink.cs'
check "DeviceService verifies via ExistsAsync before treating a RecordAsync throw as a real failure" \
  bash -c 'grep -q "_auditSink.ExistsAsync" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs'
check "an unverifiable/confirmed-absent audit write throws the dedicated AuditUnavailableException" \
  test -f api/Biolog.Api/Audit/AuditUnavailableException.cs
check "the crash-between-audit-and-commit residual is confirmed inert and its reopening trigger is named" \
  bash -c 'grep -qi "confirmed inert" docs/DEVICE-REGISTRY.md && grep -qi "durable, remote, transactional, or multi-process" docs/DEVICE-REGISTRY.md'

# --- QA-04: a credential reference must be validated, not trusted by name alone ---
check "a credential-reference validator exists" \
  test -f api/Biolog.Api/Features/DeviceRegistry/CredentialReferenceValidator.cs
check "an invalid credential reference throws the dedicated InvalidCredentialReferenceException" \
  test -f api/Biolog.Api/Features/DeviceRegistry/InvalidCredentialReferenceException.cs
check "the request boundary (DeviceService.RegisterAsync) validates the credential reference" \
  bash -c 'grep -q "CredentialReferenceValidator.Validate" api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs'
check "the repository boundary (InMemoryDeviceRepository.CreateAsync) also validates the credential reference" \
  bash -c 'grep -q "CredentialReferenceValidator.Validate" api/Biolog.Api/Features/DeviceRegistry/InMemoryDeviceRepository.cs'
check "an invalid credential reference surfaces as a 400 problem, not a 500" \
  bash -c 'grep -q "InvalidCredentialReferenceException" api/Biolog.Api/Modules/DeviceRegistryModule.cs'
check "a dedicated regression test proves the repository rejects plaintext even if the service is bypassed" \
  bash -c 'grep -q "CredentialReferenceValidator\|InvalidCredentialReferenceException" tests/dotnet/Biolog.UnitTests/DeviceRegistry/InMemoryDeviceRepositoryTests.cs'

# --- API conventions ---
check "endpoints are versioned under /api/v1 (ADR-0004 Decision 1)" \
  bash -c 'grep -q "/api/v1/devices" api/Biolog.Api/Modules/DeviceRegistryModule.cs'
check "a mutating status write requires expected_version (ADR-0001 Decision 3.2)" \
  bash -c 'grep -q "ExpectedVersion" api/Biolog.Api/Features/DeviceRegistry/DeviceStatusChangeRequest.cs'
check "a stale expected_version is a 409, not a silent overwrite" \
  bash -c 'grep -q "Status409Conflict" api/Biolog.Api/Modules/DeviceRegistryModule.cs'

# --- Module registration ---
check "Program.cs registers DeviceRegistryModule through the module host" \
  bash -c 'grep -q "new DeviceRegistryModule()" api/Biolog.Api/Program.cs'
check "DeviceRegistryModule depends on Tenant so the tenant hook runs first" \
  bash -c 'grep -q "\"Tenant\"" api/Biolog.Api/Modules/DeviceRegistryModule.cs'

# --- Developer test wiring ---
check "unit tests cover the canonical id helper" \
  test -f tests/dotnet/Biolog.UnitTests/Identifiers/CanonicalIdTests.cs
check "unit tests cover DeviceService tenant/audit/concurrency behaviour" \
  test -f tests/dotnet/Biolog.UnitTests/DeviceRegistry/DeviceServiceTests.cs
check "unit tests cover the repository's concurrent-write race" \
  test -f tests/dotnet/Biolog.UnitTests/DeviceRegistry/InMemoryDeviceRepositoryTests.cs
check "integration tests exercise the real composed host" \
  test -f tests/dotnet/Biolog.IntegrationTests/DeviceRegistryTests.cs
check "docs/DEVICE-REGISTRY.md documents the feature and its scope boundaries" \
  test -f docs/DEVICE-REGISTRY.md
check "builder evidence recorded" test -d evidence/BIO-DEV-001/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
