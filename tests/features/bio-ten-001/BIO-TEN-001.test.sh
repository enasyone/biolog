#!/usr/bin/env bash
# Offline-safe structural checks for BIO-TEN-001 (control-plane tenant registry).
# Behavioural assertions (CRUD, validation, status transitions, audit, HTTP pipeline)
# live in the executable xunit suite under tests/features/bio-ten-001/ (see
# docs/features/bio-ten-001/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-ten-001

# --- AC01: Tenant CRUD is control-plane scoped ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.TenantRegistry.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.TenantRegistry.csproj'"
check "AC01 Tenant carries no tenant_id field (it IS the control-plane boundary, ADR-0002 §1.4)" \
  bash -c "! grep -q 'TenantId' '$FEATURE/Domain/Tenant.cs'"
check "AC01 registry/service take no tenant context anywhere" \
  bash -c "! grep -qE 'TenantContext[^A-Za-z]' '$FEATURE/Domain/ITenantRegistry.cs' '$FEATURE/TenantService.cs'"
check "AC01 endpoints are versioned per ADR-0004 §1.1 and under /control-plane" \
  bash -c "grep -q '/api/v1/control-plane/tenants' '$FEATURE/Api/TenantEndpoints.cs'"
check "AC01 module registers into the shared host shape (IBiologModule)" \
  bash -c "grep -q 'IBiologModule' api/Biolog.Api/Modules/BioTen001Module.cs"
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project (circular-reference finding, fixed during integration)" \
  bash -c "test -f api/Biolog.Api/Modules/BioTen001Module.cs && ! test -f '$FEATURE/BioTen001Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioTen001Module()' api/Biolog.Api/Program.cs"

# --- AC02: operational person/credential data cannot be stored in control-plane tables ---
check "AC02 request DTOs reject unrecognised JSON members (structural enforcement)" \
  bash -c "grep -q 'JsonUnmappedMemberHandling.Disallow' '$FEATURE/Api/TenantRequests.cs'"
check "AC02 Tenant has a fixed, named property list (no free-form dictionary)" \
  bash -c "! grep -qE 'Dictionary<string, ?(object|string)' '$FEATURE/Domain/Tenant.cs'"
check "AC02 adversarial field-smuggling tests exist" \
  test -f tests/features/bio-ten-001/ControlPlaneScopeTests.cs

# --- AC03: tenant status supports active/suspended/disabled ---
check "AC03 TenantStatus enum has exactly the three documented values" \
  bash -c "grep -qE '^\s*Active,\s*$' '$FEATURE/Domain/TenantStatus.cs' && grep -qE '^\s*Suspended,\s*$' '$FEATURE/Domain/TenantStatus.cs' && grep -qE '^\s*Disabled,\s*$' '$FEATURE/Domain/TenantStatus.cs'"
check "AC03 status update endpoint exists" \
  bash -c "grep -q 'status' '$FEATURE/Api/TenantEndpoints.cs'"

# --- AC04: audit events record administrative changes ---
check "AC04 audit event envelope matches contracts/schemas/audit-event.schema.json field names" \
  bash -c "grep -q 'EventId' '$FEATURE/Audit/TenantAuditEvent.cs' && grep -q 'CorrelationId' '$FEATURE/Audit/TenantAuditEvent.cs' && grep -q 'ReasonCode' '$FEATURE/Audit/TenantAuditEvent.cs'"
check "AC04 audit write is awaited before a mutation is treated as complete" \
  bash -c "grep -q 'await auditSink.WriteAsync' '$FEATURE/TenantService.cs'"
check "AC04 audit sink failure surfaces as audit_unavailable, not a silent success" \
  bash -c "grep -q 'audit_unavailable' '$FEATURE/Domain/TenantRegistryError.cs'"
check "AC04/QA-01 the registry commit is the return statement (audit already happened above it)" \
  bash -c "grep -qE 'return await registry\.(CreateAsync|ReplaceAsync)\(' '$FEATURE/TenantService.cs'"
check "AC04/QA-01 no rollback machinery remains (nothing is ever committed before audit succeeds)" \
  bash -c "! grep -q 'RemoveIfUnchangedAsync' '$FEATURE/Domain/ITenantRegistry.cs' && ! grep -q 'RestoreAsync' '$FEATURE/TenantService.cs'"
check "AC04/QA-01 never-committed tests assert registry state, not only the thrown error" \
  bash -c "grep -q 'is_never_committed_when_the_audit_sink_fails' tests/features/bio-ten-001/TenantAuditTests.cs"
check "AC04/QA-01 the exact writer-A/writer-B interleaving from Seyi's re-review is covered" \
  bash -c "grep -q 'A_failing_writers_field_never_survives_in_a_later_writers_committed_version' tests/features/bio-ten-001/TenantAuditTests.cs"
check "AC04/QA-01 round 3: all four mutations hold a per-tenant lock across audit-then-commit" \
  bash -c "[ \"\$(grep -c 'using var _ = await LockTenantAsync' '$FEATURE/TenantService.cs')\" = 4 ]"
check "AC04/QA-01 round 3: a genuine two-writer race never produces a false ALLOWED audit" \
  bash -c "grep -q 'Two_genuinely_concurrent_writers_of_the_same_tenant_never_produce_a_false_allowed_audit_for_a_rejected_commit' tests/features/bio-ten-001/TenantAuditTests.cs"
check "AC04/QA-01 round 4: a throw-after-durable-write is resolved by verification, not assumed a failure" \
  bash -c "grep -q 'ExistsAsync' '$FEATURE/Audit/ITenantAuditSink.cs' && grep -q 'ExistsAsync' '$FEATURE/Audit/InMemoryTenantAuditSink.cs' && grep -q 'auditSink.ExistsAsync' '$FEATURE/TenantService.cs'"
check "AC04/QA-01 round 4: a durably-written-then-acked-false event still results in a correct commit" \
  bash -c "grep -q 'An_audit_write_that_durably_lands_but_throws_on_acknowledgement_still_commits_correctly' tests/features/bio-ten-001/TenantAuditTests.cs"
check "AC04/QA-01 the registry commit uses CancellationToken.None once the audit write is confirmed (Kemi heads-up)" \
  bash -c "[ \"\$(grep -c 'registry\.\(CreateAsync\|ReplaceAsync\)([^)]*, CancellationToken\.None)' '$FEATURE/TenantService.cs')\" = 4 ]"
check "AC04/QA-01 a cancellation arriving right after the audit write does not abort the commit" \
  bash -c "grep -q 'A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit' tests/features/bio-ten-001/TenantAuditTests.cs"
check "AC04 audit-specific tests exist" test -f tests/features/bio-ten-001/TenantAuditTests.cs

# --- Developer test wiring ---
check "unit/audit/scope tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.TenantRegistry.csproj' tests/features/bio-ten-001/Biolog.Features.TenantRegistry.Tests.csproj"
check "integration tests exist and reference TestHost" \
  bash -c "test -f tests/features/bio-ten-001/TenantEndpointsIntegrationTests.cs && grep -q 'Microsoft.AspNetCore.TestHost' tests/features/bio-ten-001/Biolog.Features.TenantRegistry.Tests.csproj"
check "docs/features/bio-ten-001/README.md documents the feature" test -f docs/features/bio-ten-001/README.md
check "builder evidence recorded" test -d evidence/features/bio-ten-001/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
