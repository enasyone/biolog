# BIO-MOB-003 independent performance/resilience re-review — Femi round 2

Reviewed commit: `b712d2ae9604d3f7d336464c66bca6ef37d9be8c`  
Repair implementation: `c3d2816`

## Verdict: FAIL

The repair closes `BIO-MOB-003-FEMI-01`: `HttpSyncTransport` now performs an HTTP POST and `SessionController` constructs and attaches the sync engine. Two new live-path resilience defects remain.

## Findings

### BIO-MOB-003-FEMI-02 — initial-online startup misses the only sync trigger

Severity: High  
Acceptance criterion: AC01  
Categories: Functional, Offline, Regression

`SessionController.bootstrap()` calls `await _connectivity.start()` before either live listener is attached. `PluginConnectivitySignal.start()` sets `current` and emits it immediately on a broadcast stream. With no subscriber yet, that initial event is dropped. `SyncEngine.attachTo()` only reacts to future `onChange` events and does not inspect `signal.current`; bootstrap also never calls `runSyncPass()` when the current state is already online.

Result: launching the app while online with pending mutations leaves the queue untouched until connectivity later goes offline and online again. Delta synchronization is therefore not reliably available in the normal initial-online case.

The added session-wiring test starts offline and then explicitly transitions online, so it does not cover this failure. The default-construction test has no queued mutation.

### BIO-MOB-003-FEMI-03 — unbounded HTTP request can permanently wedge synchronization

Severity: High  
Acceptance criterion: AC05  
Categories: Offline, Concurrency, Failure/Recovery

`HttpSyncTransport.upload()` awaits `_client.post(...)` with no explicit deadline. `SyncEngine.runSyncPass()` holds `_inFlight = true` until that future completes. A server that accepts a connection but never returns a response can therefore block the current mutation, every later mutation in the pass, and every reconnect-triggered pass indefinitely; reconnect storms are reduced to no-ops while the hung pass owns the single-flight guard.

The code comments claim timeouts are treated as retryable failures, but no timeout is established by this implementation or its tests.

## Reproduction

Run from the repository root:

```sh
bash evidence/BIO-MOB-003/codex/independent-review-femi-round2.sh
```

Expected current result: foundation/component checks and the original repair checks pass; initial-online drain and bounded HTTP deadline fail; exit status `1`.

Direct Flutter execution remains unavailable in this sandbox because the shared Flutter SDK cache and lockfile under `/opt/homebrew/share/flutter/bin/cache` are not writable. The defects above are established by the production control flow, and the committed builder/Seyi passing tests do not cover either case.

## Acceptance criteria

| Criterion | Verdict | Evidence |
|---|---|---|
| AC01 — delta sync additions/updates/revocations | FAIL | Real transport exists, but normal initial-online startup misses synchronization entirely. |
| AC02 — server acknowledgements idempotent | PASS | Fresh and replayed acknowledgements use the same guarded transition. |
| AC03 — critical conflicts enter queue | PASS | Version conflicts route unconditionally to `recordConflict`. |
| AC04 — no last-write-wins for sensitive data | PASS | No automatic merge path exists. |
| AC05 — reconnect storms create no duplicates | FAIL | Duplicate guards exist, but one unbounded upload can wedge all current and future reconnect passes indefinitely. |

## Category verdicts

- Functional: FAIL.
- Security: PASS for reviewed conflict/idempotency behavior.
- Tenant isolation: PASS for the reviewed mutation envelope and local queue path.
- Offline: FAIL.
- Concurrency: FAIL because an unbounded request can monopolize the single-flight guard.
- Regression: FAIL.

No implementation repair was made.
