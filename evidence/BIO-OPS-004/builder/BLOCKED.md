# BIO-OPS-004 — BLOCKED

## Finding SY-OPS004-01 — High

The shipped reconnect path never synchronizes BIO-ACC-003 access-cache state.

`SessionController.bootstrap()` attaches only BIO-MOB-003's generic `SyncEngine`. A production
search finds no construction or invocation of `AccessCacheSyncClient` outside its own class:
neither `pullRevocationDelta` nor `pushPendingPresentationEvents` has a caller in `mobile/lib`.

Reproduction:

1. Launch the shipped app with its empty encrypted database.
2. Transition online; `SessionController` runs only generic offline-mutation synchronization.
3. Transition offline and verify a person revoked on the server. The local deny-list was never
   pulled, so the person is treated as not revoked and allowed with a stale flag.
4. The decision event is durably inserted into `presentation_events`.
5. Transition online again. The generic `SyncEngine` does not read that table, so the event stays
   pending indefinitely.

This blocks AC03 in the real workflow and AC04/AC05 reconciliation. The isolated dependency tests
pass only because they seed the cache and invoke the client directly.

## Independent checks

- AC01/AC02 offline path selection: PASS by source trace.
- AC03 cached deny and local logging component: PASS.
- AC04 encrypted database queue: PASS by schema/write/read trace.
- AC05 dependency idempotency/conflict primitives: PASS.
- Live revocation pull on connect/reconnect: FAIL.
- Live presentation-event drain on reconnect: FAIL.
- Independent acceptance harness: 4 PASS, 2 FAIL.

Required remediation: wire one connectivity-triggered access-cache synchronization coordinator
into the production session/bootstrap path. It must pull revocations and drain pending presentation
events for the trusted bound tenant, retain failures for retry, be single-flight under reconnect
storms, and expose generic mutation conflicts without silently resolving them. Add an end-to-end
session test starting offline, denying a cached revocation, surviving close/open, reconnecting twice,
and proving one server event plus visible conflicts.
