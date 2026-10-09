# TD-WEB-PROVENANCE-BRIDGE security review — round 3

**Verdict: FAIL / DO NOT MERGE**  
**Reviewed:** `1af2e67`, `23f4477`, `5be0ef5`, `b4cde01`

ZB-WEBPROV-03 is closed: one marker file per session makes revocation additive without shared read-modify-write state, and reissue revokes the prior valid token before minting its replacement.

## ZB-WEBPROV-02 remains — High blocking — opt-in enables unauthenticated admin issuance

`BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE` is default-off, but when set to `true` it only authorizes the mechanism. It does not authenticate or authorize the caller. `startDemoSession` still accepts caller-selected `admin` and `alpha`/`beta`, and the role selector/`?as=admin` deliberately invoke it. Thus every caller in an enabled deployment can obtain the same admin token and backend provenance proof.

The implementation explicitly admits it cannot confine that token to a synthetic backend tenant: the token tenant is not load-bearing and the .NET backend resolves the actual tenant from HTTP Host. The protection is therefore an operator promise never to enable the feature on a host with real tenant data or grants, not an enforced security boundary. A dangerous opt-in does not make attacker-selected identity trusted.

Default-off also does not meet this task's positive purpose: no token/provenance is issued and the tracked browser workflows remain blocked. When enabled, the live test imports the signer directly and only reaches `tenant_context_missing`; it neither exercises the public issuance action nor reaches tenant role-based access control. It does not prove an authorized successful workflow.

Required: either derive claims from authenticated identity, or technically confine demo issuance to a dedicated synthetic backend tenant/data plane that cannot resolve or access real tenants. The enabled-mode test must directly attempt caller-selected admin issuance and prove the resulting identity cannot gain unintended privilege. Complete positive LIVE proof must reach the tenant permission decision, not stop before it.

## Additional fail-closed gap

`verifySessionToken` ignores `BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE`. Turning the flag off stops new issuance but leaves every previously issued token valid for up to 24 hours unless individually revoked. If this flag is the deployment safety switch, verification must also reject while disabled or disabling it must revoke the active issuance epoch.
