# BIO-UX-010 — Build interactive operational journeys for access, attendance, visitors, transport and devices

As an operator, the five operational page families (Access, Attendance, Visitors, Transport,
Devices) are clickable in simulation, so their workflows can be tested while the owning backend
services (BIO-ACC-001, BIO-ATT/VIS/TRN-*, BIO-DEV-001/002) are still being built. Only AccessService
and DeviceService have real backends today (per the dispatch); Attendance/Visitor/Transport pages
are SIMULATION-only by necessity, not a scope gap — none of their `BIO-ATT/VIS/TRN` stories are
started, so there is no live endpoint any of these pages could honestly call yet.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Access/Attendance/Visitors/Transport/Devices pages are interactive | Each route's `PlaceholderPage` wrapper replaced with a real client page (`web/features/<domain>/<domain>-page.tsx`) rendering live tenant-scoped synthetic data, forms and lists — not a generic "Simulate primary action" button | `bio-ux-010-operational-journeys.spec.ts` AC01 block |
| **AC02** | Online/offline/degraded-device/denied-access/notification scenarios available | Shared `ScenarioPicker` component + 3 new `ScenarioId` presets (`degraded-device`, `denied-access`, `notification`) added to `web/lib/synthetic-data`; `deriveNotifications()` surfaces the `notification` scenario's alerts | `bio-ux-010-scenarios.test.ts`; `bio-ux-010-operational-journeys.spec.ts` AC02 block |
| **AC03** | Policy-builder controls simulate assurance-level decisions and reason codes | Access Control's "Create policy" form (zone, `AssuranceLevel`, `AccessReasonCode`) produces a visible simulated policy row after `accessPort` confirms the outcome | `bio-ux-010-operational-journeys.spec.ts` AC03 block |
| **AC04** | Visitor check-in/out and transport boarding/alighting produce visible simulated events | Visitors page flips a visitor's status badge and logs a visit-history entry on check-in/out; Transport page adds a route-assignment entry — both confirmed via their own port before updating visible state | `bio-ux-010-operational-journeys.spec.ts` AC04 block |
| **AC05** | Every primary/secondary action exposes a stable port for later live services | Each domain's `route-manifest.ts`-named primary/secondary actions got their own `ModulePort` key (mirroring `ReportingService`'s established two-port pattern), registered in `MODULE_PORT_KEYS`/`MODULE_PORTS`; no dead buttons | `bio-ux-010-module-ports.test.ts`; `bio-ux-010-operational-journeys.spec.ts` AC05 block |

## Scope notes (disclosed, not silently decided)

