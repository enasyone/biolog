# TD-WEB-PROVENANCE-BRIDGE security review — round 6

**Verdict: PASS**  
**Reviewed:** `a9bc39d`

The three web actor IDs are now fixed canonical `per_<UUIDv7>` values and exactly match the backend seed. They remain role-only identities, but round-five tenant confinement makes every use resolve to the one configured synthetic tenant.

The role ceiling is correct. `TENANT_ADMIN`, `ENROLMENT`, and `READ_ONLY` receive no authority merely from assignment: `PermissionService` consults tenant-scoped `RolePermissionGrant` rows for all three. Only `SUPER_ADMIN` short-circuits grant lookup, and it is deliberately not seeded. No grant rows are added by this change, so re-verification must provision only each story's minimum permission.

Direct repository seeding does not bypass a load-bearing aggregate invariant. Constructors validate canonical tenant/person identifiers; repositories independently require the normal initial states (`PRE_ENROLLED`/version 1 and `ACTIVE`/version 1), use structural tenant/person/role keys, and reject collisions. The seed creates the person before its role assignment, preserves existing rows, and is idempotent on restart. Skipping request-scoped authorization/audit is proportionate for a fixed startup fixture rather than a caller-driven administrative mutation.

Integration coverage proves all three identities and roles are visible through a genuine confined demo session and absent without tenant resolution. The fixed admin identity cannot bypass the real permission-grant lookup.

Non-blocking operational condition: seeding is gated by `WebSessionBridge:SyntheticTenantId` alone, not the web issuance flag or token-secret validity. That configuration key must remain exclusive to the dedicated synthetic deployment/tenant established in round five; setting it on a real tenant would intentionally create these public fixed identities there.
