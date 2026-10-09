# BIO-VIS-002 independent re-review — round 10

Verdict: **FAIL**

Reviewed test-only remediation `7904d39` and builder evidence `9b0a9cc` against F09. Production remains unchanged from the already-correct `c264927` atomic `GetOrAdd` implementation.

## Blocking finding — F09 remains unresolved

`BarrierGatedVisitorCredentialAuditSink` calls `Barrier.SignalAndWait()` immediately before delegating to the real sink. This guarantees that both tasks reach the wrapper before either enters the inner `CreateIfAbsentAsync`, but it does not control execution after the barrier releases.

A valid scheduler interleaving remains:

1. Both callers reach `SignalAndWait`; the barrier releases them.
2. Caller A is scheduled and executes the entire inner create operation.
3. Only afterward does caller B execute the inner create operation.

If the inner implementation regressed to the former `ExistsAsync` then overwriting `WriteAsync` sequence, this serialized post-barrier interleaving would still pass every new assertion: A writes, B observes the existing event, both return the same object, one record remains, and the losing correlation is absent. The tests therefore still do not fail deterministically against the old bug.

`Assert.Same` and the losing-correlation assertion strengthen the final-state checks, but cannot compensate for the missing barrier between the old algorithm's observation and write phases.

## Required proof

Add a deterministic regression/mutation test for the old algorithm with coordination inside the contested sequence:

- both callers must complete the absence observation before either write is permitted;
- then release both writes and prove the old implementation violates immutability;
- run the same contract against the production atomic implementation and prove one winning payload is returned and retained.

An alternative is an instrumented test sink/operation hook whose barrier sits after both competing calls have entered the critical create operation, not solely before delegation.

## Other results

- Production F08 implementation: PASS by source inspection.
- Structural harness: **85 passed, 0 failed**.
- Builder evidence reports BIO-VIS-002 unit **45/45**, BIO-VIS-001 regression **25/25**, and integration **192/192**.
