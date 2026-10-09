#!/usr/bin/env bash
# Offline-safe structural checks for BIO-BIO-001 (biometric service/provider abstraction).
# Behavioural assertions live in the executable xunit suite under tests/features/bio-bio-001/
# (see docs/features/bio-bio-001/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-bio-001/Biolog.Features.Biometrics

# --- AC01: Fingerprint and face provider interfaces are defined ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.Biometrics.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.Biometrics.csproj'"
check "AC01 a fingerprint provider interface exists" test -f "$FEATURE/Providers/IFingerprintProvider.cs"
check "AC01 a face provider interface exists" test -f "$FEATURE/Providers/IFaceProvider.cs"
check "AC01 fingerprint and face are two distinct interfaces, not one generic interface" \
  bash -c "grep -q 'interface IFingerprintProvider' '$FEATURE/Providers/IFingerprintProvider.cs' && grep -q 'interface IFaceProvider' '$FEATURE/Providers/IFaceProvider.cs' && ! grep -qi 'interface IBiometricProvider<' '$FEATURE/Providers/IBiometricProvider.cs'"
check "AC01 a structural test proves the two interfaces are distinct types adversarially" \
  bash -c "grep -q 'AC01_fingerprint_and_face_provider_interfaces_are_distinct_types' tests/features/bio-bio-001/ProviderContractTests.cs"
check "AC01 a synthetic implementation exists for each provider (AC05 local-dev requirement)" \
  bash -c "test -f '$FEATURE/Synthetic/SyntheticFingerprintProvider.cs' && test -f '$FEATURE/Synthetic/SyntheticFaceProvider.cs' && grep -q 'IFingerprintProvider' '$FEATURE/Synthetic/SyntheticFingerprintProvider.cs' && grep -q 'IFaceProvider' '$FEATURE/Synthetic/SyntheticFaceProvider.cs'"

# --- AC02: provider-specific templates/identifiers are isolated ---
check "AC02 the canonical template type exists" test -f "$FEATURE/Domain/BiometricTemplate.cs"
check "AC02 the provider extraction output type carries no identifier field of any kind" \
  bash -c "test -f '$FEATURE/Providers/ExtractedBiometricFeatures.cs' && ! grep -v '^\s*///' '$FEATURE/Providers/ExtractedBiometricFeatures.cs' | grep -qiE 'string\??\s*(Id|Identifier|VendorId|TemplateId|SessionHandle)\b'"
check "AC02 the canonical template carries no vendor/provider-internal identifier field" \
  bash -c "! grep -v '^\s*///' '$FEATURE/Domain/BiometricTemplate.cs' | grep -qiE 'VendorId|ProviderTemplateId|ExternalId|SessionHandle'"
check "AC02 providers are context-free: extraction takes no tenant or person id" \
  bash -c "! grep -E 'ExtractTemplateAsync' '$FEATURE/Providers/IFingerprintProvider.cs' '$FEATURE/Providers/IFaceProvider.cs' | grep -qiE 'tenantId|personId'"
check "AC02 a structural test proves the omission adversarially, not just by inspection" \
  bash -c "grep -q 'AC02_BiometricTemplate_carries_no_vendor_or_provider_internal_identifier_field' tests/features/bio-bio-001/BiometricTemplateTests.cs"
check "AC02 template construction is restricted to the validated factory (no public constructor)" \
  bash -c "grep -q 'private BiometricTemplate(' '$FEATURE/Domain/BiometricTemplate.cs' && grep -q 'public static BiometricTemplate Create(' '$FEATURE/Domain/BiometricTemplate.cs'"
check "QA-01 (Seyi round 1) BiometricTemplate.Payload clones on write and on read, no aliasing" \
  bash -c "grep -q '_payload.Clone()' '$FEATURE/Domain/BiometricTemplate.cs' && grep -q 'payload.Clone()' '$FEATURE/Domain/BiometricTemplate.cs'"
check "QA-01 ExtractedBiometricFeatures.Payload clones on write and on read" \
  bash -c "grep -q '_payload.Clone()' '$FEATURE/Providers/ExtractedBiometricFeatures.cs' && grep -q 'value.Clone()' '$FEATURE/Providers/ExtractedBiometricFeatures.cs'"
check "QA-01 BiometricSample.Payload clones on write and on read" \
  bash -c "grep -q '_payload.Clone()' '$FEATURE/Providers/BiometricSample.cs' && grep -q 'value.Clone()' '$FEATURE/Providers/BiometricSample.cs'"
check "QA-01 a test reproduces the reviewer's exact enrol-then-mutate-then-read-back scenario" \
  bash -c "grep -q 'QA01_mutating_an_enrolled_templates_Payload_after_the_fact_does_not_alter_the_stored_candidate' tests/features/bio-bio-001/BiometricServiceTests.cs"
