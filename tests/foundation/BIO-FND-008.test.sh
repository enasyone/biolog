#!/usr/bin/env bash
# Offline-safe structural checks for BIO-FND-008 (ASP.NET Core API/service shell).
# Behavioural assertions (boot, health status, OpenAPI shape, module ordering,
# tenant middleware behaviour) live in the executable xunit suites under
# tests/dotnet/ (see docs/TESTING.md, docs/API-SHELL.md, scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

# --- AC01: API boots locally against development configuration ---
check "AC01 Biolog.Api project exists" test -f api/Biolog.Api/Biolog.Api.csproj
check "AC01 project targets the pinned .NET SDK's TFM" bash -c 'grep -q "<TargetFramework>net10.0</TargetFramework>" api/Biolog.Api/Biolog.Api.csproj'
check "AC01 appsettings.json exists" test -f api/Biolog.Api/appsettings.json
check "AC01 appsettings.Development.json exists" test -f api/Biolog.Api/appsettings.Development.json
check "AC01 development launch profile sets ASPNETCORE_ENVIRONMENT" \
  bash -c 'grep -q "\"ASPNETCORE_ENVIRONMENT\": \"Development\"" api/Biolog.Api/Properties/launchSettings.json'
check "AC01 Program.cs exposes Program for WebApplicationFactory" \
  bash -c 'grep -q "public partial class Program" api/Biolog.Api/Program.cs'

# --- AC02: health/readiness endpoints exist ---
check "AC02 HealthModule registers health checks" bash -c 'grep -q "AddHealthChecks" api/Biolog.Api/Modules/HealthModule.cs'
check "AC02 liveness endpoint mapped, never evaluates checks" \
  bash -c 'grep -q "/health/live" api/Biolog.Api/Modules/HealthModule.cs && grep -q "Predicate = _ => false" api/Biolog.Api/Modules/HealthModule.cs'
check "AC02 readiness endpoint mapped, evaluates every check" \
  bash -c 'grep -q "/health/ready" api/Biolog.Api/Modules/HealthModule.cs && grep -q "Predicate = _ => true" api/Biolog.Api/Modules/HealthModule.cs'

# --- AC03: OpenAPI generation works ---
check "AC03 native OpenAPI package referenced (not a third-party generator)" \
  bash -c 'grep -q "Microsoft.AspNetCore.OpenApi" api/Biolog.Api/Biolog.Api.csproj'
check "AC03 OpenApiModule registers and maps the document" \
  bash -c 'grep -q "AddOpenApi" api/Biolog.Api/Modules/OpenApiModule.cs && grep -q "MapOpenApi" api/Biolog.Api/Modules/OpenApiModule.cs'
check "AC03/QA-01 Microsoft.OpenApi is pinned to the patched 2.12.2, not a vulnerable version" \
  bash -c 'grep -q "Microsoft.OpenApi\" Version=\"2\.12\.2\"" api/Biolog.Api/Biolog.Api.csproj'
check "AC03/QA-01 the resolution and its history are documented" \
  bash -c 'grep -q "GHSA-v5pm-xwqc-g5wc" api/Biolog.Api/Biolog.Api.csproj && grep -q "GHSA-v5pm-xwqc-g5wc" docs/API-SHELL.md'
check "AC03/QA-01 vulnerability audit gate exists and is wired into quality.sh --full" \
  bash -c 'test -x scripts/check-dotnet-vulnerabilities.sh && grep -q "check-dotnet-vulnerabilities.sh" scripts/quality.sh'

# --- QA-02: vulnerability gate fails closed on audit failure, not just on findings ---
# Uses a fake `dotnet` on PATH so these stay offline-safe and deterministic; the real
# dotnet-backed audit runs separately in evidence capture (docs/TESTING.md).
run_fake_audit() {
  local fixture="$1"
  local tmp
  tmp="$(mktemp -d)"
  cat > "$tmp/dotnet" <<EOF
#!/usr/bin/env bash
$fixture
EOF
  chmod +x "$tmp/dotnet"
  PATH="$tmp:$PATH" bash scripts/check-dotnet-vulnerabilities.sh fake.csproj >"$tmp/out" 2>&1
  echo "$?" > "$tmp/status"
  echo "$tmp"
}

