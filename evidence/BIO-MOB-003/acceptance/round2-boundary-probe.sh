#!/usr/bin/env bash
set -u

repo="$(git rev-parse --show-toplevel)"
cd "$repo"
fail=0

if grep -q 'X-Biolog-Device-Id' mobile/lib/core/sync/sync_transport.dart \
  && grep -q 'X-Biolog-Device-Secret' mobile/lib/core/sync/sync_transport.dart; then
  echo 'PASS production client sends required device credentials'
else
  echo 'FAIL production client sends required device credentials'
  fail=1
fi

if rg -n 'writeSecret\(' mobile/lib | grep -v 'device_credential_store.dart' >/dev/null; then
  echo 'PASS production app provisions the device secret'
else
  echo 'FAIL production app provisions the device secret'
  fail=1
fi

if rg -n 'request\.DeviceId\s*!=\s*device\.DeviceId|device\.DeviceId\s*!=\s*request\.DeviceId' \
  api/features/bio-mob-003/OfflineMutationIntakeService.cs >/dev/null; then
  echo 'PASS authenticated device is cross-validated against payload device_id'
else
  echo 'FAIL authenticated device is cross-validated against payload device_id'
  fail=1
fi

if sed -n '/ComputeFingerprint/,/^    }/p' api/features/bio-mob-003/OfflineMutationIntakeService.cs \
  | grep -q 'request.DeviceId'; then
  echo 'PASS device_id is bound into the idempotency fingerprint'
else
  echo 'FAIL device_id is bound into the idempotency fingerprint'
  fail=1
fi

exit "$fail"
