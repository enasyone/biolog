# BIO-TEN-003 — TD-WEB-PROVENANCE-BRIDGE re-verification

**Verdict: PASS with non-blocking advisories**

Reviewed implementation commit `472aff9` and evidence commit `4fdf920` independently against the tracked post-acceptance obligation: prove a successful authorised LIVE browser workflow after the shared web-session provenance bridge, plus a differentiated unprivileged denial.

## Acceptance result

- The web route table now supplies `TenantFeatureConfig:ActorProvenanceSecret` for the feature-configuration request, so the proxy can derive the canonical demo PERSON actor and inject the matching HMAC proof.
- Startup provisioning is inert unless `WebSessionBridge:SyntheticTenantId` is configured. When configured, it creates that one synthetic tenant and only three story-scoped grants: TenantAdmin read/manage and ReadOnly read. No viewer manage grant exists.
- The real-host integration coverage proves admin GET -> PATCH -> confirming GET returns 200, viewer GET returns 200 and PATCH returns 403, an unseeded well-formed actor returns 403, missing actor returns 400, and missing provenance returns 401. These paths use the real tenant registry, PERSON/role seed, RBAC engine, and endpoint.
- The opt-in Playwright specification now exercises the mapped `/administration/tenants` page through signed admin and viewer sessions: admin loads, toggles and confirms persisted state; viewer loads but receives a real 403 and no UI state change. This replaces the obsolete negative-only 401 proof.
- The optional `targetTenant` override is confined to this explicit-target administration page; authorization remains enforced server-side for the requested tenant.

## Independent checks

- Static inspection confirmed the canonical demo PERSON identifiers, signed web-session token, tenant-scoped grants, permission differentiation, and persistence confirmation.
- `td-web-provenance-bridge-route-table.test.ts` passed as part of the frontend unit run. The full unit command ended `544/546` because of the two already-disclosed unrelated BIO-UX-005 registry failures.
- Local .NET execution could not be used as independent runtime evidence in this sandbox: direct execution stalled when the in-process host attempted networking, and `dotnet test` failed at MSBuild named-pipe creation with `SocketException (13): Permission denied`. Builder evidence reports the focused real-host tests and full integration suite passing.

## Non-blocking advisories

1. `BioTen003ReverifyEndpointsTests` currently contains **five** facts, while its comments and builder metadata say four. The results claimed are substantively present, but the evidence count should be corrected.
2. The route matcher uses `prefix('/api/v1/tenants')`, so it also signs unrelated routes such as `/api/v1/tenants/primary-action`. That endpoint ignores the extra headers, so this does not create an authorization bypass or block the required workflow. A precise matcher such as `^/api/v1/tenants/[^/]+/feature-configuration$` would better preserve the route table's documented gated-route-only contract.

No release-blocking defect was found in BIO-TEN-003's tracked bridge re-verification.
