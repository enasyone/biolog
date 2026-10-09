# BIO-UX-010 — builder summary

Implemented interactive operational journeys for Access, Attendance, Visitors, Transport and
Devices, replacing each domain's `PlaceholderPage` shell (BIO-UX-002/003) with a real client page
that reads tenant-scoped synthetic data (BIO-UX-004) and calls a route-manifest-named port
(BIO-UX-005's mock/live adapter framework) for every action.

## What shipped

- 5 new page components (`web/features/<domain>/<domain>-page.tsx`) replacing the placeholder
  wrapper on `/access-control`, `/attendance`, `/visitors`, `/transport`, `/devices`.
- 3 shared components: `ScenarioPicker`, `NotificationList`, `ActionStatus`.
- `web/lib/synthetic-data` extended: 3 new scenarios (`degraded-device`, `denied-access`,
  `notification`), a `degraded` device-connectivity state, an access-event `reasonCode`,
  `AssuranceLevel`/`AccessReasonCode` types, and a new `deriveNotifications()` module.
- `feature-mode.ts`/`module-registry.ts` extended with each domain's route-manifest-named
  secondary action(s) as independently-mode-switchable ports (AccessService, AttendanceService,
  TransportService, DeviceService gain one each; VisitorService gains two), mirroring
  `ReportingService`'s established two-port pattern.
- 16 new Playwright tests (`bio-ux-010-operational-journeys.spec.ts`, one per AC plus an
  accessibility sweep of all 5 pages) and 2 new Vitest files (scenario/notification transforms;
  port-registry parity), plus updates to 2 pre-existing Vitest files whose hard-coded
  single-multi-port-module assumptions this story's own multi-port additions legitimately changed
  (each updated assertion documented inline with why).
- `docs/features/bio-ux-010/README.md` — full AC table, disclosed scope notes (the missing
  `governance/ui-integration-map.json`, the Human Test Console boundary, why ports stay nullary),
  and the two accessibility defects axe-core caught and how they were fixed.

## Defects found and fixed during this round (self-caught, before any external review)

1. **Badge colour contrast (axe-core, WCAG AA 4.5:1).** `.biolog-op-badge`'s first draft coloured
   small text with `--biolog-color-success`/`--biolog-color-warning` — both measured under
   threshold (4.14:1/3.64:1) even against plain white. Fixed by keeping badge text
   `--biolog-color-text` and moving the status colour to a `border-left` accent (only needs the
   non-text 3:1 threshold, already cleared). No shared design-token was changed.
2. **Implicit-label `<select>` accessible-name concatenation.** A `<label>` wrapping a `<select>`
   computes its accessible name from every descendant `<option>`'s text, not just the label's own
   text — `getByLabel('Zone')` ambiguously matched the Reason-code select because one of its
   options is literally `'restricted-zone'`. Fixed with an explicit `aria-label` on every form
   control across all 5 pages (also a genuine screen-reader-quality fix, not just a test
   workaround).
3. **Mock-adapter determinism not accounted for in tests.** `createMockAdapter`'s scenario index is
   a hash of `${module}:${callCount}`, shared per *service module*, not per port — a fresh port's
   own first call does not always resolve to `'success'` (verified per-module in
   `bio-ux-010-module-ports.test.ts`). An early draft of the Playwright spec assumed first-click-
   succeeds and additionally raced a click against React's in-flight async state update, which hung
   indefinitely once the racing click's target button had already been removed from the DOM by a
   just-landed success. Fixed with a `clickUntilVisible` helper that checks state before each click
   and waits out the mock's simulated latency between attempts.

## Test results

- `tsc --noEmit`: clean.
- `next build`: 34 static pages, clean.
- `vitest run` (tests/frontend): 267 passed, 35 files, 0 failed.
- `playwright test` (tests/frontend): 126 passed, 0 failed.
- Manual verification: production build/start (panel exclusion + new pages render real content,
  checked post-hydration in a real headless browser, not raw HTML grep); per-port LIVE-mode switch
  (`NEXT_PUBLIC_FEATURE_MODE_ACCESSSERVICE_VIEWACCESSEVENTS=LIVE`) — see `manual-verification.txt`.

## Scope boundaries disclosed, not silently assumed

See `docs/features/bio-ux-010/README.md` "Scope notes" for the full reasoning on: the missing
`governance/ui-integration-map.json`; the Human Test Console (BIO-UX-012, not this story); why
ports stay nullary rather than parameterised; and why list/query actions read `generateDataset()`
directly rather than wiring up the still-dormant `SyntheticDataStore` class.