- **`governance/ui-integration-map.json` does not exist in this repository.** `route-manifest.ts`
  and `module-port.ts` both cite it as their nominal source of truth for `servicePort`/route data,
  but no commit on any branch ever added the file (a pre-existing gap, already flagged by
  BIO-UX-002/003's own evidence for the same reason). `route-manifest.ts`'s already-approved
  `AccessService`/`AttendanceService`/`VisitorService`/`TransportService`/`DeviceService` entries
  (routes, `primaryActions`, `secondaryActions`) were treated as the authoritative AC-mapping
  source instead, consistent with that prior precedent.
- **The "Human Test Console" named in the master builder prompt's UI-first constraints is a
  separate, not-yet-built BIO-UX-012 deliverable** (`feature-mode-panel.tsx`'s own doc comment says
  so explicitly). This story's scenario picker is a plain per-page `<select>`, not an attempt to
  build or pre-empt that interactive console — it needs no dev-only guarding because scenario
  switching is a legitimate simulated product capability for an operator testing degraded/offline
  conditions, not a debug tool (unlike the dev-only feature-mode panel, which stays untouched).
- **Ports stay nullary (`ModulePort`'s existing shape), not parameterised.** Every "command" action
  (create policy, check in/out, flag exception, assign route, register device) follows a two-step
  pattern: the page collects real input (zone, assurance level, reason code, ...) and updates
  visible page-level state itself, then calls its port for the network-outcome banner
  (success/failure/offline) — the same generic outcome contract every other module already uses.
  This was chosen over inventing per-domain parameterised port methods because it needed zero
  changes to `module-port.ts`'s `createMockAdapter`/`createLiveAdapter` factories or the
  `ModulePort` interface itself, stays consistent with the one other multi-action precedent
  (`features/reporting`'s two independently-mode-switchable ports), and keeps every port a genuine,
  swappable SIMULATION/LIVE surface (AC05) rather than a page-specific one-off.
- **List/query actions ("View access events", "View attendance record", "View boarding/alighting
  log", "View connectivity status", "View visit history") read from `generateDataset()`
  (BIO-UX-004), not the dormant `SyntheticDataStore` class.** `store.ts`'s own doc comment names
  BIO-UX-005/012 as its integration point for in-session *mutation* tracking; none of these five
  pages need cross-navigation mutation persistence (a fresh `generateDataset(DEFAULT_SEED,
  scenarioId)` per scenario selection is sufficient and simpler), so `SyntheticDataStore` remains
  unconsumed — a deliberate choice, not an oversight.

## Extending the synthetic-data and port-registry frameworks

Per the dispatch (`web/lib/route-manifest.ts`, `web/lib/synthetic-data/`, `web/lib/module-port.ts`
were listed as available), this story extended rather than forked these BIO-UX-004/005 frameworks:

- `web/lib/synthetic-data/types.ts`: `ScenarioId` gained `'degraded-device' | 'denied-access' |
  'notification'`; `DeviceConnectivity` gained `'degraded'`; `SyntheticAccessEvent` gained an
  optional `reasonCode`; new `AssuranceLevel`/`AccessReasonCode`/`SyntheticNotification` types.
  `OPERATIONAL_SCENARIO_IDS` narrows the full `SCENARIO_IDS` union to the 5 AC02 names.
- `web/lib/synthetic-data/scenarios.ts`: 3 new pure transforms (`degradedDeviceOf`,
  `deniedAccessOf`, `notificationOf` — the last composes the other two plus sets
  `flags.simulateNotification`), added to `applyScenario`'s switch.
- `web/lib/synthetic-data/notifications.ts` (new): `deriveNotifications(dataset)` — pure, gated on
  `flags.simulateNotification` so switching to `offline`/`degraded-device` alone (without also
  selecting `notification`) changes a page's list content without an unrelated banner appearing.
- `web/lib/feature-mode.ts` / `web/lib/module-registry.ts`: `MODULE_PORT_KEYS`/`MODULE_PORTS`
  extended with each domain's route-manifest-named secondary (and, for Visitors, two secondary)
  actions, plus their own `NEXT_PUBLIC_FEATURE_MODE_<MODULE>_<PORT>` per-port env overrides —
  verified end to end in a real browser (see "Proving AC02/AC05" below), not just unit-tested.

## Accessibility fixes found by axe-core, not assumed away

- **Badge colour contrast.** `.biolog-op-badge`'s first draft coloured 12px status text directly
  with `--biolog-color-success`/`--biolog-color-warning` on their matching `-bg` tokens — axe-core
  flagged both as under WCAG AA's 4.5:1 threshold (4.14:1/3.64:1 even against plain white,
  verified independently). Fixed by keeping badge text `--biolog-color-text` (14.4–15.3:1 against
  every status `-bg` token in both themes) and moving the status colour to a `border-left` accent,
  which only needs the 3:1 non-text/UI-component threshold every status colour already clears — no
  shared design-token change, scoped entirely to this story's own new CSS classes.
- **Implicit `<label>` wrapping a `<select>` absorbs every descendant `<option>`'s text into the
  computed accessible name**, not just the label's own visible text or the selected option — e.g. a
  "Reason code" select's name became `"Reason code" + "after-hours" + "restricted-zone" + ...`
  concatenated, which is both a confusing screen-reader announcement and (caught by
  `bio-ux-010-operational-journeys.spec.ts`, not manual review) made `getByLabel('Zone')`
  ambiguously match that select too, since `'restricted-zone'` contains `'zone'`. Fixed with an
  explicit `aria-label` on every form control (`Zone`, `Assurance level`, `Reason code`, `Person`,
  `Route`, `Device name`, `Scenario`), which short-circuits the concatenation per the accessible-
  name computation's own precedence order.

## Mock-adapter determinism and test design

`createMockAdapter`'s deterministic index (`module-port.ts`) is a hash of `${module}:${callCount}`
— shared per *module* (`AccessService`, `VisitorService`, ...), not per port instance, so a fresh
port's own first call does not always land on `'success'` (verified: AccessService does;
AttendanceService/VisitorService/TransportService/DeviceService's own first call is
`'failure'`/`'offline'` — see `bio-ux-010-module-ports.test.ts`). `bio-ux-010-operational-journeys.
spec.ts`'s `clickUntilVisible` helper checks the target state *before* each click and waits out the
mock's simulated latency after, rather than assuming immediate success or racing a click against an
in-flight async result (an earlier draft that raced the two hung indefinitely once a button its
locator depended on had already been removed from the DOM by a just-landed success).

## Evidence references

- `evidence/BIO-UX-010/builder/metadata.json`, `SUMMARY.md`
- `evidence/BIO-UX-010/builder/web-typecheck-build-output.txt`
- `evidence/BIO-UX-010/builder/frontend-unit-test-output.txt`
- `evidence/BIO-UX-010/builder/frontend-e2e-test-output.txt`
- `evidence/BIO-UX-010/builder/manual-verification.txt` (production-build panel exclusion,
  per-port LIVE-mode switch)