check "F02 (Zainab round 3) the in-memory store key is a structured tuple, not a delimiter-joined string" \
  bash -c "grep -q 'ConcurrentDictionary<(string TenantId, string PersonId, BiometricModality Modality)' '$FEATURE/Synthetic/InMemoryBiometricTemplateStore.cs' && ! grep -q '\\\\u0000' '$FEATURE/Synthetic/InMemoryBiometricTemplateStore.cs'"
check "F02 a test reproduces the reviewer's exact colliding-tuple scenario" \
  bash -c "grep -q 'F02_embedded_delimiter_characters_in_tenant_or_person_id_never_collide_across_distinct_tuples' tests/features/bio-bio-001/InMemoryBiometricTemplateStoreTests.cs"

# --- AC03: biometric service is separate from MySQL matching logic ---
check "AC03 the orchestration service exists" test -f "$FEATURE/Service/BiometricService.cs"
check "AC03 a template-store contract exists, separate from the service" test -f "$FEATURE/Matching/IBiometricTemplateStore.cs"
check "AC03 a matcher contract exists, separate from the service" test -f "$FEATURE/Matching/IBiometricMatcher.cs"
check "AC03 the feature project has zero NuGet package references (no SQL/MySQL client of any kind)" \
  bash -c "! grep -q '<PackageReference' '$FEATURE/Biolog.Features.Biometrics.csproj'"
check "AC03 no SQL/MySQL identifier appears anywhere in the service or matching contracts" \
  bash -c "! grep -v '^\s*///\|^\s*//' '$FEATURE/Service/BiometricService.cs' '$FEATURE/Matching/IBiometricTemplateStore.cs' '$FEATURE/Matching/IBiometricMatcher.cs' | grep -qiE 'MySql|SqlConnection|DbConnection|SELECT |INSERT INTO'"
check "AC03 a structural test proves the service's dependencies are interfaces only, not a database type" \
  bash -c "grep -q 'AC03_BiometricService_has_no_dependency_of_any_kind_on_a_SQL_or_MySQL_client' tests/features/bio-bio-001/BiometricServiceTests.cs"
check "AC03 module project references stay one-way (no ProjectReference back to Biolog.Api)" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\.Api\.csproj' '$FEATURE/Biolog.Features.Biometrics.csproj'"

# --- AC04: provider health and model/version metadata are supported ---
check "AC04 a provider health type exists with status and model version" \
  bash -c "test -f '$FEATURE/Domain/BiometricProviderHealth.cs' && grep -q 'BiometricProviderStatus Status' '$FEATURE/Domain/BiometricProviderHealth.cs' && grep -q 'string ModelVersion' '$FEATURE/Domain/BiometricProviderHealth.cs'"
check "AC04 every provider interface exposes a health check" \
  bash -c "grep -q 'GetHealthAsync' '$FEATURE/Providers/IBiometricProvider.cs'"
check "AC04 the service aggregates health across providers" \
  bash -c "grep -q 'GetProviderHealthAsync' '$FEATURE/Service/BiometricService.cs'"
check "AC04 a template records the provider version that produced it" \
  bash -c "grep -q 'string ProviderVersion' '$FEATURE/Domain/BiometricTemplate.cs'"

# --- AC05: no real biometric data is required for local development ---
check "AC05 a synthetic fingerprint provider exists" test -f "$FEATURE/Synthetic/SyntheticFingerprintProvider.cs"
check "AC05 a synthetic face provider exists" test -f "$FEATURE/Synthetic/SyntheticFaceProvider.cs"
check "AC05 a synthetic (in-memory) template store exists" test -f "$FEATURE/Synthetic/InMemoryBiometricTemplateStore.cs"
check "AC05 a synthetic matcher exists" test -f "$FEATURE/Synthetic/SyntheticBiometricMatcher.cs"
check "AC05 the module wires only the synthetic implementations, no vendor SDK" \
  bash -c "grep -q 'SyntheticFingerprintProvider' api/Biolog.Api/Modules/BioBio001Module.cs && grep -q 'SyntheticFaceProvider' api/Biolog.Api/Modules/BioBio001Module.cs && grep -q 'InMemoryBiometricTemplateStore' api/Biolog.Api/Modules/BioBio001Module.cs && grep -q 'SyntheticBiometricMatcher' api/Biolog.Api/Modules/BioBio001Module.cs"
check "AC05 module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioBio001Module.cs && ! test -f '$FEATURE/BioBio001Module.cs'"
check "AC05 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioBio001Module()' api/Biolog.Api/Program.cs"

# --- Developer test wiring ---
check "unit tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.Biometrics.csproj' tests/features/bio-bio-001/Biolog.Features.Biometrics.Tests.csproj"
check "docs/features/bio-bio-001/README.md documents the feature" test -f docs/features/bio-bio-001/README.md
check "builder evidence recorded" test -d evidence/features/bio-bio-001/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
