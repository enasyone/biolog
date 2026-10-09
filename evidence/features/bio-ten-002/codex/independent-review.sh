#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
cd "$repo_root"

failures=0

if dotnet run --project evidence/features/bio-ten-002/codex/HostExactnessProbe/HostExactnessProbe.csproj --nologo; then
  echo "PASS malformed port/suffix inputs fail closed"
else
  echo "FAIL malformed port/suffix inputs alias a verified hostname"
  failures=$((failures + 1))
fi

if rg -q 'BioTen002Module' api/Biolog.Api --glob '*.cs' &&
   rg -q 'TenantDomainService|Biolog.Features.TenantDomainRegistry' api/Biolog.Api --glob '*.cs'; then
  echo "PASS shipped host wires BIO-TEN-002 into tenant context resolution"
else
  echo "FAIL shipped host has no BIO-TEN-002 module/context-resolution wiring"
  failures=$((failures + 1))
fi

if grep -q 'Request.Query\["host"\]' api/features/bio-ten-002/Api/TenantDomainEndpoints.cs; then
  echo "FAIL advertised host channel reads a query parameter, which ADR-0002 excludes as a channel"
  failures=$((failures + 1))
else
  echo "PASS host channel does not trust a query parameter"
fi

if rg -q 'TenantContext\? Context \{ get; set; \}' api/Biolog.Api/Tenancy/ITenantContextAccessor.cs; then
  echo "FAIL resolved tenant context remains publicly replaceable through the request accessor"
  failures=$((failures + 1))
else
  echo "PASS downstream consumers cannot replace the resolved tenant context"
fi

if rg -q 'public sealed record TenantContext\(' api/Biolog.Api/Tenancy/TenantContext.cs; then
  echo "FAIL callers can publicly construct a context that did not come from resolution"
  failures=$((failures + 1))
else
  echo "PASS tenant contexts cannot be publicly forged"
fi

exit "$failures"
