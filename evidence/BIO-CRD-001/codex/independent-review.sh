#!/usr/bin/env bash
set -u
cd "$(git rev-parse --show-toplevel)"

if grep -q 'AddSingleton<ITenantClaimReader, HeaderTenantClaimReader>' api/Biolog.Api/Features/BrandAssets/BrandAssetsModule.cs \
  && grep -q 'request.Headers.TryGetValue' api/Biolog.Api/Features/BrandAssets/TenantClaim.cs; then
  echo "FAIL caller-controlled tenant header is the active tenant authority" >&2
  exit 1
fi

echo "PASS tenant authority is not caller-controlled"
