#!/usr/bin/env bash
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

failed=0
if git show 6edd7f5:api/Biolog.Api/Tenancy/TenantContextMiddleware.cs | grep -q 'accessor.Context = null'; then
  echo "FAIL frozen production middleware still makes every device endpoint tenant_context_missing"
  failed=1
fi
if git show 6edd7f5:api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs | grep -q 'catch (DeviceConcurrencyConflictException)'; then
  echo "FAIL frozen remediation swallows rollback conflict and can preserve unaudited state"
  failed=1
fi
test "$failed" -eq 0
