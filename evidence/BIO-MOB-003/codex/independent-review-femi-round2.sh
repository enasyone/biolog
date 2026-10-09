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
check "production transport no longer throws UnimplementedError" \
  bash -c "! sed -n '/class HttpSyncTransport/,/^}/p' mobile/lib/core/sync/sync_transport.dart | grep -q 'throw UnimplementedError'"
check "sync engine is wired into SessionController" \
  grep -q '_syncEngine.attachTo(_connectivity)' mobile/lib/core/session/session_controller.dart

start_line="$(grep -n 'await _connectivity.start()' mobile/lib/core/session/session_controller.dart | cut -d: -f1)"
attach_line="$(grep -n '_syncEngine.attachTo(_connectivity)' mobile/lib/core/session/session_controller.dart | cut -d: -f1)"
if [ -n "$start_line" ] && [ -n "$attach_line" ] && [ "$attach_line" -lt "$start_line" ]; then
  printf 'PASS AC01 startup subscribes before the initial connectivity emission\n'
elif grep -q '_syncEngine.runSyncPass' mobile/lib/core/session/session_controller.dart; then
  printf 'PASS AC01 startup explicitly drains when initially online\n'
else
  printf 'FAIL AC01 startup while already online triggers a sync pass\n'
  fail=1
fi

if sed -n '/Future<SyncAck> upload/,/^  }/p' mobile/lib/core/sync/sync_transport.dart \
    | grep -q '\.timeout('; then
  printf 'PASS AC05 HTTP upload has a bounded deadline\n'
else
  printf 'FAIL AC05 HTTP upload has a bounded deadline\n'
  fail=1
fi

exit "$fail"
