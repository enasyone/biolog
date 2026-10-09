# BIO-VIS-002 security re-review — Zainab round 5

## Verdict: FAIL (Medium, blocking)

### F07 — successful deactivation can be resolved without an audit record

Commit `5aea251` closes F06’s duplicate-active-card path and provides a permission/provenance-gated operator trigger. However, reconciliation is not audit-convergent.

`ReconcileStrandedCardsAsync` deactivates the card, then writes the reconciliation audit, then marks the ledger resolved. If deactivation succeeds but the audit write fails durably, the method catches the error and leaves the ledger unresolved. On the next run, the card is already not Active, so the early branch marks the ledger resolved immediately without writing any audit event. The privileged physical-access mutation is permanently absent from audit/history.

**Required fix:** when an unresolved ledger entry is already not Active, write or verify the reconciliation audit before `MarkResolvedAsync`. Use a deterministic event/idempotency key so retries cannot duplicate the audit. Add a test where deactivation succeeds, audit fails, then a retry proves the audit is durably present before the ledger resolves.

F06’s fail-closed grant check and operator endpoint otherwise pass by inspection. Structural checks pass 73/73 but contain no audit-after-deactivation failure injection.
