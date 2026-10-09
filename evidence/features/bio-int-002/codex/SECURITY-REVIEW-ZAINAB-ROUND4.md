# BIO-INT-002 security review — Zainab — round 4

**Verdict: FAIL**

Reviewed remediation `f910ece` and evidence `90e4055`. The structural harness passes 67/67.
The widened claim-release scope, mapping-bound provenance key, duplicate external-key rejection,
and cross-mapping regression are correct. Provenance recovery still fails at the newly tested
genuine-write-failure boundary.

## ZAINAB-INT-002-F05 — verified provenance failure still loses Created outcome on retry

`RecordRowProvenanceSafelyAsync` runs only after PERSON and external link creation. If its write
genuinely fails, it rethrows and releases the idempotency claim as intended, but there is no durable
row outcome. On retry, `ClassifyRowAsync` sees the already-created link and returns `Unchanged`;
because it never enters the `Created` branch, it does not consult the orphan ledger that could show
this PERSON was created by the failed attempt. The final batch and audit therefore still lose the
original `Created` provenance.

The new failure test asserts only claim release and `CreateCallCount == 1`. It does not assert the
retry's row outcome, so it passes while the stated invariant remains broken.

Required fix: record a recoverable Created intent/outcome before or atomically with link creation,
or make retry reconstruction consult a mapping/attempt-bound durable marker after a provenance
write failure. The injected-failure test must assert that the successful retry returns exactly one
`Created` outcome for the original PERSON and that its audit `created_count` is one, not zero.
