# TD-CRD-005-IDEMPOTENCY-RACE review

Verdict: **FAIL (test evidence); production fix passes inspection**

Commit `e681ac1` correctly adds claim ownership, bounded wait/replay, fingerprint mismatch rejection, release-on-failure, and a 503 timeout. Structural harness: **37/37**.

The new same-key test is not deterministic. Its `TaskCompletionSource` only releases two tasks; one `ActivateAsync` may complete before the other calls `Claim`. The historical broken implementation would pass under that schedule. Add a gate inside the first execution after it owns the in-flight claim, wait until the second caller has observed that in-flight claim, then release completion. Assert one execution and one stable result.

An additional timeout test here is not required: this path is a mechanical copy of the independently tested BIO-INT-002 loop and its error/status mapping is correct by inspection.
