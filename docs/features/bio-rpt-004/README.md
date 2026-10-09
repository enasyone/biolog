# BIO-RPT-004 — Dashboards and operational health views

As operators, a simple-first dashboard surfaces identity, card, device, sync, access and
production state so day-to-day health is visible without running a report.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Dashboard metrics come from authorised tenant scope | `DashboardService.GetSnapshotAsync` resolves the tenant itself from `ICurrentTenantProvider` (server-side, never request-supplied), calls three narrow gateways scoped to that one tenant id, and — since Seyi round 1 (F01) — requires the requesting actor to hold `dashboard.view_operational_health` via a real `IDashboardPermissionGateway` check BEFORE any gateway is ever called | `DashboardServiceTests` (AC01/F01 tests), `DashboardEndpointsIntegrationTests` (AC01/F01 tests) |
| **AC02** | Drill-down links preserve role permissions | `OperationalHealthPanel`'s drill-down links reuse the shell's own `requiredPermission`/`isNavItemVisible` mechanism against the SAME destination route the sidebar already gates | `bio-rpt-004-operational-health.spec.ts` (AC02 tests, admin/staff/viewer) |
| **AC03** | Stale/offline data is labelled | Every panel carries an explicit `DataFreshness` with THREE distinct states (`live`/`stale`/`unavailable`, since Seyi round 1 F02) plus its own `ObservedAt` timestamp. A real source whose read just failed is `Stale` (counts zeroed, reason stated) — isolated per panel, never crashing the whole snapshot. The three domains with no real backing source at all (Card/Device/Sync) are `Unavailable` with a stated `Reason` — a structurally different, permanently distinct state and badge tone from `Stale` | `DashboardServiceTests`/`DashboardEndpointsIntegrationTests` (AC03/F02 tests), `bio-rpt-004-operational-health.spec.ts` (F02 test) |
| **AC04** | No cross-tenant aggregation is exposed to tenant roles | `DashboardService` has no method/overload that accepts a tenant id parameter at all — there is no code path, for any role, that can request a second tenant's data through this feature | `DashboardServiceTests` (AC04 tests, incl. a reflection-based structural proof) |
| **AC05** | The approved web surface (`/reports`, `/audit`, `/dashboard`) is connected through `ReportingService` to this story's live implementation or an explicitly documented HYBRID state, and the mapped end-to-end human workflow passes without an unapproved UI redesign | A new dedicated `ReportingService` port key (`operationalHealth`), mounted on the shell's existing Dashboard route (`/`) alongside BIO-UX-011's own metrics tiles — additive, not a redesign. Three of six panels are genuinely LIVE; three are an honestly disclosed HYBRID gap (see "Which sources are wired, and why" below) | `DashboardEndpointsIntegrationTests`, `bio-rpt-004-operational-health-mock.test.ts`, `bio-rpt-004-operational-health.spec.ts` (SIMULATION), `bio-rpt-004-operational-health-live.spec.ts` (LIVE, gated) |

## Design decisions and scope boundaries

### `governance/ui-integration-map.json` does not exist — `route-manifest.ts` is the source of truth

Same pre-existing, already-documented gap every sibling BIO-RPT/BIO-UX story has flagged
(`docs/features/bio-ux-003/README.md`, `docs/features/bio-ux-010/README.md`,
`docs/features/bio-rpt-003/README.md`): the file is referenced but was never committed.
`web/lib/route-manifest.ts`'s `ROUTE_MANIFEST` already carries the de-facto mapping for all
three AC05-named routes — `dashboard` → `/` (reconciled from the map's stale `/dashboard`
reference, the same resolution BIO-UX-003/BIO-RPT-003 already made — **not creating a second
`/dashboard` page is the now-twice-precedented resolution, applied a third time here**),
`reports` → `/reports`, `audit` → `/audit`. All three already name `servicePort: 'ReportingService'`.

### Which sources are wired, and why

Following BIO-RPT-003's own precedent exactly ("wire only already-public, already-exposed
sources, don't retrofit new coupling"): this story reads from whichever already-accepted
sibling features expose a tenant-wide list method today, and discloses — rather than silently
fabricates — the three that don't.

**Wired (real, LIVE):**

- **Identity** — `BIO-IDN-001`'s `IPersonRepository.ListByTenantAsync` (already `internal`,
  already grants `Biolog.Api` access via `InternalsVisibleTo`). Counts by `PersonStatus`
  (active/suspended/other).
- **Access** — `BIO-ACC-002`'s `IAccessDecisionEventRepository.ListForTenantAsync` (same
  `internal`+`InternalsVisibleTo` shape). Counts by `AccessDecisionOutcome` (allowed/denied).
