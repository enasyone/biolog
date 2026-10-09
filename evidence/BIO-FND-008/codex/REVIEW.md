# BIO-FND-008 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation `8f14088` at evidence head `2cd5378`.

The structural harness passes 28/28. Builder evidence records passing .NET unit and integration suites for boot, development configuration, health/readiness, OpenAPI, tenant default refusal, and module-cycle detection. Independent .NET test execution was blocked by sandbox socket denial; compilation reached the test runner and independently reproduced the dependency warning.

## Finding

### BIO-FND-008-QA-01 — High — API pins a known vulnerable OpenAPI package despite a patched 2.x release

`Biolog.Api.csproj` directly pins `Microsoft.OpenApi` 2.4.1. NuGet emits `NU1903` and the committed audit transcript classifies it High under `GHSA-v5pm-xwqc-g5wc` / `CVE-2026-49451`.

The implementation comments and documentation incorrectly state that the entire 2.x line is vulnerable and that only 3.x is fixed. The Microsoft OpenAPI.NET maintainer advisory says affected 2.x versions end at 2.7.4 and explicitly identifies 2.7.5 as the patched 2.x release. The advisory assigns CVSS 7.5 and describes unauthenticated network-triggerable process termination when an application parses a crafted circular OpenAPI document.

This violates the Definition of Done requirement for zero unresolved High security issues. Documenting the warning does not close it.

Required closure: move to a compatible patched package set that resolves `Microsoft.OpenApi` to 2.7.5 or later (or another advisory-free implementation), prove build/OpenAPI generation still pass, and make the vulnerability audit fail the quality gate on High findings.

Maintainer advisory: https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-v5pm-xwqc-g5wc

## Reproduction

```sh
python3 evidence/BIO-FND-008/codex/independent-review.py
bash tests/foundation/BIO-FND-008.test.sh
dotnet list api/Biolog.Api/Biolog.Api.csproj package --vulnerable --include-transitive
```

Independent dependency probe: FAIL at 2.4.1. Structural suite: 28/28 pass. The committed `evidence/BIO-FND-008/builder/dotnet-vulnerable-packages.txt` records the same High finding.

## Category verdicts

- Functional: PASS based on structural checks and recorded executable evidence.
- Security: FAIL — QA-01.
- Tenant isolation: PASS — the shell hook clears context and invents no tenant.
- Offline/recovery: PASS for the offline structural suite; reviewer test execution was sandbox-limited.
- Concurrency: not applicable.
- Regression: PASS for the story structural suite.
