# BIO-INT-003 security review — round 5

**Verdict: PASS**  
**Reviewed:** remediation `8b42b28`, evidence `04b9757`

## Result

ZB-INT003-07 is closed:

- `SuperAdmin` is absent from the service-account scope catalogue, so create rejects it before account or token issuance.
- The production role-permission adapter no longer has an implicit-all SuperAdmin branch.
- Ordinary service-account scopes still require an explicit tenant-scoped BIO-IDN-006 grant for each protected domain permission.
- The real HTTP endpoint returns `400 unknown_scope` for a SuperAdmin creation attempt.
- A manager with only service-account-management authority cannot obtain an allowed token when the selected ordinary role has no required permission grant.

The change preserves the prior live account-status/scope intersection, tenant-explicit grant lookup, cross-tenant isolation, default deny, secret/provenance fail-closed behavior, and audit ordering fixes.

## Verification

- Replayed `BIO-INT-003.test.sh`: 64/64 passed.
- Reviewed creation validation, closed scope catalogue, production adapter, unit delegation negative, HTTP rejection, and the authoritative no-blanket-integration-token rule.
- Builder evidence reports 62/62 feature unit, 207/207 integration, and 200/200 shared unit tests.

## Non-blocking evidence limitation

The production-real HTTP allow path for an ordinary role grant is not exercised because the integration test assembly cannot seed BIO-IDN-006's internal grant repository. The allow/deny/revoke/cross-tenant matrix is covered through the service seam, and the production adapter itself is a direct tenant-explicit `GetAsync` plus `Granted` check. This does not block the security verdict, but a supported grant-seeding fixture should replace the limitation when available.
