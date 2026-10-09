# TD-WEB-PROVENANCE-BRIDGE security review — round 5

**Verdict: PASS WITH TRACKED RE-VERIFICATION**  
**Reviewed:** `e92be7a`, reverted round-four experiment, backend confinement `72945c7`

ZB-WEBPROV-02 is closed for the approved demo/showcase scope. The backend independently verifies the signed token with a separate 32-character-minimum configured secret and, on success, resolves one configured synthetic tenant before Host resolution. Token role and tenant claims cannot select the backend tenant. Tests prove a valid demo token overrides a simultaneously valid real Host, while admin and viewer claims reach the same tenant-scoped permission denial. Invalid, expired, malformed, wrong-key, missing, or unconfigured tokens do not activate the demo channel.

The earlier controls remain intact: issuance is explicit/default-off; verification also checks the live issuance flag; caller identity headers are stripped; session tokens are HttpOnly and role+tenant signed; reissue revokes the old session; per-session revocation markers cannot clobber each other.

## Conditions and residual boundaries

- `WEB_SESSION_TOKEN_SECRET` and `WebSessionBridge:TokenSecret` must be provisioned to the same strong value, and `WebSessionBridge:SyntheticTenantId` must remain a dedicated synthetic tenant with no real data or grants. This is now load-bearing deployment configuration.
- The backend intentionally does not share the web revocation store. The accepted topology therefore requires API traffic carrying this marker to pass through the web proxy; direct backend exposure remains outside this demo bridge's trust model.
- Disabling and re-enabling the issuance flag revives an otherwise unexpired, non-revoked token. Operators needing permanent emergency invalidation must rotate the session secret or retain the flag off; this is not equivalent to a burned issuance epoch.
- This pass does not complete `TD-WEB-PROVENANCE-BRIDGE-REVERIFY`. Current integration evidence reaches the real permission denial, not an authorised success. The tracked follow-up must still prove authorised-success and unprivileged-deny browser workflows for BIO-RPT-004, BIO-BIO-009, and BIO-TEN-003.

No remaining merge blocker was found within Kemi's approved synthetic-demo confinement scope.
