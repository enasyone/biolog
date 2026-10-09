# BIO-INT-003 security review — round 3

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** remediation `c5e2f9d`, evidence `e193f44`

## Closed from round 2

- ZB-INT003-05: the provenance verifier now returns false when its key is absent or too short. The explicit branch depends only on static process configuration, not request-controlled secret data; it is safe even though it differs from the floor sweep's random-dummy pattern. The former public proof regression exercises the real HTTP gate.
- ZB-INT003-06: ordinary missing-account and stale-version requests are rejected before a completion-shaped audit is written.
- Revoked accounts and withdrawn scope labels are re-read on every protected call; `/whoami` now denies a token lacking `ReadOnly`.

## Blocking finding

### ZB-INT003-03 — High — role labels are still not mapped to domain permissions

AC02 and `API-ARCHITECTURE.md` require OAuth scopes to map to domain permissions. The remediation instead declares that a role label *is* a permission. `KnownRoles` contains `ReadOnly`, `Gate`, `SuperAdmin`, and the other human role names; `/whoami` checks the literal `ReadOnly` label. It never resolves a namespaced domain permission key or consults BIO-IDN-006's tenant-scoped role-to-permission grants.

That conflicts with the authoritative RBAC model: `RolePermissionGrant` explicitly defines which permission keys a role carries as per-tenant configuration, with an absent grant denying by default. A tenant's `ReadOnly` role could be granted or denied `service-accounts:whoami` (or its canonical namespaced equivalent), but this code ignores that mapping and authorizes solely from the role-shaped token string. Conversely, a service account cannot be granted a narrow domain permission without being assigned a whole human role label.

The new tests prove live role-label intersection, not scope-to-permission mapping. Documentation cannot redefine a role into a permission contrary to the dependency's domain model.

Required: define OAuth scopes as domain permission keys, or add an explicit tenant-scoped scope-to-domain-permission mapping. A protected resource must require a domain permission and resolve it from the token's live effective scopes/mapping. Add allow/deny tests showing the same role/scope label produces different results when the tenant permission grant changes, plus cross-tenant negatives.

## Verification

- Replayed `BIO-INT-003.test.sh`: 54/54 passed. Its AC02 checks validate a closed role catalogue and one literal-label check; they do not prove mapping to domain permissions.
- Reviewed the provenance branch, live account reread, endpoint denial handling, audit prevalidation, tests, story AC02, `API-ARCHITECTURE.md`, and BIO-IDN-006 `RolePermissionGrant` semantics.
