# BIO-INT-003 security review — round 4

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** remediation `93a0241`, evidence `1ae52d6`

## Closed from round 3

For ordinary roles, ZB-INT003-03 is now implemented correctly:

- `/whoami` requires the namespaced `integrations.service_account_whoami` domain permission.
- The token's tenant and live effective role scopes are passed to a gateway over BIO-IDN-006's tenant-explicit `IRolePermissionGrantRepository`.
- Missing grants deny by default; grant revocation is live; cross-tenant grants do not satisfy the check.

## Blocking finding

### ZB-INT003-07 — High — SuperAdmin creates a blanket integration token and privilege-escalation path

`KnownRoles` permits `SuperAdmin` as a service-account scope. `RolePermissionGatewayAdapter` then returns true for every permission whenever that scope is present, without consulting a tenant grant. The create endpoint allows any actor holding `integrations.service_account_manage` to mint such an account.

This violates the authoritative API architecture rule: “Service accounts/OAuth scopes map to domain permissions; no blanket tenant admin token for integrations.” It also turns the narrow service-account-management permission into an escalation primitive: its holder can mint a credential that automatically passes every present and future domain-permission check, even permissions the human manager does not hold.

The HTTP allow-path test relies on exactly this forbidden shortcut because it cannot seed a real permission grant. Mirroring the human-role SuperAdmin invariant into non-human OAuth credentials is not safe; the architecture explicitly distinguishes integration tokens from blanket tenant administrators.

Required: reject `SuperAdmin` as a service-account scope, or remove its implicit-all behavior for service accounts and require explicit tenant-scoped domain-permission mappings. Add creation/grant rejection and a test proving a service-account manager cannot mint a token for a permission they were not authorised to delegate. The real HTTP allow path must use a genuine narrow grant or another production-real narrow bootstrap fixture, not SuperAdmin.

## Verification

- Replayed `BIO-INT-003.test.sh`: 59/59 passed. The harness does not check the no-blanket-admin-token architecture rule and treats the SuperAdmin shortcut as its positive proof.
- Reviewed live scope intersection, tenant-explicit grant lookup, default deny, SuperAdmin adapter behavior, management permission boundary, and API architecture.
