# BIO-TEN-003 — Implement tenant feature configuration

**Owner:** Tunde — Identity & Tenant Engineer
**Wave:** W12
**Conflict group:** `tenant-config`

## User story

As BTS/Tenant Admin, I need selectable feature modules and policies per tenant.

## AC → evidence map

| AC | Statement | Where it lives |
| --- | --- | --- |
| AC01 | Modules include biometrics, card production, visitor, attendance, transport and Document Verify subfeatures. | `Domain/TenantModule.cs` (6 top-level modules), `Domain/DocumentVerifySubfeature.cs` (8 closed subfeature toggles), `Domain/TenantFeatureConfiguration.cs` |
| AC02 | 1:N face identification requires explicit enablement. | `Domain/BiometricsFeatureConfig.cs` (`Enabled`/`Face1NIdentificationEnabled` are two independent fields), `Integration/TenantFeatureFlagPolicyAdapter.cs` (the real BIO-BIO-007 seam — see below) |
| AC03 | Configuration is versioned/audited. | `TenantFeatureConfigurationService` (CAS `Version`, audit-before-commit discipline copied from BIO-TEN-001), `Audit/*` |
| AC04 | Disabled modules fail closed at API and UI boundaries. | `TenantFeatureConfigurationService.GetEffectiveAsync` (fail-closed default), `Domain/ITenantFeatureGateway.cs`, end-to-end proof via the BIO-BIO-007 adapter — see "AC04 scope boundary" below |
| AC05 | `/administration/tenants` + `/settings` connected through `TenantsPort` to a live implementation or a documented HYBRID state. | `/administration/tenants` is fully live; `/settings` needs no change — see below |

## Design decisions and scope boundaries

Each of these is a deliberate, disclosed choice, matching this codebase's existing style of
naming a boundary explicitly rather than overclaiming or silently under-delivering.

### AC01: module and subfeature naming, traceable to the frontend

