#!/usr/bin/env bash
set -u

repo="$(git rev-parse --show-toplevel)"
cd "$repo"
fail=0

check() {
  local name="$1"
  shift
  if "$@"; then
    printf 'PASS %s\n' "$name"
  else
    printf 'FAIL %s\n' "$name"
    fail=1
  fi
}

check "component harness passes" bash tests/foundation/BIO-MOB-003.test.sh

if sed -n '/class HttpSyncTransport/,/^}/p' mobile/lib/core/sync/sync_transport.dart \
    | grep -q 'throw UnimplementedError'; then
  printf 'FAIL AC01 production HttpSyncTransport is implemented\n'
  fail=1
else
  printf 'PASS AC01 production HttpSyncTransport is implemented\n'
fi

if rg -l 'SyncEngine|HttpSyncTransport|attachTo\(' mobile/lib \
    --glob '!mobile/lib/core/sync/**' \
    --glob '!mobile/lib/core/db/app_database.dart' \
    --glob '!mobile/lib/core/connectivity/connectivity_signal.dart' \
    | grep -q .; then
  printf 'PASS AC01/AC05 sync engine is wired into the live mobile app\n'
else
  printf 'FAIL AC01/AC05 sync engine is wired into the live mobile app\n'
  fail=1
fi

exit "$fail"
