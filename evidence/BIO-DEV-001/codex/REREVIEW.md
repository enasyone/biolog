# BIO-DEV-001 remediation re-review

**Verdict: FAIL**

Reviewed frozen remediation `1d5e85d` at evidence head `6edd7f5`. Uncommitted builder edits present during review were not treated as delivered and were not modified.

## QA-01 remains open

Production `TenantContextMiddleware` still always assigns null, so every real device request still returns `tenant_context_missing`. A test-only accessor that ignores production middleware and trusts `X-Test-Tenant-Id` proves service logic in a synthetic host, but it does not create a successful path in the shipped host. The builder documentation expressly confirms every real HTTP client still fails. AC01 remains non-operational.

## QA-02 remains open

The frozen remediation commits first, then compensates after audit failure. Its own `CompensateAsync` swallows `DeviceConcurrencyConflictException`. The same deterministic race as BIO-TEN-001 applies: writer A commits and blocks/fails auditing; writer B reads A's unaudited state and commits a newer audited version; A's compensation conflicts and is swallowed, leaving state derived from A without A's audit. The documentation expressly admits compensation is skipped under an intervening writer.

Uncommitted edits switch to audit-before-commit, but those are outside the frozen review head and also retain a separate false-success concern: they emit `ALLOWED / status_updated` before a CAS that can reject the mutation.

## Reproduction

```sh
bash evidence/BIO-DEV-001/codex/remediation-review.sh
```

Functional FAIL; Security FAIL; Tenant Isolation PASS in repository logic; Offline/Recovery FAIL; Concurrency FAIL; Regression FAIL.
