# TD-CRD-007-IDEMPOTENCY-RACE review

Verdict: **FAIL (test evidence); production fix passes inspection**

Commit `85d4fc2` correctly adds claim ownership and bounded wait/replay to `CompleteOfflineJobAsync`. Structural harness: **44/44**.

The same-key test is distinct from the older different-key probe, but its single start gate does not force overlap. One call can complete before the second claims, allowing the historical bug to pass. Pause the owner while its claim remains in-flight, wait until the loser observes that claim, then release and assert one job/audit and one stable result.

A separate timeout test is not required; the copied timeout branch and 503 mapping are correct by inspection.
