#!/usr/bin/env bash
# Offline-safe structural checks for BIO-CRD-001 (versioned tenant brand asset library).
# Behavioural assertions (upload, DPI, SVG/malware validation, tenant isolation, idempotency)
# live in the xUnit suites under tests/dotnet/ (see docs/BRAND-ASSETS.md, scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/Biolog.Api/Features/BrandAssets

# --- AC01: PNG/JPEG/SVG uploads are supported ---
check "AC01 module registered in the host" bash -c 'grep -q "new BrandAssetsModule()" api/Biolog.Api/Program.cs'
check "AC01 upload endpoint mapped" bash -c "grep -q 'MapPost(\"/\", BrandAssetEndpoints.UploadNewAsync)' $FEATURE/BrandAssetsModule.cs"
check "AC01 format detection covers PNG/JPEG/SVG only" test -f "$FEATURE/BrandAssetFormatDetector.cs"

# --- AC02: assets record tenant, organisation/campus usage, version and status ---
check "AC02 domain model carries tenant/org/campus/version/status" bash -c \
  "grep -q 'TenantId' $FEATURE/BrandAssetModels.cs && grep -q 'OrganisationId' $FEATURE/BrandAssetModels.cs && grep -q 'CampusId' $FEATURE/BrandAssetModels.cs && grep -q 'Version' $FEATURE/BrandAssetModels.cs && grep -q 'BrandAssetStatus Status' $FEATURE/BrandAssetModels.cs"
check "AC02 new-version endpoint mapped" bash -c "grep -q 'versions' $FEATURE/BrandAssetsModule.cs"
check "AC02 status-transition endpoint mapped" bash -c "grep -q 'UpdateStatusAsync' $FEATURE/BrandAssetsModule.cs"

# --- AC03: raster quality/effective DPI warnings are calculated ---
check "AC03 raster inspector exists" test -f "$FEATURE/RasterImageInspector.cs"
check "AC03 low-resolution DPI threshold defined" bash -c "grep -q 'LowResolutionDpiThreshold' $FEATURE/RasterImageInspector.cs"
check "AC03 SVG never carries a raster quality assessment" bash -c "grep -q 'RasterQuality = processed.RasterQuality' $FEATURE/BrandAssetEndpoints.cs"

# --- AC04: SVG/content validation and malware scanning hooks exist ---
check "AC04 SVG validator rejects active content" bash -c \
  "grep -q 'script' $FEATURE/SvgValidator.cs && grep -q 'foreignObject' $FEATURE/SvgValidator.cs && grep -q 'javascript:' $FEATURE/SvgValidator.cs && grep -q 'DtdProcessing.Prohibit' $FEATURE/SvgValidator.cs"
check "AC04 malware scanning hook interface exists" bash -c "grep -q 'interface IMalwareScanner' $FEATURE/MalwareScanner.cs"
check "AC04 malware scan runs before anything is persisted" bash -c "grep -q 'scanner.Scan' $FEATURE/BrandAssetEndpoints.cs"

# --- AC05: cross-tenant asset access is denied ---
check "AC05 repository scopes every read/write by tenant" bash -c \
  "grep -q 'asset.TenantId == tenantId' $FEATURE/BrandAssetRepository.cs"
check "AC05 tenant identity requires a verified per-tenant secret, not a bare header (QA-01)" \
  bash -c "grep -q 'ITenantCredentialStore' $FEATURE/TenantAuthenticator.cs && grep -q 'credentialStore.Validate' $FEATURE/TenantAuthenticator.cs"
check "AC05 tenant authentication is fail-closed (no default tenant)" bash -c "grep -q 'TenantAuthenticationFailedException' $FEATURE/TenantAuthenticator.cs $FEATURE/ProblemResponse.cs"
check "AC05 route handlers never touch the credential store directly (no self-registration endpoint)" \
  bash -c "! grep -q 'ITenantCredentialStore' $FEATURE/BrandAssetEndpoints.cs"
