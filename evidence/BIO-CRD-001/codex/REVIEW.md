# BIO-CRD-001 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation `e697bd1` at evidence head `9372223`.

The supplied foundation suite passes 20/20 for format, metadata, raster quality, SVG/malware hooks, and repository filtering. AC05 is not enforced at the request boundary.

## Finding

### BIO-CRD-001-QA-01 — Critical — Caller-controlled tenant header grants access to any tenant namespace

The production module registers `HeaderTenantClaimReader`, which treats `X-Biolog-Tenant-Id` as the tenant authority after only a syntax check. There is no authenticated principal, host-domain agreement, resolved tenant context, or provenance binding. Any caller can change the header from its own tenant ID to a victim tenant ID and the endpoints will query the repository as that victim.

Repository methods correctly filter by the tenant string they receive, but that provides no isolation when the attacker chooses the string. Existing cross-tenant tests show that tenant B cannot retrieve tenant A's asset while still claiming B; they do not test a B caller claiming A, because the implementation has no caller identity against which to detect the forgery.

The implementation documentation explicitly says this is not ADR-0002 resolution and the header is not verified. That limitation directly conflicts with AC05, rather than satisfying it as a temporary seam.

Required closure: derive tenant identity from the provenance-checked resolved tenant context established by ADR-0002, fail closed when it is unavailable, and reject/remove the direct header authority. Add end-to-end tests proving a caller cannot select another tenant through any request-controlled channel.

## Reproduction

```sh
bash evidence/BIO-CRD-001/codex/independent-review.sh
bash tests/foundation/BIO-CRD-001.test.sh
```

Independent boundary probe: FAIL. Supplied foundation suite: 20/20 PASS, but checks only missing-header behavior and repository filtering after trusting the supplied tenant string.

## Category verdicts

- Functional: FAIL — AC05 is not met.
- Security: FAIL — request input controls authorization scope.
- Tenant isolation: FAIL — any tenant namespace can be selected by header.
- Offline/recovery: PASS.
- Concurrency: not applicable to the blocking defect.
- Regression: FAIL — tests omit tenant-claim forgery.
