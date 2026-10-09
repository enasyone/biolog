# TD-WEB-PROVENANCE-BRIDGE security review

**Verdict: FAIL / DO NOT MERGE**  
**Reviewed:** `7f4994e`, `6353e6c`, `d644c5a`, joint sign-off `0a15495`

The route-to-secret mapping, 32-character secret floor, Web Crypto HMAC construction, and unconditional stripping of caller-supplied actor/provenance headers are sound by inspection. The identity source is not.

## ZB-WEBPROV-01 — Critical — browser-controlled demo cookie is promoted to trusted identity

`SessionProvider` writes `biolog-demo-role` with client-side `document.cookie`. The existing session file explicitly says it is a placeholder, not authentication or role-based access control; the role is also selectable through the user interface and `?as=` query parameter. `proxy.ts` nevertheless accepts that unsigned cookie, derives `demo-actor-{role}`, and uses the real server secret to mint a valid `X-Actor-Provenance` proof.

Any network caller can therefore send `Cookie: biolog-demo-role=admin` to a same-origin `/api/v1/*` route and have the trusted edge manufacture a cryptographically valid proof for `demo-actor-admin`. If that actor has tenant permissions, the caller inherits them. If it has no grants, the bridge cannot produce the required authorised-positive LIVE workflow and does not unblock the re-verification stories. Either state fails the bridge's purpose.

The same flaw survives sign-out: `signedOut` is React-only state, the cookie remains set, and the proxy never checks a server-side session. `SameSite=Lax` is a cross-site request control, not cookie authenticity; the cookie is intentionally non-HttpOnly and caller-writable.

This violates ADR-0006 Decision 3's essential requirement that the edge verify the real caller before injecting actor identity. The HMAC proves only that the proxy minted the header; it cannot repair an untrusted input used to select the actor.

### Required remediation

- Resolve actor identity from a server-authenticated, integrity-protected session or verified token that the browser cannot forge or arbitrarily upgrade.
- Fail closed when no valid session exists or after sign-out; do not default to a privileged operational actor.
- Keep demo role switching separate from real authorization. If a demo-only bridge is required, confine it to an explicitly non-production mode and synthetic backend tenant/data with no real grants.
- Add end-to-end negatives proving a forged `biolog-demo-role=admin`, `?as=admin`, caller-supplied actor/provenance headers, missing session, and signed-out session cannot obtain an authorised backend result. Add a positive using a genuinely authenticated, tenant-bound actor.

## Additional boundary note

The README confirms no tenant identity/header reaches the backend from the web today. The role-only cookie bridge therefore also cannot demonstrate a complete authenticated tenant-bound browser flow. That gap must be closed or explicitly incorporated into the trusted server session before `TD-WEB-PROVENANCE-BRIDGE-REVERIFY` can claim positive LIVE coverage.
