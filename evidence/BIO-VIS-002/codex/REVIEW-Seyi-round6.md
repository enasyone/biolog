# BIO-VIS-002 independent re-review — round 7

Verdict: **FAIL**

Reviewed builder commit `4da8f2b` and implementation commit `5aea251` against the approved BIO-VIS-002 acceptance criteria and the unresolved F06 recovery findings.

## Independent findings

- Functional/recovery: FAIL on audit convergence. `ReconcileStrandedCardsForPersonAsync` is a real operator path and can deactivate a ledgered card without a later visitor action, but its recovery sequence can permanently omit the required audit record.
- Security: PASS. The endpoint requires asserted actor provenance and `visitor_credentials.manage`; an empty `person_id` is rejected. Tenant identity comes from the current tenant context, so callers cannot select another tenant in the request body.
- Data integrity: PASS. `GrantAsync` retries reconciliation, then re-reads unresolved ledger entries and throws `stranded_card_unresolved` before any new card issue when recovery has not converged. This prevents a second active card for the same person/card class.
- Concurrency: PASS for the remediated path. Grant and explicit reconciliation use the same tenant/person lock; the recovery loop re-reads the current card version and only resolves the ledger after the card is confirmed non-active or deactivation succeeds.
- Audit/data integrity: FAIL. `ReconcileStrandedCardsAsync` deactivates the card, then audits, then resolves the ledger. If deactivation succeeds and the audit write fails, the catch leaves the entry unresolved. On retry, the card is already inactive, so the early branch marks the ledger resolved without writing or verifying the missing audit. The physical-access mutation is then permanently absent from audit history, violating AC05.
- Regression: PASS. Checkout and revoke only add best-effort reconciliation; their existing primary behavior remains reachable.

## Verification

- `bash tests/features/bio-vis-002/BIO-VIS-002.test.sh`: **73 passed, 0 failed**.
- Source inspection confirmed the endpoint is registered through `BioVis002Module.MapEndpoints`, and the service/ledger checks occur before card issue.
- Local `dotnet test` compiled both feature and test assemblies successfully, but execution was aborted by the workspace restriction `System.Net.Sockets.SocketException (13): Permission denied` in the Visual Studio test runner. Builder evidence on the reviewed commit records BIO-VIS-002 unit **42/42**, BIO-VIS-001 regression **25/25**, and integration **192/192**.

## Non-blocking note

The older `ReconcileStrandedCardsAsync` XML comment still says that no operator-independent trigger exists. That comment is stale after this remediation; the actual endpoint and later method documentation are correct. This does not change runtime behavior.

## Required remediation

Before resolving an already-inactive ledger entry, write or verify the deterministic reconciliation audit event. Use a stable idempotency key so repeated recovery cannot duplicate it. Add failure-injection coverage for: deactivation succeeds, audit fails, retry restores/verifies the audit, and only then the ledger resolves.
