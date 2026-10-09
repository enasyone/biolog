# BIO-TEN-002 remediation re-review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed remediation `3fabca0540b0749529a23effe05d4cd85c11b970` at builder evidence head `7b3780d`.

## Finding status

### BIO-TEN-002-QA-01 — High — remains open

The query-parameter half is fixed: the feature-local diagnostic endpoint now reads `context.Request.Host.Host` and ignores `?host=`. However, the original finding also required the real shipped host channel mandated by ADR-0002 Decision 6. That production integration remains absent and is now explicitly acknowledged by the remediation documentation:

- `BioTen002Module` is not registered in `api/Biolog.Api`.
- `TenantDomainService.ResolveAsync` is not connected to the shipped `TenantContextMiddleware`.
- The positive tests still self-host `BioTen002Module` directly rather than exercise the packaged API's tenant-context pipeline.
- A repository search finds no `BioTen002Module` or `TenantDomainService` reference under `api/Biolog.Api`.

ADR-0002 Decision 6 is explicit that BIO-TEN-002 “implements the host channel,” that it is “a real, production resolution mechanism,” and that once it ships verified custom hostnames provide a genuine shipped-host positive path. A feature-local diagnostic endpoint that is unreachable from the packaged API and does not produce a tenant context is not that mechanism. Correcting the documentation to stop overclaiming is necessary but cannot turn missing production behavior into a pass.

Required closure: obtain the approved shared-path allocation, register the module in the shipped host, connect verified-host resolution to `TenantContextMiddleware`, and prove through the real `Biolog.Api` pipeline that a verified request hostname yields the correct resolved context while unknown, pending, suspended, and retired hostnames fail closed. Until then BIO-TEN-002 cannot end the interim accommodation or satisfy the user story's “route to the correct BIOLOG tenant” outcome.

### BIO-TEN-002-QA-02 — closed

`HostNormalizer` now strips a colon suffix only when it is 1–5 ASCII digits and parses within 0–65535. Empty, nonnumeric, signed, overflow, repeated-colon, and suffix-bearing forms return null and therefore `host_not_recognised`. Builder evidence records the unchanged independent `HostExactnessProbe` passing all three original reproductions.

## Regression evidence

- Offline structural suite: 33/33 PASS.
- Repository traceability inventory: PASS, 18 implemented stories.
- Builder evidence: 90/90 feature tests and independent original host-exactness probe PASS.
- Local independent probe could not run with `--no-restore` because its generated assets were intentionally not committed; no local runtime execution is claimed.

Functional FAIL; Security FAIL; Tenant Isolation FAIL; Offline/Recovery NOT APPLICABLE; Concurrency PASS; Regression FAIL.
