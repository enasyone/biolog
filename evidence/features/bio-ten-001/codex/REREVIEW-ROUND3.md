# BIO-TEN-001 remediation re-review — round 3

**Verdict: FAIL**

Reviewed remediation `5655c49` / evidence `9e80de4` against AC04 and BIO-TEN-001-QA-01.

Audit-before-commit prevents committed state without an audit event, closing the previous race. It creates the inverse integrity defect: every candidate mutation is durably recorded as `Outcome = Allowed` with a success reason such as `tenant_updated` before the compare-and-swap commit. If a concurrent writer commits first, the candidate's commit returns `concurrency_conflict`, but its permanent audit event still falsely says the administrative change was allowed and updated.

The deterministic probe holds writer A after its audit write, lets writer B commit the same expected version, then releases A. A returns `concurrency_conflict`; A's durable event remains `ALLOWED / tenant_updated`, although A's name was never committed. This is not an audit of an attempt: the envelope explicitly records a successful outcome and success reason.

Required closure: use one atomic transaction/outbox, or model the pre-commit record honestly as an attempt and append a durable error/aborted outcome if the commit fails, with correlation that prevents consumers from treating the attempt as the final change. The final audit view must not claim a rejected mutation succeeded.

## Reproduction

```sh
bash evidence/features/bio-ten-001/codex/independent-review.sh
```

Expected: `FAIL rejected concurrent writer can retain a durable ALLOWED success audit`. The executable interleaving is recorded in `AuditAtomicityProbe/Program.cs`; its restore completed, but `dotnet run` stalled during build in the reviewer sandbox, so this report does not claim it executed.

Category verdicts: Functional FAIL; Security FAIL; Tenant Isolation PASS; Offline/Recovery PASS for audit outage; Concurrency FAIL; Regression FAIL.
