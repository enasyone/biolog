# BIO-MOB-003 independent performance/resilience re-review — Femi round 3

Reviewed commit: `2eff731830b2ae25e7ea9d1c65ba1789fa656682`  
Repair implementation: `4db7b3b`

## Verdict: PASS

Both round-two findings are closed.

- `BIO-MOB-003-FEMI-02`: `SessionController.bootstrap()` now attaches the state listener and `SyncEngine` before `ConnectivitySignal.start()`. The initial online reading reaches the engine, so a queue present at launch drains without requiring a later connectivity transition.
- `BIO-MOB-003-FEMI-03`: `HttpSyncTransport.upload()` now applies a configurable request deadline, defaulting to 30 seconds. `TimeoutException` follows the existing upload-failure path, bounded retry accounting runs, and `SyncEngine.runSyncPass()` releases `_inFlight` in `finally`.

## Reproduction

The following all passed at the reviewed commit:

```sh
bash evidence/BIO-MOB-003/codex/independent-review-femi.sh
bash evidence/BIO-MOB-003/codex/independent-review-femi-round2.sh
bash tests/foundation/BIO-MOB-003.test.sh
```

Results: original Femi harness PASS; round-two Femi harness PASS; foundation harness 19 passed, 0 failed. Builder evidence records Flutter 104 tests passed and static analysis with no issues. Direct Flutter execution remains unavailable in this sandbox because the shared SDK cache is not writable; the committed test transcript covers the new initial-online and real timeout regressions.

## Acceptance criteria

| Criterion | Verdict | Evidence |
|---|---|---|
| AC01 — delta sync additions/updates/revocations | PASS | Real HTTP transport, live bootstrap wiring, initial-online delivery, operation-preserving deltas, and revocation priority are present. |
| AC02 — server acknowledgements idempotent | PASS | Fresh/replayed acknowledgements share the guarded acknowledgement transition and the idempotency key is transmitted. |
| AC03 — critical conflicts enter queue | PASS | Only version conflicts map to conflict acknowledgements; engine routes them unconditionally to the resolution queue. |
| AC04 — no last-write-wins for sensitive data | PASS | No automatic merge path exists and conflict payload is preserved. |
| AC05 — reconnect storms create no duplicates | PASS | Single-flight plus atomic claim guards prevent duplicate upload ownership; initial reconnect is subscribed before emission; bounded request deadlines allow recovery from a hung response. |

## Category verdicts

- Functional: PASS.
- Security: PASS.
- Tenant isolation: PASS.
- Offline: PASS.
- Concurrency: PASS.
- Regression: PASS.

No implementation changes were made during this review.
