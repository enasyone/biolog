#!/usr/bin/env bash
# Offline-safe structural checks for BIO-TEN-002 (tenant domain mapping and verification).
# Behavioural assertions (resolution, verification lifecycle, duplicate prevention, audit,
# HTTP pipeline) live in the executable xunit suite under tests/features/bio-ten-002/ (see
# docs/features/bio-ten-002/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-ten-002

# --- AC01: TenantDomain stores hostname, tenant, status and primary flag ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.TenantDomainRegistry.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.TenantDomainRegistry.csproj'"
check "AC01 TenantDomain carries hostname/tenant_id/status/is_primary" \
  bash -c "grep -q 'string Hostname' '$FEATURE/Domain/TenantDomain.cs' && grep -q 'string TenantId' '$FEATURE/Domain/TenantDomain.cs' && grep -q 'TenantDomainStatus Status' '$FEATURE/Domain/TenantDomain.cs' && grep -q 'bool IsPrimary' '$FEATURE/Domain/TenantDomain.cs'"
check "AC01 module registers into the shared host shape (IBiologModule)" \
  bash -c "grep -q 'IBiologModule' api/Biolog.Api/Modules/BioTen002Module.cs"
check "BIO-TEN-002-QA-01 follow-up: module lives one-way in api/Biolog.Api/Modules, not back in the feature project (BIO-TEN-001's circular-reference finding)" \
  bash -c "test -f api/Biolog.Api/Modules/BioTen002Module.cs && ! test -f '$FEATURE/BioTen002Module.cs'"
check "BIO-TEN-002-QA-01 follow-up: registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioTen002Module()' api/Biolog.Api/Program.cs"
check "BIO-TEN-002-QA-01 follow-up: TenantDomainService is wired into TenantContextMiddleware's host channel" \
  bash -c "grep -q 'TenantDomainService' api/Biolog.Api/Tenancy/TenantContextMiddleware.cs && grep -q 'ResolveAsync' api/Biolog.Api/Tenancy/TenantContextMiddleware.cs"
check "BIO-TEN-002-QA-01 follow-up: the one-way project-reference check passes" \
  bash scripts/check-module-project-references.sh
check "AC01 endpoints are versioned per ADR-0004 §1.1 and under /control-plane" \
  bash -c "grep -q '/api/v1/control-plane/tenant-domains' '$FEATURE/Api/TenantDomainEndpoints.cs'"

# --- AC02: host resolution is case-normalised and exact ---
check "AC02 host normalizer exists" test -f "$FEATURE/Resolution/HostNormalizer.cs"
check "AC02 normalizer lowercases" bash -c "grep -q 'ToLowerInvariant' '$FEATURE/Resolution/HostNormalizer.cs'"
check "AC02 normalizer rejects non-ASCII rather than converting it (no punycode/IDN conversion API used)" \
  bash -c "grep -q '0x7F' '$FEATURE/Resolution/HostNormalizer.cs' && ! grep -q 'IdnMapping\|\.ToASCII(' '$FEATURE/Resolution/HostNormalizer.cs'"
check "AC02 resolution goes through the same normalizer used at registration (single source of exactness)" \
  bash -c "grep -q 'HostNormalizer.Normalize' '$FEATURE/TenantDomainService.cs'"
check "BIO-TEN-002-QA-01 round 1: the resolve endpoint reads the real Host header, never a query parameter (code, not prose)" \
  bash -c "grep -q 'context.Request.Host.Host' '$FEATURE/Api/TenantDomainEndpoints.cs' && ! grep -v '^\s*///' '$FEATURE/Api/TenantDomainEndpoints.cs' | grep -q 'Request.Query\[.host.\]'"
check "BIO-TEN-002-QA-01 round 1: a test proves the query string is ignored, not merely unused" \
  bash -c "grep -q 'Resolve_ignores_a_query_string_host_parameter_entirely' tests/features/bio-ten-002/TenantDomainEndpointsIntegrationTests.cs"
check "BIO-TEN-002-QA-02 round 1: a colon suffix is validated as a syntactic port before being stripped" \
  bash -c "grep -q 'portPart.All(char.IsAsciiDigit)' '$FEATURE/Resolution/HostNormalizer.cs'"
check "BIO-TEN-002-QA-02 round 1: negative tests exist for the malformed-port aliasing Seyi found" \
  bash -c "grep -q 'A_malformed_port_suffix_is_rejected_not_silently_stripped' tests/features/bio-ten-002/HostResolutionTests.cs"

# --- AC03: unverified/unknown hosts fail closed ---
check "AC03 host_not_recognised error exists and is a 404" \
  bash -c "grep -q 'HostNotRecognised' '$FEATURE/Domain/TenantDomainError.cs' && grep -A2 'HostNotRecognised()' '$FEATURE/Domain/TenantDomainError.cs' | grep -q ', 404)'"
check "AC03 resolution only accepts VERIFIED status" \
  bash -c "grep -q 'TenantDomainStatus.Verified' '$FEATURE/TenantDomainService.cs'"
check "AC03 unknown-vs-unverified is deliberately indistinguishable (single throw site, no separate 'exists but unverified' error)" \
  bash -c "[ \"\$(grep -c 'throw TenantDomainError.HostNotRecognised()' '$FEATURE/TenantDomainService.cs')\" = 2 ]"

