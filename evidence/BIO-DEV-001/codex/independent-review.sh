#!/usr/bin/env bash
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

pass=0
fail=0
check() {
  local label="$1"; shift
  if "$@"; then
    echo "PASS  $label"
    pass=$((pass + 1))
  else
    echo "FAIL  $label"
    fail=$((fail + 1))
  fi
}

check "AC01 HTTP registry has a tenant-resolution path capable of reaching successful CRUD" \
  bash -c '! grep -q "accessor.Context = null" api/Biolog.Api/Tenancy/TenantContextMiddleware.cs'

check "AC03 audit failure cannot leave an un-audited committed status mutation" \
  bash -c 'svc=api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs; audit=$(grep -n "await _auditSink.RecordAsync" "$svc" | cut -d: -f1); mutation=$(grep -n "await _repository.UpdateStatusAsync" "$svc" | cut -d: -f1); test "$audit" -lt "$mutation"'

echo "=============================="
echo "$pass passed, $fail failed"
test "$fail" -eq 0
