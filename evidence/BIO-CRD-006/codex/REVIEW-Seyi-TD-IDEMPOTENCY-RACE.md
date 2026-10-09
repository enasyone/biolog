# TD-CRD-006-IDEMPOTENCY-RACE review

Verdict: **FAIL (test evidence); production fix passes inspection**

Commit `1deb33e` correctly adds claim ownership and bounded wait/replay to both `ActivateReplacementAsync` and `IntakePresentationEventAsync`. Structural harness: **65/65**.

Both new tests use only a shared start gate. The scheduler may complete one call before the other claims, so the old broken implementation could pass. Each call site needs a deterministic in-flight interleaving: pause the owner after claim acquisition, prove the loser has observed the shared in-flight claim, then release the owner and assert one side effect and one stable replay result.

Per-site timeout tests are not required because both local timeout paths match the independently tested BIO-INT-002 control flow and were verified statically.
