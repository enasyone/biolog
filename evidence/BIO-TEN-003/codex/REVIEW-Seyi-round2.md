# BIO-TEN-003 round-two review

Verdict: FAIL — CHANGES_REQUIRED

Reviewed remediation `c6f8e8c` and builder evidence `1f0a7c3` against BIO-TEN-003 AC01–AC05.

The round-one authorization, tenant-existence, and Face 1:N parent/child defects are closed. The scoped AC04 limitation and AC05 browser provenance limitation are explicitly documented as approved cross-story follow-ups. One new security blocker remains.

## BIO-TEN-003-F05 — weak provenance secrets are accepted (High)

`HmacActorProvenanceStore.Configure` accepts any non-empty string as the HMAC key. `ActorProvenanceProvisioning.LoadFromConfiguration` therefore treats even a one-character deployment secret as a valid authorization credential. The integration suite reinforces the defect by using `test-provenance-secret`, only 22 UTF-8 bytes.

This is below the current canonical actor-provenance control already applied elsewhere on this floor: a configured secret shorter than 32 bytes must fail closed. A weak operator configuration must not silently become the root credential for tenant-wide feature administration, including Face 1:N enablement.

Reproduction: configure `TenantFeatureConfig:ActorProvenanceSecret` as `x`; compute `HMACSHA256("x", actorId)`; `Verify` returns true and the request proceeds to RBAC. Expected: configuration below the 32-byte minimum is rejected/fails closed, with 31/32 boundary tests.

## Verification

- Functional: PASS for the round-one functional findings.
- Security: FAIL (F05).
- Tenant isolation: PASS after provenance, tenant-scoped RBAC, and existence checks.
- Concurrency/data integrity: PASS by inspection of the existing lock/audit-before-commit path.
- UI/regression: PASS WITH TRACKED FOLLOW-UP under the approved shared web-provenance bridge boundary.