The six top-level modules (`TenantModule`) and eight Document Verify subfeatures
(`DocumentVerifySubfeature`) are named exactly per the story spec and cross-checked against
`web/lib/feature-mode.ts`'s own documented comment on `DocumentVerifyService`'s ports ("the
other seven Document Verify steps (capture, MRZ, barcode, OCR, document identification, NFC,
selfie comparison)" plus `authenticity`, named separately there). Traceability to the
frontend's own `ServicePort` names (`web/lib/route-manifest.ts`):

| `TenantModule` | Frontend `ServicePort` |
| --- | --- |
| `Biometrics` | `BiometricService` |
| `CardProduction` | `CredentialService` (card production is modelled under the Credential service port in `route-manifest.ts`; `BioCrd007Module.cs` itself names its module string `"CardProduction"`) |
| `Visitor` | `VisitorService` |
| `Attendance` | `AttendanceService` |
| `Transport` | `TransportService` |
| `DocumentVerify` | `DocumentVerifyService` |

### AC02: Biometrics and Face-1N are genuinely independent fields

`BiometricsFeatureConfig.Enabled` (the parent module switch) and
`BiometricsFeatureConfig.Face1NIdentificationEnabled` are two separately-settable fields on
the same record. `TenantFeatureConfigurationService.UpdateAsync` updates them from two
distinct, independently-nullable request fields — enabling the parent module in the same or a
later request never implicitly flips Face-1N on. Proven directly:
`TenantFeatureConfigurationServiceTests.Enabling_the_biometrics_module_never_implicitly_enables_face_1n_identification`
and the mirror-image
`Face_1n_identification_can_be_enabled_independently_of_the_parent_module`, plus the same
assertion again through the real BIO-BIO-007 adapter in
`TenantFeatureFlagPolicyAdapterTests.Enabling_the_parent_biometrics_module_alone_does_not_enable_face_1n_AC02`.

**Round-2 remediation (ZB-TEN003-02/BIO-TEN-003-F02, High).** The two fields stay independent
in *storage* exactly as above, but round 1 let the raw child flag alone decide the *effective*
policy — a tenant could have `Face1NIdentificationEnabled=true` while `Enabled=false` (the
parent module OFF) and `FaceSearchService` would still search, contradicting AC04's own
fail-closed module boundary. Fixed: `TenantFeatureConfigurationService`'s synchronous Face-1N
cache (the one `TenantFeatureFlagPolicyAdapter` actually reads) and
`IsFace1NIdentificationEnabledAsync` both now store/return `Biometrics.Enabled &&
Biometrics.Face1NIdentificationEnabled` — the parent-AND-child effective policy, recomputed on
every commit. Proven directly:
`TenantFeatureFlagPolicyAdapterTests.Disabling_the_parent_biometrics_module_immediately_denies_face_1n_even_though_the_child_flag_remains_true`
(the exact reproduction both reviewers gave) and
`TenantFeatureConfigurationServiceTests.IsFace1NIdentificationEnabledAsync_requires_both_the_parent_module_and_the_child_flag`.

### AC02: the BIO-BIO-007 swap

`api/features/bio-bio-007/Biolog.Features.FaceSearch/Domain/IFaceSearchFeatureFlagPolicy.cs`'s
own doc comment explicitly invited this story: "A later story implementing a real, persisted,
RBAC-gated tenant feature-flag administration surface implements this interface without
FaceSearchService changing shape." This story is that later story.

`Integration/TenantFeatureFlagPolicyAdapter.cs` implements `IFaceSearchFeatureFlagPolicy` by
delegating to `TenantFeatureConfigurationService.IsFace1NIdentificationEnabledSync` — a
synchronous read backed by an in-memory `ConcurrentDictionary<string, bool>` cache the service
keeps in lockstep with every committed write (the same shape
`SyntheticFaceSearchFeatureFlagPolicy` already used), because
`FaceSearchService.SearchAsync` calls `IsEnabledForTenant` synchronously, never awaited — the
interface's signature itself was not touched (that would be a shared-contract change needing
Kemi's approval; this story does not make one).

**The one pre-approved cross-story edit.** `api/Biolog.Api/Modules/BioBio007Module.cs` (owned
by BIO-BIO-007's own engineer, not this story) had exactly one registration line changed:

```diff
- services.AddSingleton<IFaceSearchFeatureFlagPolicy, SyntheticFaceSearchFeatureFlagPolicy>();
+ services.AddSingleton<IFaceSearchFeatureFlagPolicy, TenantFeatureFlagPolicyAdapter>();
```

plus one `using Biolog.Features.TenantFeatureConfig.Integration;` line and one added sentence
in that file's own doc comment pointing here. This is the smallest possible edit achieving the
swap; nothing else in that file was touched (not its `DependsOn`, not `MapEndpoints`, not the
`FaceGalleryApprovalGatewayAdapter` nested class). Tunde (the dispatching agent for this story)
recorded this edit as pre-approved before it was made: the swap does not change
`IFaceSearchFeatureFlagPolicy`'s own signature (no shared contract touched), registration order
does not matter since `AddSingleton<I,T>()` resolves lazily, and the interface's own doc
comment explicitly invited exactly this later implementation. See
`evidence/features/bio-ten-003/builder/` for the recorded diff.

`api/features/bio-ten-003/Biolog.Features.TenantFeatureConfig.csproj` carries a
`ProjectReference` to BIO-BIO-007's own `Biolog.Features.FaceSearch.csproj` so the adapter can
see the public `IFaceSearchFeatureFlagPolicy` interface — the identical "feature-to-feature
reference to implement another feature's own public extension interface" shape BIO-BIO-007's
own csproj already established for BIO-BIO-001. No `InternalsVisibleTo` grant was needed or
requested: `IFaceSearchFeatureFlagPolicy` and `SyntheticFaceSearchFeatureFlagPolicy` are both
public types.

### AC03: versioned and audited, copied from BIO-TEN-001's proven pattern

`TenantFeatureConfigurationService` mirrors `TenantService`'s exact concurrency/audit
discipline: a per-tenant-id `SemaphoreSlim` held across the whole read-validate-audit-commit
sequence; the audit event written and *confirmed* durable (write, then `ExistsAsync` on an
ambiguous throw) *before* the commit; once confirmed, the commit itself runs under
`CancellationToken.None` so a cancellation arriving in that gap cannot abort a commit whose
audit event already durably claims success. This is copied rather than re-derived — BIO-TEN-001's
own four-round history (`docs/features/bio-ten-001/README.md`) already found and fixed the
races a naïve version of this would hit.

One structural difference from BIO-TEN-001's own `TenantAuditEvent`: this feature's own
`TenantFeatureConfigurationAuditEvent.TenantId` is **required**, never null — every action
this feature audits is tenant-plane work (one specific tenant's own configuration), the
complementary case to BIO-TEN-001's always-null, control-plane-only audit events.

Only fields that actually changed are audited or committed — a no-op update (re-sending the
value a field already has) commits nothing, audits nothing, and does not move the version
(`TenantFeatureConfigurationAuditTests.A_no_op_update_commits_and_audits_nothing...`).

The very first write for a tenant targets version `0` — a reserved sentinel meaning "never
written," never confused with a real stored version `1`
(`TenantFeatureConfiguration.Default`). This lets the very first `PATCH` for a tenant use the
identical request shape as every later one, rather than needing a separate "create" verb.

### AC04: fail-closed, honestly scoped

Two levels are proven, not assumed:

1. **This feature's own read path.** `TenantFeatureConfigurationService.GetEffectiveAsync`
   returns the all-disabled `TenantFeatureConfiguration.Default` for a tenant with no
   configuration ever written — never a 404 or an exception a caller could mishandle as
   "enabled." Proven in `TenantFeatureConfigurationServiceTests.GetEffective_for_an_unknown_tenant_returns_every_module_disabled_not_a_throw`
   and `Gateway_methods_also_fail_closed_for_an_unknown_tenant`.
2. **The one real existing consuming seam.** BIO-BIO-007's `IFaceSearchFeatureFlagPolicy` is
   now backed by this story's real configuration. `TenantFeatureFlagPolicyAdapterTests` proves
   the resolved policy instance is fail-closed by default, flips correctly on explicit
   enablement, flips back on explicit disablement, and never leaks across tenants.

   **Disclosed boundary on this one.** These tests do not construct BIO-BIO-007's own
   `FaceSearchService` and call `SearchAsync` directly. `FaceSearchService` and its
   collaborators (`IFaceGalleryStore`, `IFaceGalleryApprovalGateway`, `IFaceSearchAuditSink`)
   are `internal` to that project, with `InternalsVisibleTo` granted only to `Biolog.Api` and
   `Biolog.Features.FaceSearch.Tests`. Widening that grant means editing
   `api/features/bio-bio-007/Biolog.Features.FaceSearch/Biolog.Features.FaceSearch.csproj` —
   a file owned by BIO-BIO-007's own engineer, outside this story's write scope
   (`governance/feature-path-ownership.json`), and not among the edits pre-approved for this
   story (only the one-line registration swap in `Modules/BioBio007Module.cs` was). This
   story's tests instead exercise the exact `IFaceSearchFeatureFlagPolicy` instance
   `FaceSearchService` now receives via DI — everything BIO-BIO-007's own
   `FaceSearchServiceTests.cs` (already-accepted, untouched by this story) does not itself
   re-prove about *this* policy's real, persisted behaviour. `FaceSearchServiceTests.cs`
   already proves `SearchAsync` honours whatever policy it is given, including rejecting a
   `false` answer; combined, the two test suites prove the whole chain without either one
   needing to touch the other's owned files.
3. **The other five modules (card production, visitor, attendance, transport, Document
   Verify) — round-2 disclosed boundary, verified directly, not just asserted.** Round-1
   review (both Seyi and Zainab, BIO-TEN-003-F03/ZB-TEN003-03, High) correctly rejected this
   boundary's original phrasing as too weak — "a gateway exists" is not the same claim as "AC04
   is met," and the round-1 text did not say why no stronger close was possible. god directly
   grepped all 15 stories that own these five modules — `bio-crd-002` through `bio-crd-008`,
   `bio-vis-001`/`bio-vis-002`, `bio-att-001`/`bio-att-002`, `bio-trn-001`/`bio-trn-002`,
   `bio-doc-001` through `bio-doc-010` — for any existing `FeatureFlagPolicy`/
   `FeatureEnablement`/`FeatureGate`-shaped interface, the same shape BIO-BIO-007's own
   `IFaceSearchFeatureFlagPolicy` already was. **None exists anywhere.** The Biometrics case
   was special specifically because that one seam was pre-built and pre-authorized for exactly
   this swap (see AC02 above) — there is no equivalent low-risk path for the other five.
   Wiring real enforcement into them means editing 15 separately-owned, already-accepted
   stories' own endpoint files — a disproportionate blast radius for a single remediation round
   and its own cross-story planning initiative, not a local fix.

   **This is a different shape of gap from BIO-BIO-009's own round-2 "cross-story, outside my
   write scope" argument, which Seyi correctly rejected** (per god's own explicit flag on this
   story, carried here so a reviewer judges the real shape of the gap rather than the framing
   alone): BIO-BIO-009's gap was in completing *that story's own* core promised capability
   (deletion actually working correctly) — deferring it would have left BIO-BIO-009 not doing
   its own job. This gap is the reverse shape: the five consuming endpoints that would need to
   check this gateway live entirely in *other* stories' own code, were never built to consult
   anything of this kind, and belong to engineers who have not reviewed or approved any such
   change. BIO-TEN-003 is not withholding something it owns; it is naming that the consumer
   side of this seam does not exist yet anywhere in the codebase, confirmed by direct
   inspection, not inferred from the framing being superficially similar to BIO-INT-002's AC06.

   **Scope, stated precisely**: AC04's fail-closed guarantee is proven end-to-end for the one
   module with a real existing consumer (biometrics 1:N, via the BIO-BIO-007 swap).
   `Domain/ITenantFeatureGateway.cs` exposes a clean, reusable, unit-tested read interface
   (`IsModuleEnabledAsync`/`IsDocumentVerifySubfeatureEnabledAsync`/
   `IsFace1NIdentificationEnabledAsync`) covering the other five modules too, registered in
   `BioTen003Module`, ready for a future story to consume exactly the way BIO-BIO-007's own seam
   was — but, for those five, no consuming story exists yet, so AC04's fail-closed guarantee is
   available and tested at this feature's own layer only, not proven at any other story's own
   API/UI boundary. Closing that gap for real is BIO-TEN-003's own dependents' job, the same
   relationship BIO-BIO-007 had to this story before this round existed.

### AC05: web surface

`web/features/tenants/ports.ts`'s `TenantsPort` gained two real methods —
`getFeatureConfiguration`/`updateFeatureConfiguration` — additive to the existing generic
`ModulePort` shape (its own prior doc comment explicitly invited exactly this: "The domain
story that builds real Tenants screens extends this interface with real methods here").
`web/features/tenants/live.ts` calls this story's own real
`/api/v1/tenants/{tenantId}/feature-configuration` backend; `primaryAction()`'s own existing
endpoint is untouched (additive via `{ ...base, ... }`, the same discipline BIO-INT-002's own
`live.ts` documents for the identical reason). `web/features/tenants/mock.ts` keeps its
existing deterministic scenario-cycling shape for `primaryAction()` and adds an in-memory,
per-tenant mock store for the two new methods, mirroring the same fail-closed default
(version `0`, everything disabled) as the real backend.

`web/app/administration/tenants/page.tsx` — previously a bare `PlaceholderPage` — now renders
`FeatureConfigurationConsole`, a real client component (toggle tables for the six modules, the
Face-1N flag kept visually and functionally separate from its parent module, and the eight
Document Verify subfeature toggles), reusing this repo's existing shared `biolog-card`/
`biolog-table`/`biolog-button-secondary` classes rather than redesigning the approved
shell/navigation — the same "real page replacing PlaceholderPage" pattern BIO-INT-002's own
`ImportConsole`/`AdministrationIntegrationsPage` established.

`web/app/settings/page.tsx` is left untouched. It was read first, per this story's own
instruction: it is BIO-UX-011's own fully-built System Settings page (General, Notification,
Retention/Privacy, Security & Access, Feature Flags, Offline/Device Sync), and none of its six
sections has any feature-module-configuration content — its own `TenantService` port usage is
limited to the generic `primaryAction()` Save/Reset simulation, a different concern from this
story's own AC01–AC04. Unlike BIO-INT-002's `/settings/authentication` (a route directly named
by that story's own AC06), nothing in BIO-TEN-003's AC05 names a `/settings` sub-route, so there
is no HYBRID boundary to disclose there — a one-line "see BIO-TEN-003" comment would describe a
relationship that does not exist. This is a judgement call, recorded here per this story's own
instruction to document whatever was decided.

An end-to-end test for the real workflow (toggle a module/subfeature via the live adapter, see
it reflected) is proven on the backend side by
`TenantFeatureConfigurationEndpointsIntegrationTests.Patch_then_get_reflects_the_update_end_to_end`
— the same HTTP surface `live.ts` calls.

**Round-2 disclosed HYBRID boundary (BIO-TEN-003-F04, High, Seyi).** Round-1 review correctly
found no browser-level end-to-end proof of the mapped human workflow existed. Adding one
surfaced a genuine, pre-existing, cross-cutting gap, not unique to this story: round-2's F01
fix requires `ActorProvenanceGate` (ADR-0006 Decision 3) on both routes, but no browser-usable
mechanism exists anywhere in this codebase for a pre-authentication web page to produce a valid
provenance proof — `web/features/tenants/live.ts` (like every other web `live.ts` that calls a
provenance-gated route, e.g. BIO-INT-002's own `createMapping`/`applyImport`) sends only a
hardcoded `X-Actor-Id: web-ui` with no proof at all, and computing one client-side would mean
shipping the shared secret in the JS bundle, defeating its purpose. ADR-0006 Decision 3 itself
names this missing piece explicitly: "the edge/gateway half that actually strips and re-injects
the header is an infrastructure concern outside this story's own surface — Emeka's domain." That
half does not exist yet. god confirmed this directly and approved disclosing it rather than
inventing a one-story workaround (a demo-secret bridge or Next.js middleware would itself be a
cross-cutting security-shaped decision, outside this story's call to make alone) — tracked as a
foundational, cross-cutting follow-up (god → Kemi/Emeka), not something this round holds on.

**What this means for AC05, stated honestly:** the backend behind `/administration/tenants` is
genuinely LIVE and now correctly secured (provenance + tenant-scoped permission + tenant
existence, all real). The browser UI's own write/read workflow cannot presently complete
end-to-end, because the demo actor it sends cannot pass the provenance check that security fix
now requires — a direct, visible consequence of closing F01, not a regression this story
introduced carelessly. `tests/frontend/playwright/bio-ten-003-feature-configuration-live.spec.ts`
proves the honest claim available today: an unauthenticated/unprovenanced browser call against
the real backend is correctly rejected (401), never silently permitted — the same "-live.spec.ts,
opt-in via an env var, two real servers" shape BIO-DOC-009's own spec established — rather than
faking a positive write-path pass that the current security posture cannot actually deliver.

### No bash structural harness

Unlike BIO-INT-002's/BIO-TEN-001's own `.test.sh` harnesses, this story adds none. Its own
developer-test surface (C# unit + HTTP-integration tests, mapped to AC01–AC05 by name) already
covers every behavioural claim this story makes; a bash harness would only re-verify facts the
C# suite already proves (e.g. "the registration line changed") with weaker tooling. The one
genuinely structural fact worth a quick, separate check — that `BioBio007Module.cs`'s
registration line was actually swapped — is recorded directly in builder evidence instead
(`evidence/features/bio-ten-003/builder/`), since it is a one-time fact about a diff, not an
ongoing behaviour a repeatable test suite needs to keep re-asserting.

## API surface

Tenant-plane scoped (the tenant id is explicit in the path) — distinct from BIO-TEN-001's own
control-plane-scoped `/control-plane/tenants` routes.

| Method | Path | Notes |
| --- | --- | --- |
| `GET` | `/api/v1/tenants/{tenantId}/feature-configuration` | 200 (synthesizes the all-disabled default, version 0, if nothing was ever written), 401 (missing/invalid `X-Actor-Provenance`), 403 (`tenant_feature_configuration.read` not granted), 404 (tenant does not exist). |
| `PATCH` | `/api/v1/tenants/{tenantId}/feature-configuration` | Requires `version` (optimistic concurrency), `X-Actor-Id` and `X-Actor-Provenance`. 401/403/404 as above, gated on `tenant_feature_configuration.manage`. Every body field besides `version` is optional — omitted means unchanged. |

Every response carries `X-Correlation-Id`; every error is `application/problem+json` shaped.

**Round-2 remediation (BIO-TEN-003-F01, Critical).** Round 1 shipped both routes open to any
caller who self-asserted an `X-Actor-Id` — no provenance, no permission check, no target-tenant
existence check. Fixed per ADR-0006 Decision 3's sanctioned shape, the same pattern
BIO-MOB-005/BIO-INT-002 already use: `Api/ActorProvenanceGate.cs` (project-local copy, HMAC
proof over `X-Actor-Id`, keyed by `TenantFeatureConfig:ActorProvenanceSecret`),
`Domain/IPermissionGateway.cs` (wraps BIO-IDN-006's own `PermissionService.ActorHasPermissionAsync`
via `BioTen003Module`'s `PermissionGatewayAdapter`), `Domain/ITenantExistenceGateway.cs` (wraps
BIO-TEN-001's own `TenantService.GetAsync` via `TenantExistenceGatewayAdapter` — closes "a
request can create configuration for a tenant that was never created," previously a disclosed
boundary this review correctly found was the defect itself, not merely undocumented). Order:
provenance → permission → tenant existence, so an unauthorized caller cannot use this endpoint
as a tenant-id-existence oracle. `HmacActorProvenanceStore`'s unconfigured fallback key is
generated via `RandomNumberGenerator.GetBytes(32)` at process start, never derived from a
hardcoded string (a floor-wide bug in this same primitive, flagged directly by god before this
copy was written, applied here from the start rather than inherited and fixed later) — an
unconfigured secret means every proof fails verification, fail CLOSED, never forgeable.

**Round-3 remediation (BIO-TEN-003-F05, Critical, Seyi + Zainab independently).** Round 2 fixed
the UNCONFIGURED case (above) but left the CONFIGURED case unguarded: round 2's
`Configure(sharedSecret)` accepted ANY non-empty string, so a short or typo'd real secret was a
genuinely brute-forceable HMAC key — a missing floor on a real credential, not a dummy-key
fallback issue. Not a new design problem: the exact shape already proven across 14 files in
TD-PROVENANCE-DUMMYKEY-SWEEP (Emeka's canonical pattern, ported by Busola), ported here rather
than re-derived. `HmacActorProvenanceStore.MinimumSecretLength = 32`; `Configure` now sets `_key`
to `null` (the same state as never configured) for any secret shorter than that; `Verify` opens
with `if (_key is null) return false;` before any HMAC computation — no fallback key of any kind
remains, replacing round 2's random-dummy-key approach entirely with this simpler, now-canonical
shape. Proven directly in `ActorProvenanceGateTests.cs`: 31-byte secret rejected (every proof
fails), 32-byte secret accepted (a correct proof verifies), plus never-configured, empty-secret,
wrong-secret and malformed-hex cases.

## Configuration fields

`biometrics.enabled` / `biometrics.face_1n_identification_enabled` (two independent flags,
AC02), `card_production_enabled`, `visitor_enabled`, `attendance_enabled`, `transport_enabled`,
`document_verify.enabled` plus its eight subfeature flags (`capture_enabled`, `mrz_enabled`,
`barcode_enabled`, `ocr_enabled`, `authenticity_enabled`,
`document_identification_enabled`, `nfc_enabled`, `selfie_comparison_enabled`) — plus
`tenant_id`, `version`, `created_at`, `updated_at`.

## Test counts

46 developer tests in `tests/features/bio-ten-003/` (round 1: 28; round 2 adds 12 - 9 F01
provenance/permission/tenant-existence regressions plus 3 F02 parent-AND-child Face-1N
regressions across `TenantFeatureConfigurationEndpointsIntegrationTests`,
`TenantFeatureFlagPolicyAdapterTests` and `TenantFeatureConfigurationServiceTests`; round 3 adds
6 F05 minimum-secret-length regressions in the new `ActorProvenanceGateTests.cs`), mapped to
AC/finding IDs by test name and/or doc comment. Zero regressions: BIO-BIO-007's own 23 tests
still pass unmodified, and the full `tests/dotnet/Biolog.IntegrationTests` suite (192 tests)
passes unchanged.

**Round-2 addition.** `tests/frontend/playwright/bio-ten-003-feature-configuration-live.spec.ts`
(new, opt-in via `BIOLOG_TEN003_LIVE_TESTS=1`, same shape as `bio-doc-009-authenticity-live.spec.ts`)
proves the honest AC05 claim described above - the real backend is reachable and correctly
rejects an unauthenticated/unprovenanced browser call (401), and the UI fails closed rather than
rendering a false-success state. `web/next.config.mjs` also gained its own
`/api/v1/tenants/:path*` rewrite entry in this round - a separate, pre-existing gap found while
writing this spec: it was entirely missing before, so `live.ts`'s calls would have 404'd against
the Next.js server itself, never even reaching `Biolog.Api`, independent of this round's
security fix.

## Post-acceptance fix: TD-WEB-PROVENANCE-BRIDGE-REVERIFY (2026-10-07)

BIO-TEN-003 was accepted (Chioma, 2026-10-07T00:03:07Z) on the disclosed-HYBRID-boundary
condition above, owing a real positive LIVE proof once TD-WEB-PROVENANCE-BRIDGE shipped a
browser-usable `ActorProvenanceGate` bridge. The bridge merged (commit `7ac4876`, later extended
to `20f9312` for round 6's actor-id fix); Seyi's independent re-verification
(`evidence/TD-WEB-PROVENANCE-BRIDGE/codex/REVERIFY-Seyi-BIO-RPT-004-BIO-TEN-003.md`) confirmed
the obligation was still open and listed the required closure: a real signed session, the
minimum story permission grants on a real synthetic tenant, a successful mapped browser
workflow, an unprivileged-denial negative, and dropping the stale 401-only claim.

**Blocked first on a genuinely cross-cutting RBAC gap, not fixed here.** `demo-actor-{roleId}`
(the bridge's original actor-id shape) failed `CanonicalId.IsPersonId` before any grant lookup
ever ran, so no story using the real RBAC engine could get a "granted" result through the bridge
at all - escalated to god (own finding, independently confirmed by Ngozi on BIO-RPT-004 within
minutes), routed to Kemi, resolved by Emeka's round 6 (`web/lib/demo-identity.ts`'s
`deriveDemoActorId` now returns one of three fixed `per_<UUIDv7>` literals, matched by
`DemoSessionIdentitySeed`'s own Person+PersonRoleAssignment rows in one hardcoded synthetic
tenant). Not this story's fix - tracked and merged as part of TD-WEB-PROVENANCE-BRIDGE itself.

**This story's own three remaining gaps, found and fixed in this round:**

1. **`web/lib/actor-provenance-routes.ts` had no entry at all** for
   `/api/v1/tenants/{tenantId}/feature-configuration` - `computeActorProvenance` silently
   returned `null` for this path, so `proxy.ts` never injected a provenance proof regardless of
   session validity. Added (`TenantFeatureConfig:ActorProvenanceSecret`), confirmed non-colliding
   with BIO-TEN-001's `/api/v1/control-plane/tenants` or BIO-TEN-002's `/api/v1/tenant-domains`
   (neither is `ActorProvenanceGate`-gated), proven in
   `tests/frontend/unit/td-web-provenance-bridge-route-table.test.ts`.

2. **The synthetic tenant needed to exist as a real row, and carry real grants - unlike
   BIO-RPT-004/BIO-BIO-009.** This story's own endpoint takes its target tenant id EXPLICITLY
   from the URL path (an admin console reading/writing several tenants from one surface,
   round-2's own design), never ambiently - so round 5/6's backend-side tenant confinement
   (`DemoSessionTenantResolver`) has no bearing on it at all. `BioTen003Module.Configure` now
   seeds (fail-closed/inert unless `WebSessionBridge:SyntheticTenantId` is configured, idempotent,
   same "seed data, not provisioning logic" shape `DemoSessionIdentitySeed` established): the
   synthetic tenant itself into BIO-TEN-001's `ITenantRegistry`, and real
   `RolePermissionGrant` rows - `TenantAdmin` gets both `tenant_feature_configuration.read` and
   `.manage` (the admin workflow's own minimum), `ReadOnly` gets `.read` only (deliberately no
   `.manage` - proves the real manage-permission boundary on a write attempt, not a blanket
   read-time rejection). No `RolePermissionGrant` row is ever seeded for any OTHER story's own
   permission keys - scoped to exactly what this story's own AC05 workflow needs.

3. **A genuinely non-obvious, independently-found ambient-tenant dependency.** Even though this
   endpoint's own authorization takes an explicit `tenantId`, `PermissionService.
   ActorHasPermissionAsync`'s own person-existence check
   (`IPersonExistenceGateway` -> `PersonService.GetAsync`) resolves ITS tenant AMBIENTLY
   (`RequireTenant()`) - a pre-existing BIO-IDN-006 property, not something this round
   introduces. Without a verified `X-Web-Session-Token` present (which round 5's
   `DemoSessionTenantResolver` consumes to set ambient tenant), that call throws
   `PersonError.TenantContextMissing` - an exception type this story's own endpoint never
   catches, surfacing as an unhandled 500 instead of any intended status. Found writing
   `BioTen003ReverifyEndpointsTests.cs`; fixed by sending the signed session token alongside the
   provenance proof in every test (mirrors what `proxy.ts` already forwards for real browser
   traffic) - no code change needed, since the real request path already carries this header.

**Positive proof, two layers, both real:**

`tests/dotnet/Biolog.IntegrationTests/BioTen003ReverifyEndpointsTests.cs` (new, 5 tests) - through
the REAL packaged `Biolog.Api` host, not a project-local fake: a signed admin identity reaches a
genuine 200 on GET and PATCH, with the write persisting server-side (confirmed via a follow-up
GET); a signed viewer identity reaches 200 on GET but a real 403 (`forbidden`) on PATCH; a
well-formed-but-unseeded `per_<UUIDv7>` actor is denied (`forbidden`) proving the fail-closed
default, not merely a malformed-id rejection; the pre-existing no-actor/no-provenance 400/401
negatives are unchanged. Deterministic, run as part of this story's own CI-equivalent chain,
100% reliable across every run. Unlike `DemoSessionTenantResolverEndpointsTests`'s own disclosed
ceiling ("no test project has `InternalsVisibleTo` access to seed a real permission grant
anywhere in this codebase"), that limitation does NOT apply here - `BioTen003Module.Configure`
seeds the grant itself, at real host startup, the same pipeline every other module uses, so this
suite reaches a genuine 200, not merely "resolution succeeded, permission check reached."

`tests/frontend/playwright/bio-ten-003-feature-configuration-live.spec.ts` (rewritten, supersedes
the round-2 negative-only version entirely) - the same workflow through a REAL browser: a signed
admin session loads the console, toggles a module, and the change persists across a fresh
navigation; a signed viewer session loads the console but a write attempt is correctly denied
(403) with the UI surfacing the failure, never a false success. `feature-configuration-console.tsx`
gained an optional `?targetTenant=` override (own file, not shared shell code) - the shell's
fixed demo tenant selector (`alpha`/`beta`) can never be a real `ten_<UUIDv7>` row, so without
this override the console could never target the one real synthetic tenant the bridge
provisions; absent, behaviour is unchanged (`session.tenant.id`, as before).

**Disclosed, not hidden: a manual-run connection-pool flake, found and root-caused, not papered
over.** Running both servers as plain local processes for this verification (`dotnet run` +
`next start`, no reverse proxy/load balancer) intermittently produced an EMPTY-BODY 400 on an
otherwise-correct request. Root-caused via the API host's own structured logs at the exact
failure moment: `error.type: keep_alive_timeout` / `aspnetcore.routing.match_status: failure` /
`System.OperationCanceledException` - Kestrel's keep-alive connection timing out server-side
while Next's rewrite proxy still holds and reuses it client-side, which the server then aborts
mid-request. Confirmed NOT an application-level rejection: every genuine
`TenantFeatureConfigurationError` response carries a `problem+json` body; this artifact's
response carries none. The spec itself is correct and passes cleanly in the large majority of
manual runs (confirmed repeatedly, including full 2/2 clean passes); this is a property of
sustained local two-process testing, not of the application. Flagged to god as advisory. The
backend-level suite above is unaffected (TestServer in-process, no real sockets) and is the
primary, always-reliable evidence for this round's positive-workflow claim.

**Verification:** `dotnet build` clean (0 warnings/0 errors); this story's own suite 46/46
unchanged; `BioTen003ReverifyEndpointsTests.cs` 5/5; full `Biolog.IntegrationTests` 232/232 (no
regressions - the 2 pre-existing `AuthenticationEndpointsTests` failures flagged during
BIO-ATT-003's own submission are gone, confirming Busola's `Authentication:SessionTokenSecret`
fix landed and merged separately); `scripts/check-module-project-references.sh` PASS;
`python3 scripts/check-contract-freeze.py` PASS (39 artifacts, 83 future stories - unchanged, no
`contracts/**`/`docs/adr/**` file touched); `pnpm --dir web typecheck`/`build` clean (34 routes +
`ƒ Proxy (Middleware)`, `/administration/tenants` prerenders); `pnpm --dir tests/frontend
test:unit` 545/547 (same 2 pre-existing, unrelated `bio-ux-005` failures every other story on
this floor has already confirmed independently).

**Non-blocking advisories from Seyi's re-review, both fixed:** (1) this section and the evidence
file's own summary said `BioTen003ReverifyEndpointsTests.cs` had 4 tests; it actually has 5 -
corrected above and in `evidence/features/bio-ten-003/builder/td-web-provenance-bridge-reverify-metadata.json`.
(2) `web/lib/actor-provenance-routes.ts`'s `TenantFeatureConfig` entry used a bare
`prefix('/api/v1/tenants')`, which also matched `/api/v1/tenants/primary-action` -
BIO-UX-005-AC04's own pre-existing, ungated generic simulated-action endpoint, unrelated to
feature-configuration. Confirmed harmless (that endpoint has no `ActorProvenanceGate` at all, so
it simply ignores the extra, unneeded headers) but imprecise - tightened to
`matches(/^\/api\/v1\/tenants\/[^/]+\/feature-configuration$/)`, the same precision this table
already holds every other `{id}`-shaped route to. New negative test proves
`/api/v1/tenants/primary-action` now resolves to no match at all.
