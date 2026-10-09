# BIO-OPS-006 — Seyi independent review

Verdict: **FAIL** at builder commit `78bcf8a`.

## SY-OPS006-01 — High — cross-tenant gate does not execute the negative suite

AC03 requires the cross-tenant negative suite to pass. The focused 21-check gate only runs `grep`
against selected test files and one implementation file. It never runs `Biolog.IntegrationTests` or
any other cross-tenant test assembly. The default `scripts/quality.sh` path also omits all .NET test
execution; those commands exist only behind `--full`, which the builder did not complete.

Therefore deleted, skipped, uncompilable or failing test bodies can still produce the advertised
AC03 PASS as long as matching words remain in source. The builder evidence itself says runtime
integration execution was unavailable, so there is no independent result that closes this gap.

Reproduction:

```bash
rg 'dotnet test .*Biolog.IntegrationTests' tests/features/bio-ops-006/BIO-OPS-006.acceptance.sh
```

It returns no match while the same script prints seven AC03 PASS lines.

## SY-OPS006-02 — High — history secret fallback is materially weaker than the tree scan

When gitleaks is absent, `history-secret-scan.sh` recognizes only AWS access-key IDs, private-key
headers, Slack tokens and one GitHub token form. It omits the current-tree scanner's generic API
keys, passwords and credential-bearing URLs. A real password can therefore be committed, deleted
from HEAD, and reported clean by the mandatory history gate.

The independent mutation probe initializes a repository, commits
`database_password=CorrectHorseBatteryStaple42`, deletes it in a later commit, and runs the exact
fallback. Actual result: `history-secret-scan: clean`, exit 0. The current-tree scanner's own
password rule would detect the same value while present.

## Commands and results

- Builder gate: `bash tests/features/bio-ops-006/BIO-OPS-006.acceptance.sh` — reported 21/21 PASS.
- Independent gate: `bash tests/features/bio-ops-006/BIO-OPS-006.adversarial.sh` — 0 PASS, 2 FAIL.
- Direct history mutation probe — false clean, exit 0.

## Category verdicts

- Functional: APPLICABLE / PASS — documentation and gate are callable.
- Security: APPLICABLE / FAIL — AC04 false-clean history scan.
- Tenant Isolation: APPLICABLE / FAIL — AC03 suite was not executed.
- Offline: APPLICABLE / UNVERIFIED — source binding only, no runtime result.
- Concurrency: APPLICABLE / UNVERIFIED — source binding only, no runtime result.
- Regression: APPLICABLE / FAIL — the mandatory gate accepts both mutations above.

Required remediation: make the hardening gate execute the relevant negative test assemblies and
fail closed when execution is unavailable. Use gitleaks as a required pinned tool or make the
history fallback cover at least the same credential classes as the current-tree scanner. Add
mutation tests proving failing cross-tenant bodies and deleted historical credentials fail the gate.
