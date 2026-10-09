# BIO-TEN-001 remediation re-review

**Verdict: FAIL**

Reviewed remediation commits `66964f5` and `1b412be` against prior High finding BIO-TEN-001-QA-01.

## QA-01 remains open — rollback is not concurrency-safe

The remediation repairs the single-writer audit-exception case, but `RestoreAsync` explicitly swallows a compare-and-swap failure when a concurrent writer has advanced the record. A deterministic race still leaves writer A's failed, unaudited field committed:

1. Writer A changes the name at version 1, commits version 2, then blocks in an audit sink that will fail.
2. Writer B reads version 2, changes only `license_seats`, commits version 3, and audits that field.
3. Writer A's audit fails. Its rollback from version 2 to version 1 conflicts with version 3 and is swallowed.
4. Version 3 retains writer A's name, although writer A returned `audit_unavailable` and no event audited that name.

This is the concurrency-safe scenario required by the original closure criteria. The source comments and feature documentation acknowledge the limitation. The reviewer probe now executes this interleaving.

## Evidence

```sh
bash evidence/features/bio-ten-001/codex/independent-review.sh
```

Expected result: `FAIL rollback conflict is swallowed; concurrent writer can preserve unaudited state`. The executable C# interleaving is also recorded in `AuditAtomicityProbe/Program.cs`. Its restore completed, but execution could not be completed in the reviewer sandbox because `dotnet run` stalled during build; the report does not claim it ran.

## Category verdicts

- Functional: FAIL — AC04 is still violated under a deterministic concurrent write.
- Security: FAIL — an administrative field survives without its audit event.
- Tenant isolation: PASS.
- Offline/failure recovery: FAIL — audit outage plus concurrency defeats rollback.
- Concurrency: FAIL — rollback conflict is swallowed rather than preserving audit atomicity.
- Regression: PASS for the non-concurrent cases added by the builder.
