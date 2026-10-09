# BIO-UX-005 — Implement mock/live adapter framework and feature-mode registry

As the BIOLOG engineering team, stable frontend service ports with mock and live adapters exist so
later sprints can replace simulation without redesigning screens.

**Round 1 CHANGES_REQUIRED (Chioma, evidence `e2dd368`):** AC02 initially shipped with every real
module registering exactly one port, so `getModuleStatus` could only ever return `SIMULATION` or
`LIVE` — `HYBRID` was asserted "currently unreachable" rather than demonstrated. Remediated by
giving `ReportingService` a second, genuinely independent port (`viewAuditTrail`, backing `/audit`,
alongside `primaryAction`, backing `/reports` and `/`) — see "AC02 remediation: a real multi-port
module" below. No scope change was requested; the fix uses two actions `route-manifest.ts` already
named as distinct before this remediation ran.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Ports defined per module; components depend on ports, not direct mock/API calls | 13 `web/features/<domain>/{ports,mock,live,index}.ts` scaffolds (architecture doc's reference structure) + `web/lib/module-registry.ts`; `PlaceholderPage` (all 30 non-Card-Designer routes) calls a resolved port, never local state | `bio-ux-005-module-registry.test.ts`; `bio-ux-005-adapter-framework.spec.ts` |
| **AC02** | Each module independently SIMULATION/HYBRID/LIVE | `feature-mode.ts`'s `getModuleStatus` aggregates a module's own ports; `ReportingService` has two independent ports so `HYBRID` is genuinely reachable, not just theoretical — verified in unit tests, Playwright, and a real browser session (see "AC02 remediation" below) | `bio-ux-005-feature-mode.test.ts` "reports HYBRID"; `bio-ux-005-adapter-framework.spec.ts` |
| **AC03** | Dev-only feature-mode registry; excluded from production | `web/dev-tools/feature-mode-panel.tsx`; verified absent from a real `next build`/`next start`, server-rendered and after hydration (see "Proving AC03" below) | `bio-ux-005-adapter-framework.spec.ts`; manual production-build verification |
| **AC04** | Switching mock→live needs no page redesign | `resolvePort` + `NEXT_PUBLIC_FEATURE_MODE_<MODULE>`; the identical `page.tsx`/`PlaceholderPage` files render both modes | `bio-ux-005-feature-mode.test.ts` "resolvePort"; manual live-mode verification |
| **AC05** | Mock adapters reproduce success/failure/loading/offline/boundary | `createMockAdapter`'s deterministic scenario cycle + async latency; every real domain's mock lists all three outcomes | `bio-ux-005-module-port.test.ts`; `bio-ux-005-adapter-framework.spec.ts` |
| **AC06** | Mode config auditable, fails safe when invalid | `auditFeatureModes()` (panel's data source); `getPortMode`/`setPortModeOverride` fall back to `SIMULATION` on any unknown/invalid input, never `LIVE` | `bio-ux-005-feature-mode.test.ts` "fails safe" block |

## Scope: Card Designer stays excluded (Kemi's ruling)

Per this story's own Scope notes, `/card-designer` (`web/features/bio-crd-002/003/004`) is an
explicit, disclosed exception to AC01 — it has no backend I/O today, so there is nothing to route
through a port; `BIO-UX-007-AC06` owns designing its real port once card lifecycle/production/
encoding concepts exist. Nothing in this story touches those three directories or that route.

## Why every one of the other 30 routes was wired, not just documented

No route shipped any real data-fetching before this story — every placeholder page's "Simulate
primary action" button flipped local `useState` and did nothing else. Leaving the framework as an
unused abstraction would leave AC04/AC05 unproven. Instead, `PlaceholderPage` (BIO-UX-002, owned by
this role) now calls `MODULE_PORTS[module].primaryAction()`, so every one of the 30 wired routes is
a genuine, working demonstration of SIMULATION today and LIVE the moment a domain story fills in
its `live.ts` — not a separate proof-of-concept nobody exercises.

## Architecture notes

- **Generic port shape, not 13 invented domain APIs.** Every module currently needs exactly one
  operation (a primary action that can succeed, fail, or find the network offline), because no
  route has shipped real feature-specific behaviour yet (that's BIO-UX-006 onward, each owned by a
  different domain engineer). `ModulePort` is deliberately generic; a domain story extends its own
  `ports.ts` interface with real methods without touching this framework or any other module.
- **`module` is a string prop, not the port object.** `page.tsx` files are Server Components;
  `PlaceholderPage` is a Client Component. A `ModulePort` carries a function
  (`primaryAction`), which React Server Components cannot serialise across that boundary — passing
  it directly broke the production build (caught by actually running `next build`, not assumed).
  Pages instead pass the plain `ServicePort` string; `PlaceholderPage` resolves the real port from
  `MODULE_PORTS` (`module-registry.ts`) itself, entirely client-side.
- **`NEXT_PUBLIC_FEATURE_MODE_<MODULE>` env vars use literal references, not computed keys.**
  `feature-mode.ts`'s `ENV_DEFAULTS` explicitly lists all 13 `process.env.NEXT_PUBLIC_FEATURE_MODE_*`
  accesses. A first attempt used `process.env[\`NEXT_PUBLIC_FEATURE_MODE_${module}\`]` (a computed
  key) — Next.js's bundler only inlines `NEXT_PUBLIC_*` values it can see referenced literally at
  build time, so that version silently always returned `undefined` in the browser regardless of the
  env var. Caught by actually starting a second dev server with the env var set and checking real
  behaviour (see "Proving AC02/AC04" below), not by trusting the code compiled and moving on.
- **Each mock adapter's call counter is per-instance, not module-wide.** An earlier version used one
  shared counter across every `createMockAdapter` call anywhere in the app, which made a given
  module's scenario sequence depend on how many *other* modules' ports had been called first in the
  same browser session — reproducible in the sense of "given identical history," but not in the
  practical sense a test or a human demo needs ("this module's own first click is always X"). Fixed
  to a closure-scoped counter per adapter instance.

## Proving AC03: the panel really is absent from production

```sh
cd web && rm -rf .next && pnpm build && pnpm start -p 3219 &
curl -s http://127.0.0.1:3219/ | grep -c biolog-feature-mode-panel   # 0
# then, via Playwright, after full client hydration:
# page.locator('.biolog-feature-mode-panel').count()                # 0
```

Both checked in this session — see `evidence/BIO-UX-005/builder/ac03-ac04-manual-verification.txt`
for the captured output. Not just asserted: `process.env.NODE_ENV === 'production'` is inlined to a
literal at Next.js build time, so `FeatureModePanel` always returns `null` before any hook runs in
that build, and the panel's own JSX is never mounted server- or client-side.

## Proving AC02/AC04: switching one module to LIVE leaves every other module untouched

```sh
cd web && rm -rf .next
NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE=LIVE pnpm dev -p 3220 &
# /devices' primary action now calls fetch('/api/v1/devices/primary-action') for real —
# genuinely fails (HTTP 404, no such backend route exists yet): the correct LIVE-mode behaviour.
# The feature-mode panel confirms: DeviceService -> LIVE, IdentityService -> SIMULATION (untouched).
```

Captured in `evidence/BIO-UX-005/builder/ac03-ac04-manual-verification.txt`. No `page.tsx` or
`PlaceholderPage` code differs between the two runs — only the env var.

## AC02 remediation: a real multi-port module, HYBRID genuinely reachable

`route-manifest.ts` already named `/reports` ("Run report") and `/audit` ("View audit trail") as
distinct actions before this remediation — every other module's routes still share one generic
action today, but Reporting's two were already different. `web/features/reporting/` now exposes
two independently resolvable ports instead of one:

- `ports.ts`/`mock.ts`/`live.ts`: `primaryAction` (backs `/reports` and `/`) and `viewAuditTrail`
  (backs `/audit`), each with its own mock scenario set, its own live endpoint
  (`/api/v1/reporting/primary-action` vs. `/api/v1/reporting/view-audit-trail`), and its own mode
  resolution.
- `feature-mode.ts`: `MODULE_PORT_KEYS.ReportingService` is now `['primaryAction',
  'viewAuditTrail']`; a new `PER_PORT_ENV_DEFAULTS` map lets `viewAuditTrail` diverge from the
  module's whole-module env default via `NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_VIEWAUDITTRAIL`
  (every other module keeps its simple single-var configuration, unaffected).
- `module-registry.ts`/`placeholder-page.tsx`: `MODULE_PORTS` is now indexed by module *and* port
  key (`Record<ServicePort, Record<string, ModulePort>>`); `PlaceholderPage` gained an optional
  `portKey` prop (default `'primaryAction'`, so 29 of the 30 wired pages needed no change) —
  `/audit/page.tsx` is the one page that passes `portKey="viewAuditTrail"`.

**Verified three ways, not just asserted:**

1. Unit: `setPortModeOverride('ReportingService', 'viewAuditTrail', 'LIVE')` leaves `primaryAction`
   at `SIMULATION` and `getModuleStatus('ReportingService')` returns `HYBRID`
   (`bio-ux-005-feature-mode.test.ts`).
2. Unit: the per-port env var alone (no override call) produces the same result — a config-level
   proof, not just a JS-call proof.
3. A real browser session: `NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_VIEWAUDITTRAIL=LIVE pnpm dev`
   — the dev panel shows `ReportingService: HYBRID (primaryAction: SIMULATION, viewAuditTrail:
   LIVE)`, `/audit`'s primary action genuinely calls the live endpoint (HTTP 404, no backend built
   yet — correct), and `/reports` stays on its simulated scenario cycle, unaffected. Captured in
   `evidence/BIO-UX-005/builder/ac03-ac04-manual-verification.txt` "Proof 3."

## Local development / verification

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — 34 routes, all prerendered
corepack pnpm --dir tests/frontend test:unit   # 30 new tests (3 files), 213 total (30 files) pass
corepack pnpm --dir tests/frontend test:e2e    # 110 Playwright tests pass, zero regressions
```

To flip a module to LIVE locally: set `NEXT_PUBLIC_FEATURE_MODE_<MODULE>=LIVE` in `web/.env.local`
(module name upper-cased, e.g. `NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE=LIVE`) and restart `pnpm dev`
— env vars are read once at server start, not hot-reloaded. View current state via the "DEV — feature
modes" panel, bottom-right, in any non-production build.
