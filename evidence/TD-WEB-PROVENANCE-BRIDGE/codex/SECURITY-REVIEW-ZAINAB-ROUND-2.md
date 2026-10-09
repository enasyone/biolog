# TD-WEB-PROVENANCE-BRIDGE security review — round 2

**Verdict: FAIL / DO NOT MERGE**  
**Reviewed:** `bb23f46`, `2f43919`, `8f7cd1c`, `8090318`, `5e75ee6`

The signed token prevents offline claim tampering, and proxy header stripping, secret separation/floor, expiry checks, HttpOnly cookie handling, and revoke-then-replay verification are useful improvements. The trusted identity requirement is still not met.

## ZB-WEBPROV-02 — Critical — public Server Action signs caller-selected admin identity

`startDemoSession(tenantId, roleId)` accepts any allow-listed tenant and role supplied by its caller and immediately signs them. It performs no authentication, no authorization, and no production/demo-mode check. `SessionProvider` deliberately calls it from the existing role selector and from the caller-controlled `?as=` query parameter.

An attacker no longer edits the cookie bytes directly; they ask the server to issue `admin` legitimately. The result is equivalent at the authorization boundary: the proxy trusts the token, derives `demo-actor-admin`, and mints a real backend provenance proof. A signature proves only that this unauthenticated issuance endpoint signed the attacker's chosen claims.

The code is not confined to a synthetic backend tenant. The token's `tenantId` is not consumed by the backend tenant resolver; the HTTP Host channel still selects the actual tenant. Therefore any host tenant that grants a demo actor permissions is exposed. If no tenant grants those actors, this bridge still cannot satisfy the required positive authorized LIVE workflow.

Required: derive actor and tenant claims from a server-authenticated session or other trusted principal, not Server Action arguments. If Kemi permits a demo-only issuer, enforce a non-production mode at the server boundary and bind it to a dedicated synthetic tenant/data plane with no route to real tenant grants. Add an adversarial test that directly invokes the issuance action with `admin`/alternate tenant and proves privilege cannot be obtained.

## ZB-WEBPROV-03 — High — reissue and concurrent revocation can preserve old privilege

Changing role or tenant calls `startDemoSession`, which overwrites the browser cookie without revoking the previous token. A copied admin token therefore remains valid for 24 hours after switching to viewer or another tenant. Separately, the file-backed revocation store uses unlocked read-modify-rename; two concurrent sign-outs can each read the same state and the later rename can erase the other's revocation. The implementation explicitly acknowledges this race, but for an authorization credential it means a supposedly revoked stolen token can remain usable.

Required: revoke the current session identifier before/atomically with reissue, and use an atomic additive store or shared lock/transaction that cannot lose concurrent revocations. Add role-downgrade/tenant-switch replay and barrier-controlled concurrent-revocation tests.

## Test-evidence limitation

The live positive test imports `issueSessionToken` and mints its own admin token with the signing secret. It proves HMAC interoperability, not that the production issuance path authenticated or authorized the caller. It also stops at `tenant_context_missing`, before tenant role-based access control, so it does not yet satisfy the tracked authorised-success browser workflow.