- **Production state** — `BIO-CRD-007`'s `IProductionJobRepository.ListForTenant` — PUBLIC, the
  exact same already-public method `BIO-RPT-002`'s own `ICardProductionGateway` independently
  wraps for a different dimension. Counts by `ProductionJobStatus`.

**Disclosed HYBRID gap (honestly `Simulated`, never a fabricated number):**

- **Card** — `BIO-CRD-005`'s `ICardIssueRepository` and `BIO-CRD-006`'s
  `ICardLifecycleRepository` expose no tenant-wide list method (verified directly, not assumed —
  neither interface declares anything resembling `ListByTenant`/`ListForTenant`).
- **Device** — `BIO-DEV-001`'s `IDeviceRepository` has no tenant-wide list method either, and
  `Device.LastSeenAt` is a nullable field that is set to `null` at creation and never written by
  any existing code path anywhere in the host (`web/features/devices/live.ts`'s own doc comment
  independently confirms the same gap: "a tenant-scoped device-list endpoint doesn't exist yet").
- **Sync** — `BIO-MOB-003` (Offline Sync) exposes no tenant-wide read of pending/failed offline
  mutations.

Each of these three is NOT silently skipped or faked — `DashboardService` always returns them as
an explicit `UnavailablePanel { Freshness = Simulated, Reason = "..." }` naming exactly which
dependency and which missing method is the reason, so an operator (and a future story that does
add the missing read) can tell precisely what changed. This story does not itself add a
tenant-wide list method to any of the three dependencies — that would be a cross-story-ownership
change outside this story's own scope, not a unilateral addition into another story's owned files.

### AC02's drill-down permission mechanism is the SAME one the sidebar already uses, not a second one

`OperationalHealthPanel`'s `DRILL_DOWNS` map reuses `web/lib/permissions.ts`'s
`isNavItemVisible` against the identical `requiredPermission` string
`web/lib/nav-items.ts` already assigns to the destination route (`nav:people`, `nav:cards`,
`nav:devices`, `nav:access-control`). A role that cannot see "Devices" in the sidebar cannot see
this panel's "View devices" link either, by construction — there is no independently-maintained
second permission list that could drift out of sync with the sidebar's own. Production's
drill-down also targets `nav:cards` (there is no separate "production" nav item in this shell;
production state is part of the card/credential domain) — a disclosed, deliberate reuse, not an
oversight. Sync has no destination route/nav item in this shell at all, so it renders no
drill-down — a metric-only tile has nothing a permission gate would need to hide.

### AC04 is a structural guarantee, not merely a behavioural one

`DashboardService` has exactly one public method, `GetSnapshotAsync(CancellationToken)` — no
overload anywhere on the class accepts a tenant id. `DashboardServiceTests`'s
`AC04_there_is_no_overload_anywhere_on_DashboardService_that_accepts_a_tenant_id_parameter` test
proves this by reflection, the same "prove it structurally, not just behaviourally" discipline
BIO-RPT-003's own read-only/no-mutation-route tests apply. There is consequently no code path,
for any role, that can even ask this service for a second tenant's data.

### No new ADR-0001 canonical identifier type

This feature mints no durable aggregate of its own — only a read-time projection over
already-governed data from three sibling features. `Identifiers/CanonicalId.cs` here carries only
the `evt_` correlation-id pattern (for the `X-Correlation-Id` header), the same minimal subset
BIO-RPT-002/003 each keep.

### Read-only by construction

`DashboardEndpoints.Map` registers exactly one `MapGet` — no `MapPost`/`MapPut`/`MapPatch`/
`MapDelete` of any kind exists in this feature. `DashboardEndpointsIntegrationTests`'s
`AC02_no_mutation_route_exists_under_this_prefix` test proves this at the real HTTP boundary too,
mirroring BIO-RPT-003's identical test for its own read-only audit surface.

## Deliberately out of scope for this story

- Adding a tenant-wide list method to `BIO-DEV-001`/`BIO-CRD-005`/`BIO-CRD-006`/`BIO-MOB-003` —
  a cross-story-ownership change, not something this story makes unilaterally. Card/Device/Sync
  stay an honestly-disclosed HYBRID gap (AC05's own explicit allowance) until one of those
  stories adds the missing read itself.
- A per-device "last seen"/staleness computation — `Device.LastSeenAt` is never populated by any
  existing code path, so there is nothing time-based to compute yet; the Device panel's
  `Simulated` freshness already communicates "no live data," which is the more honest signal
  than inventing a fake "N minutes ago" value.
- A second `/dashboard` route — the shell's existing `/` IS the dashboard (BIO-FND-007/BIO-UX-002/
  BIO-UX-003/BIO-RPT-003 all already resolved this identically).
