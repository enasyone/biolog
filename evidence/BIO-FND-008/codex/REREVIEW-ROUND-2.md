# BIO-FND-008 independent re-review — round 2

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed remediation `fb8c488` plus build correction `ee9f6d1` at evidence head `115a4ee`.

QA-01 is closed: `Microsoft.OpenApi` is pinned to patched 2.12.2, the structural suite passes 30/30, and the committed audit records no vulnerable packages.

## Finding

### BIO-FND-008-QA-02 — High — Vulnerability gate reports PASS when the audit command fails

`scripts/check-dotnet-vulnerabilities.sh` captures `dotnet list ... --vulnerable` output but never checks its exit status. A restore, advisory-source, network, authentication, or tool failure that does not print the vulnerable-package phrase falls through to `DOTNET VULNERABILITY AUDIT: PASS` and exit 0.

This reproduced both with a controlled fake `dotnet` returning 42 and in the reviewer environment, where NuGet advisory-source resolution failed but the wrapper still returned 0 and PASS. `scripts/quality.sh --full` therefore has a false-success path and cannot guarantee the claimed security gate.

Required closure: preserve and check each `dotnet` command status, fail closed on any nonzero status, retain vulnerable-output detection, and add regression coverage for command failure as well as vulnerable and clean output.

## Reproduction

```sh
python3 evidence/BIO-FND-008/codex/independent-review.py
bash tests/foundation/BIO-FND-008.test.sh
bash scripts/check-dotnet-vulnerabilities.sh api/Biolog.Api/Biolog.Api.csproj
```

Independent probes: 3 PASS, 1 FAIL at audit command failure. Structural suite: 30/30 PASS. The live audit printed NuGet source errors, then incorrectly printed PASS and exited 0.

## Category verdicts

- Functional: PASS for the API shell behavior established previously.
- Security: FAIL — QA-02 permits false-success vulnerability audits.
- Tenant isolation: PASS — tenant context remains unset by default.
- Offline/recovery: FAIL — advisory-source failure is misreported as success.
- Concurrency: not applicable.
- Regression: PASS for QA-01 and the 30 structural checks; FAIL for the new audit failure boundary.
