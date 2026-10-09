# TD-PHO-002-IDEMPOTENCY-RACE review

Verdict: **FAIL (test evidence); production fix passes inspection**

Commit `fd53b7e` correctly adds claim ownership and bounded wait/replay to `SubmitAsync`; direct idempotency-store tests were updated for the new ownership result. Structural harness: **38/38**.

The new same-key test uses only a start gate. The scheduler may finish one submission before the second claims, so the historical broken code could pass. Add a deterministic in-flight gate: hold the owner after claim acquisition, wait for the loser to observe the in-flight claim, then release and prove one capture/audit and one stable result.

A local timeout test is not required because the copied timeout branch and 503 mapping match the independently tested BIO-INT-002 path.
