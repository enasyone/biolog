#!/usr/bin/env bash
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

if grep -q 'TenantCredentialProvisioning.LoadFromConfiguration(configuration)' \
    api/Biolog.Api/Features/BrandAssets/BrandAssetsModule.cs && \
   grep -q 'GetSection(ConfigurationSectionPath).GetChildren()' \
    api/Biolog.Api/Features/BrandAssets/TenantCredentialStore.cs; then
  echo "PASS production host provisions at least one tenant credential"
  exit 0
fi

echo "FAIL production host provisions no tenant credential; every composed request authenticates as 401"
exit 1
