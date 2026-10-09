# BIO-INT-003 security review — round 2

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** remediation `46fa855`, evidence `bd66442`

## Closed from round 1

- ZB-INT003-01: access-token signing and secret hashing now refuse missing/short configuration; no public dummy key remains in those two primitives.
- ZB-INT003-02: management routes now require an actor, provenance proof and live tenant permission lookup.
- ZB-INT003-04's lost-secret failure is closed: audit precedes mutation and the post-audit repository write is non-cancellable.
- `RateLimitKey` remains a collision-safe structural tuple.

## Blocking findings

### ZB-INT003-03 — High — AC02 remains unimplemented

The approved criterion says OAuth2/token scopes map to permissions. Tokens still contain role-name strings only. `AuthorizeAsync` checks signature, expiry and rate limit, but accepts no required scope/permission and does not query live grants; `whoami` accepts every valid token including an empty-scope token. Revoked accounts' already-issued tokens also remain accepted until expiry.

ADR-0006 Decision 5 can define integration boundaries, but it cannot turn an explicit acceptance criterion into a documentation-only follow-up. Required: define and enforce a scope-to-permission decision on at least one real protected resource, with insufficient-scope, revoked-account, removed-grant and cross-tenant negatives.

### ZB-INT003-05 — Critical — remediation introduces a second public dummy-key authorization bypass

The new `HmacActorProvenanceStore` uses `SHA256("no-provenance-secret-configured")` as its HMAC key when configuration is absent. That literal is in source. An attacker can compute `HMAC-SHA256(dummyKey, privilegedActorId)`, pass the provenance gate, and then have the application query the privileged victim's real RBAC grants. This recreates the impersonation bypass the remediation was intended to close.

Required: use the same fail-closed configuration behavior now applied to the access-token codec—unconfigured provenance verification must always return false, never calculate with a public fallback. Add an adversarial test that computes the former dummy-key proof for a genuinely privileged actor and receives 401.

### ZB-INT003-06 — Medium — audit-before-mutation records actions that never occurred

Rotate and revoke write `SecretRotated`/`ServiceAccountRevoked` before the repository checks existence and expected version. A missing account returns 404 after a durable “rotated/revoked” event; a stale version returns 409 after the same false completion event. This damages immutable security-audit integrity.

Required: serialize and prevalidate existence/version before writing a completion-shaped audit event, then commit non-cancellably; alternatively use explicit attempt/completion events whose semantics remain truthful. Add not-found and concurrency-conflict audit assertions.

## Verification

- Replayed `BIO-INT-003.test.sh`: 47/47 passed. Its RBAC checks do not test public dummy-key proof forgery, and it treats documented non-enforcement as sufficient for AC02.
- Reviewed token/pepper changes, provenance implementation, live permission adapter, service ordering and endpoint tests.
