# BIO-VIS-002 security re-review — Zainab round 4

## Verdict: FAIL (High)

### F06 — reconciliation failure is ignored and a second card is issued

Commit `70cbf3d` adds a real deactivation reconciliation path, but `GrantAsync` wraps it in `try/catch` and deliberately continues when reconciliation fails. The unresolved ledger entry makes adoption skip the stranded Active card; the same grant then issues and activates a new card. One PERSON can therefore end the request with both the original stranded card and the new card Active.

The new retryability test proves the ledger remains unresolved while deactivation is unavailable, but does not call `GrantAsync` in that state. The wired-trigger test clears the deactivation fault before granting, so it misses the fail-open path.

**Required fix:** if any unresolved stranded card for the tenant/PERSON remains Active after reconciliation, block issuance of a new card. Add a test that keeps deactivation unavailable, invokes `GrantAsync` for a second visit, and proves `IssueAsync`/`ActivateAsync` are not called. Once reconciliation succeeds, a retry may proceed.

The disclosed “no future grant means no reconciliation” limitation also allows an Active card to persist indefinitely; an operator-triggered or broader lifecycle trigger would reduce that residual. At minimum, a future grant must fail closed rather than multiply active access.

Structural checks pass 65/65 but do not cover this adversarial sequence.