check "AC05 cross-tenant lookup is 404, not 403 (no enumeration oracle)" bash -c "grep -q 'BrandAssetNotFoundException' $FEATURE/BrandAssetEndpoints.cs"
check "AC05/QA-02 a real startup provisioning path loads tenant credentials from configuration" \
  bash -c "grep -q 'LoadFromConfiguration' $FEATURE/TenantCredentialStore.cs $FEATURE/BrandAssetsModule.cs"
check "AC05/QA-02 no tenant secret is committed to checked-in app configuration" \
  bash -c "! grep -rq 'TenantCredentials' api/Biolog.Api/appsettings.json api/Biolog.Api/appsettings.Development.json 2>/dev/null"
check "TD-CRD001-TENANT-ID-REGEX tenant-id pattern accepts the real ten_<UUIDv7> canonical shape" \
  bash -c "grep -q 'ten_\[0-9a-f\]{8}-\[0-9a-f\]{4}-7\[0-9a-f\]{3}-\[89ab\]\[0-9a-f\]{3}-\[0-9a-f\]{12}' $FEATURE/TenantAuthenticator.cs"
check "TD-CRD001-TENANT-ID-REGEX regression test uses a genuine canonical tenant id" \
  bash -c "grep -q 'Authenticates_a_real_canonical_ten_prefixed_uuid_v7_tenant_id' tests/dotnet/Biolog.UnitTests/Features/BrandAssets/HeaderTenantAuthenticatorTests.cs &&
           grep -q 'TD_CRD001_a_real_canonical_ten_prefixed_uuid_v7_tenant_id_can_authenticate_end_to_end' tests/dotnet/Biolog.IntegrationTests/Features/BrandAssets/BrandAssetsEndpointTests.cs"

# --- Developer test wiring ---
check "unit tests exist for raster/SVG/malware/repository/idempotency/tenant-auth" bash -c \
  'test -f tests/dotnet/Biolog.UnitTests/Features/BrandAssets/RasterImageInspectorTests.cs &&
   test -f tests/dotnet/Biolog.UnitTests/Features/BrandAssets/SvgValidatorTests.cs &&
   test -f tests/dotnet/Biolog.UnitTests/Features/BrandAssets/HeuristicMalwareScannerTests.cs &&
   test -f tests/dotnet/Biolog.UnitTests/Features/BrandAssets/BrandAssetRepositoryTests.cs &&
   test -f tests/dotnet/Biolog.UnitTests/Features/BrandAssets/IdempotencyStoreTests.cs &&
   test -f tests/dotnet/Biolog.UnitTests/Features/BrandAssets/HeaderTenantAuthenticatorTests.cs &&
   test -f tests/dotnet/Biolog.UnitTests/Features/BrandAssets/TenantCredentialProvisioningTests.cs'
check "unit tests cover the QA-01 tenant-forgery attack directly" \
  bash -c "grep -q 'QA01' tests/dotnet/Biolog.UnitTests/Features/BrandAssets/HeaderTenantAuthenticatorTests.cs"
check "integration tests exercise the real HTTP endpoints" test -f tests/dotnet/Biolog.IntegrationTests/Features/BrandAssets/BrandAssetsEndpointTests.cs
check "QA-02 composed-host test uses the real production provisioning path, not test-only DI seeding" \
  bash -c "grep -q 'QA02_a_tenant_credential_provisioned_through_real_application_configuration' tests/dotnet/Biolog.IntegrationTests/Features/BrandAssets/BrandAssetsEndpointTests.cs"
check "all fixtures marked SYNTHETIC" bash -c \
  'grep -rq SYNTHETIC tests/dotnet/Biolog.UnitTests/Features/BrandAssets tests/dotnet/Biolog.IntegrationTests/Features/BrandAssets'
check "builder evidence recorded" test -d evidence/BIO-CRD-001/builder
check "governance story spec recorded (BIO-FND-010 traceability)" test -f governance/stories/BIO-CRD-001.json

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
