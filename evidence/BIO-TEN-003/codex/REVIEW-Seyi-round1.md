# BIO-TEN-003 round-one review

Verdict: FAIL — CHANGES_REQUIRED

Reviewed builder `6ce66bf` and evidence `56f04fd` against BIO-TEN-003 AC01–AC05.

The `TenantFeatureFlagPolicyAdapter` is type-compatible with BIO-BIO-007's existing seam and correctly delegates to the new service. Configuration versioning, audit-before-commit, tenant-keyed storage, and all-disabled defaults are structurally sound. The composition-root swap itself is appropriately narrow. The following blockers remain.

## BIO-TEN-003-F01 — feature administration is unauthorised (Critical)

`PATCH /api/v1/tenants/{tenantId}/feature-configuration` performs an authorization decision but requires only `X-Actor-Id`. `ActorResolver` accepts that caller-supplied string without an actor-provenance proof, and the endpoint has no permission gateway or tenant-admin role check.

Any network caller can therefore name any canonical tenant in the URL, self-assert an actor, and enable Face 1:N or any other module. This violates the BTS/Tenant Admin user story and turns the tenant-scoped configuration into a cross-tenant privilege-escalation surface.

Reproduction: issue a PATCH for another tenant with `X-Actor-Id: attacker`, version 0, and `biometrics.face_1n_identification_enabled: true`. The endpoint calls `UpdateAsync` and commits it without checking authority.

Required: provenance-bound actor verification followed by a real permission lookup for reads/writes as appropriate, with cross-tenant negative host tests.

## BIO-TEN-003-F02 — disabling Biometrics does not fail closed for Face 1:N (High)

The service deliberately allows `Face1NIdentificationEnabled=true` while `Biometrics.Enabled=false`. `TenantFeatureFlagPolicyAdapter.IsEnabledForTenant` returns only the child flag. BIO-BIO-007's `FaceSearchService` consults only that adapter before searching.

Thus a tenant can disable the parent Biometrics module while Face 1:N remains operational. AC04 requires disabled modules to fail closed at API boundaries. A child flag may require separate explicit enablement, but it cannot override a disabled parent module.

Reproduction: update a tenant to `{ biometrics: { enabled: false, face_1n_identification_enabled: true } }`; the adapter returns true and FaceSearchService proceeds.

Required: the effective Face 1:N decision must require both the parent module and child subfeature to be enabled, with a regression exercising FaceSearchService through the real DI registration.

## BIO-TEN-003-F03 — five disabled-module switches have no enforcement boundary (High)

Card production, visitor, attendance, transport, and Document Verify flags are stored and returned but never consumed by their API modules or frontend visibility/action boundaries. The README explicitly says those consuming seams are not wired.

An all-disabled default in the configuration service is not fail-closed behavior if the protected APIs and UI actions never consult it. AC04 explicitly requires enforcement at API and UI boundaries, not merely a gateway available for future stories.

Required: wire the effective configuration into each named module's protected API and UI boundary, or obtain an explicit scope/AC change rather than documenting the missing enforcement as complete.

## BIO-TEN-003-F04 — AC05 workflow evidence is incomplete (High)

`/administration/tenants` and its live port are implemented, but `/settings` is untouched, and there is no browser-level end-to-end test for the mapped human toggle workflow. A backend PATCH/GET integration test does not prove the page uses the live adapter, that controls produce a visible result, or that disabled modules affect UI boundaries.

AC05 requires both named surfaces to be connected or explicitly HYBRID and the mapped end-to-end human workflow to pass. The README's judgement that `/settings` needs no change is not a HYBRID declaration or workflow proof.

## Verification

- Exact state before review: `READY_FOR_REVIEW`.
- Source/diff inspection confirmed the BIO-BIO-007 change is limited to the intended DI registration, import, and documentation.
- Builder reports 28 feature tests, BIO-BIO-007 23 tests, and integration 192 tests passing.
- Functional: FAIL (AC04, AC05).
- Security: FAIL (unauthorized cross-tenant mutation).
- Tenant isolation: FAIL (no authorization on arbitrary tenant path writes).
- Concurrency/data integrity: PASS for CAS and audit-before-commit inspection.
- Regression: PASS for the narrow adapter contract; overall acceptance blocked by findings above.
