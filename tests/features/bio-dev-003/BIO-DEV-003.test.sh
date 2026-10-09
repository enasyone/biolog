#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DEV-003 (vendor adapter framework for devices).
# Behavioural assertions live in the executable xunit suites under tests/features/bio-dev-003/
# and tests/dotnet/Biolog.IntegrationTests/DeviceAdapterEndpointsTests.cs (see
# docs/features/bio-dev-003/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-dev-003/Biolog.Features.DeviceAdapters

# --- AC01: vendor SDK code is isolated behind typed interfaces ---
check "AC01 feature project exists" test -f "$FEATURE.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE.csproj'"
check "AC01 feature project has no ProjectReference to Biolog.Api.csproj" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\\.Api\\.csproj' '$FEATURE.csproj'"
check "AC01 the typed adapter interface exists" test -f "$FEATURE/Domain/IDeviceAdapter.cs"
check "AC01 an adapter exists for each of the four named device kinds" bash -c "
  test -f '$FEATURE/Adapters/AcmeFingerprintScannerAdapter.cs' &&
  test -f '$FEATURE/Adapters/NovaVisionFaceTerminalAdapter.cs' &&
  test -f '$FEATURE/Adapters/OrbitIdNfcUhfReaderAdapter.cs' &&
  test -f '$FEATURE/Adapters/PrintWorksCardPrinterAdapter.cs'"
check "AC01 every adapter implements the one typed interface, not four separate ad-hoc shapes" \
  bash -c "grep -l ': IDeviceAdapter' '$FEATURE'/Adapters/*.cs | wc -l | grep -q '^ *4$'"
check "AC01 a registry resolves the right adapter by vendor/model" test -f "$FEATURE/Domain/IDeviceAdapterRegistry.cs"

# --- AC02: device capability discovery and health are supported ---
check "AC02 capability discovery domain type exists" test -f "$FEATURE/Domain/DeviceCapabilities.cs"
check "AC02 health domain type exists" test -f "$FEATURE/Domain/DeviceHealth.cs"
check "AC02 the orchestration service exposes both discovery and health" bash -c "
  grep -q 'DiscoverCapabilitiesAsync' '$FEATURE/Domain/DeviceAdapterService.cs' &&
  grep -q 'CheckHealthAsync' '$FEATURE/Domain/DeviceAdapterService.cs'"
check "AC02 endpoints are mapped for capabilities and health" bash -c "
  grep -q '/capabilities' api/Biolog.Api/Modules/BioDev003Module.cs &&
  grep -q '/health' api/Biolog.Api/Modules/BioDev003Module.cs"

# --- AC03: unsupported firmware/SDK combinations surface certification status ---
check "AC03 a typed exception carries the certification status" \
  bash -c "grep -q 'StatusLabel' '$FEATURE/Domain/UnsupportedDeviceCombinationException.cs'"
check "AC03 the certification gate is checked before the adapter registry is resolved" \
  bash -c "awk '/GateThenInvokeAsync/,/^    }/' '$FEATURE/Domain/DeviceAdapterService.cs' | grep -n 'certificationGate.CheckAsync\|registry.Resolve' | head -2 | tr '\\n' ' ' | grep -q 'certificationGate.CheckAsync.*registry.Resolve'"
check "AC03 the module bridges the real certification service, translating Unsupported to a failed gate" \
  bash -c "grep -q 'CertificationStatus.Unsupported' api/Biolog.Api/Modules/BioDev003Module.cs"
check "AC03 an unsupported combination maps to a 409 with certification_status" \
  bash -c "grep -q 'certification_status' api/Biolog.Api/Modules/BioDev003Module.cs && grep -q 'Status409Conflict' api/Biolog.Api/Modules/BioDev003Module.cs"

# --- AC04: no vendor SDK type leaks into domain contracts ---
check "AC04 vendor SDK simulator types exist, isolated in their own namespace" test -d "$FEATURE/VendorSdks"
check "AC04 every vendor SDK type is declared internal, not public" \
  bash -c "! grep -rE '^public (sealed )?(class|record)' '$FEATURE/VendorSdks'"
check "AC04 a reflection test enforces the isolation boundary at the assembly level" \
  test -f tests/features/bio-dev-003/VendorSdkIsolationTests.cs
check "AC04 the domain capability/health types reference no VendorSdks type" \
  bash -c "! grep -q 'VendorSdks' '$FEATURE/Domain/DeviceCapabilities.cs' '$FEATURE/Domain/DeviceHealth.cs'"

# --- AC05: the approved /devices surface is connected or explicitly documented HYBRID ---
check "AC05 the HYBRID decision and its reasoning are documented" \
  bash -c "grep -qi 'HYBRID' docs/features/bio-dev-003/README.md"
# Pinned to BIO-DEV-003's own merge range (base 4c2c421 -> tip 04d6dd9), not `develop...`: a live
# diff here would wrongly fail any later domain story (e.g. BIO-DEV-004) that legitimately extends
# web/features/devices the same way BIO-RPT-002 extended reporting's ports - this is a historical
# assertion about BIO-DEV-003's own commits, not a permanent ban on the devices port surface
# (same class of bug as BIO-INT-001's identical check, found 2026-10-06).
check "BIO-DEV-003's own branch did not modify BIO-UX-010's owned devices UI (per feature-path-ownership.json)" \
  bash -c "! git diff --name-only 4c2c421d8cf2f7b341534c91979d5d499f5dce1d..04d6dd9e20f109d38481012960dba8496a912e13 -- web/features/devices web/app/devices 2>/dev/null | grep -q ."

# --- Traceability ---
check "builder evidence directory exists" test -d evidence/BIO-DEV-003/builder
check "feature docs exist" test -f docs/features/bio-dev-003/README.md

echo "----"
echo "PASS=$pass FAIL=$fail"
[ "$fail" -eq 0 ]
