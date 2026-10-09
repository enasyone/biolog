# Retroactive specialist security review — 2026-09-22

Reviewer: Zainab, Security Engineer (`zainab-mucme6zt`)

Scope: accepted heads `d53fc43` (BIO-TEN-001), `0a142bd` (BIO-CRD-001), and `3c3d60b` (BIO-TEN-002). Review covered tenant isolation, credential/secret handling, and audit-trail integrity.

## Verdicts

| Story | Verdict | Result |
| --- | --- | --- |
| BIO-TEN-001 | PASS with disclosed boundary | Audit-before-commit, per-tenant serialization, ambiguous audit acknowledgement verification, and post-audit cancellation handling are sound for the documented singleton/in-memory tier. Reopen for durable, remote, multi-process stores. |
| BIO-CRD-001 | PASS after remediation | The original High finding was reproduced, fixed at `159ec1c`, documented at `fe8d6d9`, merged to develop at `be8683b`, and independently re-reviewed. |
| BIO-TEN-002 | PASS with disclosed boundary | Exact verified-host resolution, malformed-authority rejection, set-once internal context construction, defensive channel copying, and fail-closed unknown/unverified handling are sound. Principal/service channel reconciliation and RBAC remain explicitly assigned to later stories. |

## ZA-CRD-001 — canonical tenant identifiers rejected

- Severity/status: High — Closed after independent re-review on 2026-09-22.
- Reproduction: BIO-TEN-001 defines canonical tenant identifiers as `^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$`. BIO-CRD-001's `HeaderTenantAuthenticator` accepts only `^[a-z0-9][a-z0-9-]{0,63}$`. The required underscore in every canonical identifier therefore causes authentication to return null before the correctly provisioned secret is checked.
- Impact: all real tenants minted by the control-plane registry receive `401 tenant_authentication_failed` on brand-asset endpoints. This is an authorization-boundary integration failure and a denial of service for canonical tenants; it does not permit cross-tenant access.
- Test gap: the accepted authenticator and endpoint tests use non-canonical tenant strings such as `tenant-alpha`, `victim-tenant`, and `ten_a`, so 106/106 unit and historical 35/35 integration results do not cover the frozen identifier contract.
- Fix: `TenantIdPattern` now exactly matches the frozen canonical `ten_<UUIDv7>` contract. Unit and composed-host HTTP regression tests use genuine canonical identifiers; fixtures were migrated away from slugs; canonical-adjacent malformed identifiers and cross-tenant secret substitution remain rejected.
- Re-review evidence: diff `0a142bd..fe8d6d9` inspected; structural 28/28, unit 112/112, and fallback secret scan pass. The integration executable again stalled after discovery/start in this reviewer session and was stopped; Ngozi's commit-bound evidence records 36/36, and the same environment limitation predates this fix.

## Replay evidence

- BIO-TEN-001: structural 29/29, feature tests 51/51, fallback secret scan clean.
- BIO-CRD-001: structural 26/26, unit tests 106/106, fallback secret scan clean. Integration runner again stalled after discovery/start, matching the accepted environment limitation; the finding is independently reproducible from the frozen regexes and fixtures.
- BIO-TEN-002: structural 37/37, feature tests 90/90, shared unit tests 154/154, fallback secret scan clean.

No implementation defect was silently repaired during review.
