# BIO-VIS-002 security re-review — Zainab

## Verdict: FAIL (High)

### F03 — Request cancellation bypasses F01 compensation after activation

Commit `48e43c4` substantially improves F01/F02, but the grant recovery path reuses the caller's cancellation token. In `GrantAsync`'s catch block, an issued/activated card triggers `repository.GetAsync(..., cancellationToken)` before `MarkFailedBestEffortAsync`. The in-memory repository immediately calls `ThrowIfCancellationRequested()`. If the request is cancelled after `ActivateAsync` succeeds but before audit/final persistence completes, that reread throws from inside the catch block and compensation is never attempted. The card can remain Active while the local record remains Pending.

Even if the reread were corrected, `MarkFailedBestEffortAsync` also passes the cancelled token to card lookup/deactivation, allowing the same cancellation to suppress deactivation. Recovery work already correctly uses `CancellationToken.None` for local state transitions; card verification and compensation must use a non-request recovery token as well.

**Required fix:** once an external card may exist, perform the granted-state reread, card lookup, and compensating deactivation with `CancellationToken.None` or a bounded internal recovery token independent of the request. Add a failure-injection test that cancels immediately after successful activation and proves the card is not left Active and the local row converges to a traceable terminal state.

F02's revoke-before-checkout ordering is sound by inspection. The structural harness passes 50/50 but has no cancellation-after-activation test.
