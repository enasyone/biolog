# BIO-TEN-003 — TD-WEB-PROVENANCE-BRIDGE re-verification

**Verdict: PASS**  
**Reviewed implementation:** `472aff9`  
**Builder evidence:** `4fdf920`

The startup fixture and LIVE workflow preserve the security boundaries required for the
post-acceptance re-verification:

- Seeding is inert unless `WebSessionBridge:SyntheticTenantId` is configured and is scoped to
  that exact structural tenant key.
- Only `tenant.feature_configuration.read` and
  `tenant.feature_configuration.manage` are introduced. Tenant Admin receives read/manage;
  Read Only receives read only. Existing grant rows, including an explicit revocation, are not
  overwritten.
- The URL tenant remains the authorization tenant. BIO-IDN-006 resolves role assignments and
  grants with that explicit tenant ID, so a caller-controlled `targetTenant` cannot reuse the
  synthetic tenant's assignments or grants against another tenant.
- The signed session token supplies the ambient synthetic tenant needed by BIO-IDN-006's person
  existence adapter, but does not replace the explicit-tenant permission lookup.
- The real-host tests prove admin read/write, viewer read plus write denial, unseeded-person
  denial, missing-actor denial, and missing-provenance denial. The browser proof uses the real
  proxy/session bridge and checks the server-side 403 rather than a client-only restriction.
- The route-table entry selects the BIO-TEN-003 provenance secret. No other current API route
  exists under `/api/v1/tenants`; future routes under that prefix must be reviewed before reuse.

## Operational condition

`WebSessionBridge:SyntheticTenantId` must remain a dedicated synthetic tenant. If an operator
points it at an existing real tenant, startup intentionally seeds the fixed demo identities and
these three narrow grant rows into that tenant. This is an existing trusted-configuration boundary
of the shared demo bridge and should be enforced by deployment policy or a future explicit
synthetic-tenant marker before production enablement.

## Verification

- Inspected the complete `20f9312..472aff9` production/test delta.
- Inspected BIO-IDN-006 `ActorHasPermissionAsync` and confirmed tenant-explicit assignment/grant
  lookup after the ambient person-existence prerequisite.
- Builder evidence reports the story suite 46/46 and focused real-host tests passing; no new
  blocking security finding was found.