- RBAC enforcement on the new `/api/v1/dashboard/operational-health` endpoint itself — this
  mirrors BIO-RPT-002's own `ReportEndpoints` (no permission check on its GET routes either); the
  permission boundary this story implements is AC02's client-side drill-down visibility, which is
  what the AC actually asks for ("drill-down links preserve role permissions," not "the metrics
  endpoint is itself permissioned").

## Proving AC05

```bash
# 1. Real backend
dotnet build api/Biolog.Api/Biolog.Api.csproj --nologo
dotnet run --project api/Biolog.Api/Biolog.Api.csproj --no-build --urls http://127.0.0.1:5091 &

# 2. Register + verify a tenant domain for the host the test runs against (BIO-TEN-002)
curl -X POST http://127.0.0.1:5091/api/v1/control-plane/tenant-domains \
  -H "Content-Type: application/json" -H "X-Actor-Id: web-ui" \
  -d '{"hostname":"127.0.0.1","tenant_id":"ten_00000000-0000-7000-8000-0000000000f1"}'
curl -X PUT http://127.0.0.1:5091/api/v1/control-plane/tenant-domains/127.0.0.1/status \
  -H "Content-Type: application/json" -H "X-Actor-Id: web-ui" \
  -d '{"version":1,"status":"VERIFIED"}'

# 3. LIVE-built Next.js server (NEXT_PUBLIC_* baked in at build time, not runtime)
(cd web && rm -rf .next && \
  NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_OPERATIONALHEALTH=LIVE \
  BIOLOG_API_BASE_URL=http://127.0.0.1:5091 pnpm exec next build)
(cd web && BIOLOG_API_BASE_URL=http://127.0.0.1:5091 pnpm exec next start -p 3213 -H 127.0.0.1 &)

# 4. The real dual-server LIVE Playwright proof
(cd tests/frontend && BIOLOG_RPT004_LIVE_TESTS=1 BIOLOG_BASE_URL=http://127.0.0.1:3213 \
  pnpm exec playwright test bio-rpt-004-operational-health-live.spec.ts --workers=1)
```

## Round 2 — Seyi review findings fixed

Seyi FAILed round 1 (commit `da9f1fa`) with two High findings:

- **F01 (AC01):** tenant scoping alone is not authorization — the endpoint had no actor/permission
  gate, so any caller in the tenant's host could read the snapshot regardless of role. Fixed by
  adding `IDashboardPermissionGateway` (wraps BIO-IDN-006's real `PermissionService.HasPermissionAsync`,
  the same dependency/wrapping shape BIO-RPT-003's own `IAuditExportPermissionGateway` already
  established) and a new permission key `dashboard.view_operational_health`. `DashboardService`
  now requires `X-Actor-Id` and checks this permission BEFORE calling any health gateway — proven
  both at the service level (a `FakePermissionGateway`, with a call-count assertion that the
  identity gateway is never even invoked for a denied actor) and at the real HTTP boundary (403
  for a denied actor, 400 for no actor at all).
- **F02 (AC03):** live panels were unconditionally `Live` with no timestamp/offline signal, and
  the three source-less domains were mislabelled `Stale` (conflating "no source exists" with "a
  real source's data is old/degraded"). Fixed by splitting `DataFreshness` into three states —
  `Live`, `Stale` (a real source whose most recent read genuinely failed — each of the three real
  gateway calls is now wrapped individually, so one failing source degrades only its own panel,
  never the whole snapshot), `Unavailable` (Card/Device/Sync, permanent, never the same value or
  UI tone as `Stale`) — and every panel now carries its own `ObservedAt` timestamp, not just one
  snapshot-level one. The frontend renders three visually distinct badge tones (success/warning/
  neutral).

**Self-caught during round 2:** the new per-panel reason text (a long, slash-joined dependency
name with no natural break point) blew out the `.biolog-metrics-grid`'s `minmax(10rem, 1fr)`
grid track at mobile width — a classic CSS grid min-content overflow, not caught by the round-1
Playwright run since round 1's reason strings happened to be short enough. Fixed with a new,
narrowly-scoped `.biolog-health-reason { overflow-wrap: anywhere }` class (not an edit to the
shared `.biolog-empty-state`/`.biolog-card` classes other stories rely on). Confirmed via the
`dashboard-mobile-light` visual-regression screenshot before and after.

## Round 3 — Seyi round-2 review findings fixed

Seyi FAILed round 2 (commit `b0f62e0`) with two further findings:

- **F03 (Critical, AC01):** round 2's real permission check trusted a raw, caller-asserted
  `X-Actor-Id` directly - a real RBAC grant does not make a caller-controlled principal
  trustworthy. god's instruction was explicit: build on the CURRENT canonical ADR-0006 Decision 3
  pattern (BIO-INT-003 commit `c5e2f9d`, `MinimumSecretLength`/`_key is null => false`, no
  fallback key), not the earlier, superseded dummy-key version several other sibling stories
  still carry. Hand-ported `ActorProvenanceGate.cs` feature-locally (the same per-feature
  copy-not-shared-library discipline every sibling gate uses), wired into `DashboardEndpoints`
  BEFORE the permission check. Proven at the real HTTP boundary: no proof at all, a bogus proof,
  and a proof computed for a different actor id are all refused with 401; a separate test proves
  the GATE ORDER itself (an unproven, unprivileged actor gets 401 `actor_provenance_required`,
  never 403 `dashboard_view_forbidden` - the provenance check never even reaches the permission
  check). No frontend live port anywhere in this codebase ships a real provenance secret to the
  browser (verified directly) - the LIVE Playwright spec now proves the real backend genuinely
  refuses the demo session with 401, the honest, disclosed consequence of BIO-INT-001's own
  pre-auth state, mirroring BIO-RPT-003's identical precedent for its own permission gate.
- **F04 (High, AC03):** a failed gateway read fabricated a zero-valued `Stale` panel stamped with
  the failure's own time - indistinguishable from "this tenant genuinely has zero people/
  decisions/jobs right now." Added `LastKnownHealthCache` (process-lifetime singleton, the same
  "must survive past any one request's own scope" reasoning as `ReportJobRunner`): every
  successful read records its counts and true observation time per tenant per domain; a failed
  read now retains and returns THOSE values and THAT timestamp instead of a fresh fabricated
  zero. Panel counts are now nullable end to end (domain records, wire DTOs, frontend types) - a
  tenant that has NEVER had a successful read reports no counts at all (`null`), never a zero.
  The frontend labels retained values "Last known" and renders a never-observed source as an
  explicit empty state, never a blank zero.

**Verified (round 3):** backend tests 30/30 (up from 22, +8 new F03/F04 tests), `dotnet build`
clean, structural harness 45/45 (up from 37), `tsc --noEmit` clean, frontend vitest 470/470
(unchanged - mock mode is unaffected by either finding), targeted SIMULATION Playwright spec 7/7,
and the dashboard-touching shell/axe/visual-regression specs re-checked (one unrelated flake on
`/settings` confirmed pre-existing via isolated re-run).

## Verified

- `dotnet build api/Biolog.Api/Biolog.Api.csproj` clean.
- `dotnet test tests/features/bio-rpt-004/Biolog.Features.OperationalDashboard.Tests.csproj`:
  11/11 passing.
- `bash tests/features/bio-rpt-004/BIO-RPT-004.test.sh`: 31/31 passing.
- `tests/frontend`: `node node_modules/typescript/bin/tsc --noEmit` clean; the full
  `vitest run` suite (63 files, 469 tests, including the new
  `bio-rpt-004-operational-health-mock.test.ts`) passing; the full default Playwright suite
  (`playwright test`) passing with only two pre-existing, unrelated flakes confirmed by isolated
  re-run (`bio-crd-002-card-designer.spec.ts`'s own pre-existing "Back" tab locator-ambiguity bug,
  and `bio-ux-015-product-experience-gate.spec.ts`'s `administration-integrations` route) — neither
  touches any file this story owns.
- One real, self-caught regression, fixed before commit: the new panel's auto-load success-status
  banner (reusing the shared `.biolog-placeholder-status--success` class) was visible on `/` by
  default with no user action, which is what first exposed a pre-existing colour-contrast
  shortfall in that shared class (4.14:1 vs. the required 4.5:1 — every existing use of the class
  elsewhere in this app requires a click first, so nothing had rendered it by default for an axe
  scan before). Fixed by not rendering a redundant success banner at all (the panels rendering IS
  the success indicator) rather than editing the shared design-system class, which needs Damilola
  ownership. `bio-ux-002-shell.spec.ts`'s three axe baseline tests on `/` (nav, search-results
  panel, user-menu panel) all pass after this fix.
- `tests/frontend/playwright/bio-ux-014-visual-regression.spec.ts`'s six `dashboard-*` baseline
  screenshots (mobile/tablet/desktop × light/dark) were deliberately regenerated — the new
  Operational health section is required, approved new content (AC01), not an unintended
  layout change; the diff was purely additive page height from the new section.
