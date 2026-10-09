#!/usr/bin/env bash
set -u

failures=0
pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1"; failures=$((failures + 1)); }

session='mobile/lib/core/session/session_controller.dart'
verification='mobile/lib/core/verification/verification_service.dart'
cache='mobile/lib/core/access/access_cache_service.dart'
cache_sync='mobile/lib/core/access/access_cache_sync_client.dart'
database='mobile/lib/core/db/sqlcipher_app_database.dart'

if rg -q 'ConnectivityState.online' "$verification" && rg -q '_accessCacheService.checkPresentation' "$verification"; then
  pass 'AC01/AC02 network loss selects the cached verification path'
else
  fail 'AC01/AC02 offline verification path is absent'
fi

if rg -q 'reasonDeniedRevokedCached' "$cache" && rg -q 'recordPresentationEvent' "$cache"; then
  pass 'AC03 known cached revocations deny and create a local event'
else
  fail 'AC03 cached revocations are not denied and logged'
fi

if rg -q 'CREATE TABLE presentation_events' "$database" \
  && rg -q 'synced INTEGER NOT NULL' "$database" \
  && rg -q "conflictAlgorithm: ConflictAlgorithm.abort" "$database"; then
  pass 'AC04 presentation events use the encrypted durable database queue'
else
  fail 'AC04 presentation events are not durably queued'
fi

if rg -q "_idempotencyHeader: event.eventId" "$cache_sync" \
  && rg -q 'conflictedMutations' mobile/lib/core/sync/sync_engine.dart; then
  pass 'AC05 dependency clients provide idempotent event upload and surfaced mutation conflicts'
else
  fail 'AC05 dependency clients lack idempotency or conflict visibility'
fi

# End-to-end acceptance requires the shipped bootstrap to invoke the access-cache client on
# reconnect. Component-only tests that instantiate it manually do not deliver this behavior.
if rg -q 'AccessCacheSyncClient' "$session" && rg -q 'pullRevocationDelta' "$session"; then
  pass 'AC03 reconnect refreshes the live revocation cache'
else
  fail 'AC03 shipped app never pulls revocations into the live cache'
fi

if rg -q 'AccessCacheSyncClient' "$session" && rg -q 'pushPendingPresentationEvents' "$session"; then
  pass 'AC04/AC05 reconnect drains the durable presentation-event queue'
else
  fail 'AC04/AC05 shipped reconnect never uploads queued presentation events'
fi

if (( failures > 0 )); then
  printf '%d required integrated invariant(s) failed\n' "$failures"
  exit 1
fi

printf 'PASS: BIO-OPS-004 integrated acceptance invariants satisfied\n'
