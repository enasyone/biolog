# BIO-CRD-001 remediation re-review

**Verdict: FAIL**

Reviewed remediation commits `57580d7` and `fa08377` against prior Critical finding BIO-CRD-001-QA-01.

## QA-01 header forgery is closed, but the composed feature has no usable authenticated path

The bare tenant-id bypass is removed: an id now requires its matching credential, and cross-tenant id/secret pairing is denied. However, `BrandAssetsModule` creates an empty `InMemoryTenantCredentialStore`, and no production startup/configuration code ever calls `Register`. Only unit tests and integration-test fixtures provision credentials by resolving the store directly from dependency injection.

Therefore every request to the actual composed host returns `tenant_authentication_failed`, including valid PNG/JPEG/SVG uploads. The documentation mentions local-dev configuration or a seed script, but neither exists. A security remediation cannot satisfy AC01/AC05 by making all tenants permanently unable to authenticate.

Required closure: wire a real trusted provisioning source into application startup (without hard-coded secrets or an unauthenticated registration endpoint), and add a positive composed-host test that uses the same production provisioning path.

## Reproduction

```sh
bash evidence/BIO-CRD-001/codex/remediation-review.sh
```

The probe searches production host code for credential provisioning outside the store's method declaration and fails because none exists.

## Category verdicts

- Functional: FAIL — no tenant can use the composed asset API.
- Security: PASS for the original cross-tenant forgery scenario.
- Tenant isolation: PASS — mismatched tenant id/secret pairs fail generically.
- Offline/failure recovery: NOT APPLICABLE.
- Concurrency: PASS based on retained idempotency/repository coverage.
- Regression: FAIL — integration success is obtained through test-only direct DI seeding, not a production path.

Baseline: builder structural suite passed 23/23 and compiled unit runner passed 47/47. Unrelated untracked `tests/flutter/pubspec.lock` was preserved.
