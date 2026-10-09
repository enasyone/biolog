# BIO-INT-003 security review — round 1

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** implementation `6903622`, builder evidence `6b44153`

## Blocking findings

### ZB-INT003-01 — Critical — missing configuration enables token forgery

`HmacApiAccessTokenCodec` substitutes a deterministic key derived from the public source string `no-api-access-token-secret-configured` when `ApiSecurity:AccessTokenSecret` is absent. The same public key is used for issuing and validating. The feature test explicitly proves two unconfigured codec instances accept one another's token. This is fail-open, not fail-closed: anyone who can read the source can mint a valid token for any tenant, client, role-shaped scope set and future expiry.

Required: refuse service startup or refuse both issuance and validation until a sufficiently strong secret is configured. Remove the shared dummy signing key and add a test that an unconfigured host cannot issue or accept a token.

The secret-hash pepper uses the same unsafe configuration pattern. A 256-bit generated client secret limits immediate exploitation, but missing pepper configuration must also fail startup rather than silently downgrade the at-rest control to a public HMAC key.

### ZB-INT003-02 — Critical — service-account administration has no actor or authorization gate

Create, list, rotate-secret and revoke endpoints require only ambient tenant context. They do not resolve an actor, verify ADR-0006 provenance, or check a tenant role-based access control permission. Any caller that reaches a tenant plane can create credentials—including a `SuperAdmin`-scoped account—read account metadata, rotate another integrator's secret, or revoke the account.

Required: apply verified actor provenance and a live tenant permission check to every management/read operation. Add unauthenticated, forged-actor, unprivileged-actor and cross-tenant negatives. One-time plaintext secret responses must only be reachable after that authorization succeeds.

### ZB-INT003-03 — High — AC02 scopes do not map to or enforce permissions

The implementation uses the eleven role names as scopes and explicitly does not query BIO-IDN-006 permission grants. `AuthorizeAsync` validates signature/expiry and rate limit only; the sole protected route does not require even one scope. A role label is not a mapping to the permissions currently granted to that role, and no resource authorization decision consumes it. Revoked accounts' already-issued self-contained tokens also remain accepted until expiry because authorization never rereads account state.

Required: define the scope-to-permission contract and enforce required permission(s) at a real protected resource. Validate against current tenant grants/account status or define and implement bounded revocation semantics. Tests must prove insufficient scope, removed grant, revoked account and cross-tenant token use fail closed.

### ZB-INT003-04 — High — credential mutations commit before audit and can lose the only new secret

Create, rotate and revoke mutate the repository first and write audit afterward using the caller's cancellation token. `IApiAuditSink` claims existence-recheck recovery, but `ServiceAccountService.AuditAsync` performs only one `WriteAsync` and never calls `ExistsAsync`. If audit fails or cancellation arrives after rotation commits, the old secret is invalid, the new plaintext is not returned, and no rotation audit exists. Create can similarly persist an undisclosed credential; revoke can commit without its required API audit.

Required: implement the repository/audit point-of-no-return discipline used elsewhere: audit-before-commit where possible, ambiguous-write verification, and non-cancellable completion after durable mutation. Add genuine audit throw/cancellation tests that prove no silent mutation and no lost rotated secret.

## Additional results

- The `RateLimitKey` record struct correctly gives structural equality over tenant, client and route; the BIO-INT-002 delimiter-collision class is closed.
- Secret generation uses 32 random bytes, hashes compare in constant time, plaintext is absent from the account and audit types, and optimistic version checks serialize rotation/revocation.
- The fixed-window limiter is process-local. This must be documented as a single-process tier or replaced by a shared atomic limiter before multi-instance deployment.
- The token endpoint binds JSON, while OAuth 2.0 client-credentials requests normally use form encoding. This is an interoperability issue for functional review, not the security verdict driver.

## Verification

- Replayed `tests/features/bio-int-003/BIO-INT-003.test.sh`: 40/40 passed. The harness checks that the RBAC gap is documented; it does not prove the missing control acceptable.
- Reviewed endpoint composition, token/secret cryptography, repository isolation, mutation ordering, audit sink, rate limiter and focused tests.