tmp_clean="$(run_fake_audit 'echo "The given project \`fake\` has no vulnerable packages given the current sources."; exit 0')"
check "QA-02 clean audit output still passes" bash -c "[ \"\$(cat '$tmp_clean/status')\" = 0 ]"

tmp_vuln="$(run_fake_audit 'echo "The given project \`fake\` has the following vulnerable packages"; exit 0')"
check "QA-02 vulnerable-package output still fails" bash -c "[ \"\$(cat '$tmp_vuln/status')\" != 0 ]"

tmp_exit="$(run_fake_audit 'echo "synthetic transport failure" >&2; exit 42')"
check "QA-02 nonzero dotnet exit status fails closed (previously a false PASS)" \
  bash -c "[ \"\$(cat '$tmp_exit/status')\" != 0 ]"
check "QA-02 nonzero-exit failure is reported, not swallowed" \
  bash -c "grep -q 'audit did not complete' '$tmp_exit/out'"

tmp_advisory="$(run_fake_audit 'echo "warn : NU1900: Error occurred while getting package vulnerability data: Unable to load the service index for source https://api.nuget.org/v3/index.json."; exit 0')"
check "QA-02 advisory-source failure with exit 0 fails closed (previously a false PASS)" \
  bash -c "[ \"\$(cat '$tmp_advisory/status')\" != 0 ]"

# QA-03 (Seyi, found via BIO-DEV-001's review): real `dotnet list` output can print NU1900
# AND the "has no vulnerable packages" clean phrase in the SAME run - the advisory-source
# failure is a warning, not a replacement for the summary line. QA-02's fixture above never
# covered this combined shape, so the wrapper's "or unexpected output" fallback silently
# never fired and this exact output passed.
tmp_combined="$(run_fake_audit 'echo "warn : NU1900: Error occurred while getting package vulnerability data: nodename nor servname provided, or not known (api.nuget.org:443)."; echo "The given project \`fake\` has no vulnerable packages given the current sources."; exit 0')"
check "QA-03 advisory-source failure fails closed even when a clean phrase is ALSO present" \
  bash -c "[ \"\$(cat '$tmp_combined/status')\" != 0 ]"
check "QA-03 the combined-output failure names the advisory-source cause, not a generic message" \
  bash -c "grep -q 'advisory-source failure' '$tmp_combined/out'"

rm -rf "$tmp_clean" "$tmp_vuln" "$tmp_exit" "$tmp_advisory" "$tmp_combined"

# --- AC04: tenant middleware hook exists but invents no tenant defaults ---
check "AC04 tenant context accessor exists" test -f api/Biolog.Api/Tenancy/ITenantContextAccessor.cs
check "AC04 tenant middleware hook exists" test -f api/Biolog.Api/Tenancy/TenantContextMiddleware.cs
check "AC04/BIO-TEN-002 middleware fails closed to null on any resolution failure, never invents a tenant" \
  bash -c 'grep -q "catch (TenantDomainError)" api/Biolog.Api/Tenancy/TenantContextMiddleware.cs && grep -qE "return null;" api/Biolog.Api/Tenancy/TenantContextMiddleware.cs'
check "AC04 middleware does not read any caller-controlled channel (query, cookie, or a tenant-looking header) - only the real Host" \
  bash -c '! grep -qE "Request\.(Headers|Query|Cookies)|User\.Claims" api/Biolog.Api/Tenancy/TenantContextMiddleware.cs && grep -q "Request.Host.Host" api/Biolog.Api/Tenancy/TenantContextMiddleware.cs'
check "AC04 TenantModule wires the middleware into the pipeline" \
  bash -c 'grep -q "UseMiddleware<TenantContextMiddleware>" api/Biolog.Api/Modules/TenantModule.cs'

# --- AC05: module registration pattern prevents circular dependencies ---
check "AC05 module interface exists" test -f api/Biolog.Api/Modules/IBiologModule.cs
check "AC05 module host performs topological ordering" bash -c 'grep -q "TopologicalSort" api/Biolog.Api/Modules/ModuleHost.cs'
check "AC05 a cycle raises a dedicated exception" test -f api/Biolog.Api/Modules/CircularModuleDependencyException.cs
check "AC05 Program.cs registers modules through the host, not ad hoc" \
  bash -c 'grep -q "new ModuleHost(" api/Biolog.Api/Program.cs'
check "AC05 no feature project references Biolog.Api.csproj back (one-way dependency)" \
  bash -c 'test -x scripts/check-module-project-references.sh && bash scripts/check-module-project-references.sh'
check "AC05 project-reference-direction check fails closed on a circular reference" bash -c '
tmp=$(mktemp -d "${TMPDIR:-/tmp}/bio-fnd-008-modref.XXXXXX") || exit 1
[ -n "$tmp" ] && [ -d "$tmp" ] || exit 1
trap "rm -rf \"$tmp\"" EXIT
mkdir -p "$tmp/api/features/bio-fake-999"
cat > "$tmp/api/features/bio-fake-999/Fake.csproj" <<CSPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../../Biolog.Api/Biolog.Api.csproj" />
  </ItemGroup>
</Project>
CSPROJ
git -C "$tmp" init -q
cp scripts/check-module-project-references.sh "$tmp/check.sh"
! (cd "$tmp" && bash check.sh) >/dev/null 2>&1 || exit 1
output=$(cd "$tmp" && bash check.sh 2>&1)
printf "%s\n" "$output" | grep -q "Fake.csproj references Biolog.Api.csproj"
'
check "AC05 developer tests cover ordering and cycle detection" \
  test -f tests/dotnet/Biolog.UnitTests/Modules/ModuleHostTests.cs

# --- Developer test wiring ---
check "unit tests reference the api project" \
  bash -c 'grep -q "Biolog.Api.csproj" tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj'
check "integration tests reference the api project and Mvc.Testing" \
  bash -c 'grep -q "Biolog.Api.csproj" tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj && grep -q "Microsoft.AspNetCore.Mvc.Testing" tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj'
check "integration tests cover AC01-03 through a real HTTP pipeline" \
  test -f tests/dotnet/Biolog.IntegrationTests/ApiShellTests.cs
check "tenant middleware unit tests exist" \
  test -f tests/dotnet/Biolog.UnitTests/Tenancy/TenantContextMiddlewareTests.cs
check "docs/API-SHELL.md documents the shell" test -f docs/API-SHELL.md
check "builder evidence recorded" test -d evidence/BIO-FND-008/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
