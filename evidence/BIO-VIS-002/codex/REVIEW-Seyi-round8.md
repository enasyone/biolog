# BIO-VIS-002 independent re-review — round 9

Verdict: **FAIL**

Reviewed implementation `c264927` and builder evidence `771e9da` against F08 and the requested deterministic concurrency proof.

## Implementation verdict

- Production F08 fix: PASS. `CreateIfAbsentAsync` uses the value overload of `ConcurrentDictionary.GetOrAdd`, so one immutable payload wins for a stable event identifier and every racing caller receives that stored value. `AuditAsync` routes only stable-id reconciliation events through this primitive; ordinary fresh-id audit behavior is unchanged.
- Sequential F07 recovery remains intact: a genuine audit failure keeps the ledger unresolved, and a retry can create or observe the stable audit event before resolving.
- Structural harness: **85 passed, 0 failed**.

## Blocking finding F09 — concurrency tests do not force the contested interleaving

Both new tests use a `TaskCompletionSource` as a start gate. The tasks wait for one signal and are then eligible to run, but there is no two-party readiness barrier inside or immediately before the contested create operation. The scheduler may execute caller A through `CreateIfAbsentAsync` before caller B starts it.

Consequently, the old defective check-then-write implementation could pass these tests whenever the two tasks happen to run sequentially: A observes absent and writes, then B observes present. The service-level test has the same issue and asserts only the final one-record count, which the old overwrite bug also produced. Neither test proves the payload was never replaced during the operation.

This fails the explicitly requested deterministic race-regression proof even though source inspection confirms the current `GetOrAdd` implementation is correct.

## Required remediation

Add a controlled two-party barrier at the contested boundary. A test sink or hook should pause both callers after they have entered the create path (or, for a regression model, after both have observed absence) and release them together. Assert:

- both calls return the same winning payload;
- the stored payload is the same object/value returned to both callers;
- a losing payload is never subsequently observable;
- the ledger resolves only after the atomic result is durable.

The test must fail deterministically if `CreateIfAbsentAsync` regresses to the prior `ExistsAsync` plus overwriting `WriteAsync` sequence.

## Execution note

Builder evidence reports BIO-VIS-002 unit **45/45**, BIO-VIS-001 regression **25/25**, and integration **192/192**. This finding is about test determinism, not the correctness of `ConcurrentDictionary.GetOrAdd` itself.