# --- AC04: duplicate hostname assignment across tenants is prevented ---
check "AC04 duplicate_hostname error exists and is a 409" \
  bash -c "grep -q 'DuplicateHostname' '$FEATURE/Domain/TenantDomainError.cs' && grep -A2 'DuplicateHostname(string hostname)' '$FEATURE/Domain/TenantDomainError.cs' | grep -q ', 409,'"
check "AC04 the registry enforces uniqueness atomically via a hostname-keyed TryAdd" \
  bash -c "grep -q '_domains.TryAdd' '$FEATURE/Domain/InMemoryTenantDomainRegistry.cs'"
check "AC04 a cross-tenant duplicate-hostname test exists" \
  bash -c "grep -q 'Register_rejects_a_hostname_already_claimed_by_another_tenant' tests/features/bio-ten-002/TenantDomainServiceTests.cs"

# --- AC05: A-record/CNAME deployment detail is separated from application routing ---
check "AC05 no DNS deployment field exists anywhere in the domain/request/response shapes (property declarations, not prose)" \
  bash -c "! grep -v '^\s*///' '$FEATURE/Domain/TenantDomain.cs' '$FEATURE/Api/TenantDomainRequests.cs' '$FEATURE/Api/TenantDomainResponse.cs' | grep -qiE 'a_record|cname|dns_target|\bttl\b'"
check "AC05 a structural test proves the omission adversarially, not just by inspection" \
  bash -c "test -f tests/features/bio-ten-002/TenantDomainScopeTests.cs && grep -q 'DnsDeploymentFieldNames' tests/features/bio-ten-002/TenantDomainScopeTests.cs"

# --- ADR-0003 Decision 7: audit-atomicity pattern, applied to this story's own aggregate ---
check "Decision 7 element 1: per-aggregate locks exist for both scopes (hostname and tenant)" \
  bash -c "grep -q 'LockHostnameAsync' '$FEATURE/TenantDomainService.cs' && grep -q 'LockTenantAsync' '$FEATURE/TenantDomainService.cs'"
check "Decision 7 element 2: the registry commit is the return statement (audit already happened above it)" \
  bash -c "grep -qE 'return await registry\.(CreateAsync|ReplaceAsync)\(' '$FEATURE/TenantDomainService.cs'"
check "Decision 7 element 3: ExistsAsync verification on an ambiguous audit-write throw" \
  bash -c "grep -q 'ExistsAsync' '$FEATURE/Audit/ITenantDomainAuditSink.cs' && grep -q 'ExistsAsync' '$FEATURE/Audit/InMemoryTenantDomainAuditSink.cs' && grep -q 'auditSink.ExistsAsync' '$FEATURE/TenantDomainService.cs'"
check "Decision 7 element 4: CancellationToken.None on every commit once the audit is confirmed" \
  bash -c "[ \"\$(grep -c 'registry\.\(CreateAsync\|ReplaceAsync\)([^)]*, CancellationToken\.None)' '$FEATURE/TenantDomainService.cs')\" = 4 ]"
check "the SetPrimary two-record commit is proven under one shared tenant lock, not two independent hostname locks" \
  bash -c "grep -q 'SetPrimary_on_two_different_domains_of_the_same_tenant_never_leaves_two_domains_primary' tests/features/bio-ten-002/TenantDomainAuditTests.cs"
check "a same-hostname concurrency race is proven (BIO-TEN-001 round-3-shape reproduction)" \
  bash -c "grep -q 'Two_genuinely_concurrent_status_updates_of_the_same_domain_never_produce_a_false_allowed_audit' tests/features/bio-ten-002/TenantDomainAuditTests.cs"
check "audit sink failure never leaves a committed-but-unaudited record (register/status/set-primary)" \
  bash -c "grep -q 'Register_is_never_committed_when_the_audit_sink_fails' tests/features/bio-ten-002/TenantDomainAuditTests.cs && grep -q 'UpdateStatus_is_never_committed_when_the_audit_sink_fails' tests/features/bio-ten-002/TenantDomainAuditTests.cs && grep -q 'SetPrimary_is_never_committed_when_the_audit_sink_fails' tests/features/bio-ten-002/TenantDomainAuditTests.cs"
check "a cancellation arriving right after a confirmed audit write does not abort the commit" \
  bash -c "grep -q 'A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit' tests/features/bio-ten-002/TenantDomainAuditTests.cs"

# --- Developer test wiring ---
check "unit/audit/resolution/scope tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.TenantDomainRegistry.csproj' tests/features/bio-ten-002/Biolog.Features.TenantDomainRegistry.Tests.csproj"
check "integration tests exist and reference TestHost" \
  bash -c "test -f tests/features/bio-ten-002/TenantDomainEndpointsIntegrationTests.cs && grep -q 'Microsoft.AspNetCore.TestHost' tests/features/bio-ten-002/Biolog.Features.TenantDomainRegistry.Tests.csproj"
check "docs/features/bio-ten-002/README.md documents the feature" test -f docs/features/bio-ten-002/README.md
check "builder evidence recorded" test -d evidence/features/bio-ten-002/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
