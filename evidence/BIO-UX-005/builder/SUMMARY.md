# BIO-UX-005 — Builder evidence

**Story:** Implement mock/live adapter framework and feature-mode registry
**Owner:** Damilola — Product UI/UX & Frontend Lead
**Worktree:** `biolog-worktrees/BIO-UX-005`, branch `feature/BIO-UX-005` off `develop`
**Dependencies:** BIO-UX-003 (DONE, merged `63d9e66`), BIO-ARC-004 (DONE)
**Conflict group:** `ui-adapter-core`
**Commit:** `c5a96e1249d45759e70f66a25fddbef2a8b3be2b` (round 2; round 1 was `2877caf9`)

## Round 2 — CHANGES_REQUIRED remediation (Chioma, evidence `e2dd368`)

Round 1 acceptance rejected: AC02 requires each module to independently support SIMULATION/
HYBRID/LIVE, but every real module had exactly one port, so `getModuleStatus()` could never
actually return `HYBRID` — the builder evidence honestly disclosed this as "currently
unreachable," which Chioma correctly identified as insufficient ("a future possibility is not
evidence of the approved current behavior").

Remediated per her option (a) — a real multi-port module with executable evidence, not a scope
change: `ReportingService` now has two genuinely independent ports, `primaryAction` (`/reports`,
`/`) and `viewAuditTrail` (`/audit`) — actions `route-manifest.ts` already named as distinct
before this remediation, not invented to make the assertion pass. See
`docs/features/bio-ux-005/README.md`'s "AC02 remediation" section for the full implementation and
three-way verification (unit test override, unit test per-port env var, and a real browser session
showing the dev panel display `HYBRID` with the correct per-port breakdown while `/audit` genuinely
calls its live endpoint and `/reports` stays simulated, untouched). New evidence captured in
`ac03-ac04-manual-verification.txt`'s "Proof 3" and `round2-*-output.txt` files.

Full suite after remediation: 27 unit files / 217 tests, 110 Playwright tests, all pass, zero
regressions. `web/` typecheck and production build clean.

## Round 1

## AC to evidence map

See `docs/features/bio-ux-005/README.md` for the full AC table and architecture notes. Summary:

| AC | Evidence |
|---|---|
| AC01 (ports per module) | 13 `web/features/<domain>/{ports,mock,live,index}.ts`; `module-registry.ts`; every non-Card-Designer route now calls through a port |
| AC02 (independent SIMULATION/HYBRID/LIVE) | `getModuleStatus`; **verified live** — flipping DeviceService to LIVE left IdentityService at SIMULATION |
| AC03 (dev-only registry, excluded from prod) | `feature-mode-panel.tsx`; **verified absent** from a real `next build && next start`, server-rendered and post-hydration |
| AC04 (mock→live needs no redesign) | `resolvePort` + env var; **verified live** — same `page.tsx`/`PlaceholderPage` files, only the env var differs |
| AC05 (mock reproduces success/failure/loading/offline/boundary) | `createMockAdapter`'s deterministic cycle + real async latency; empty-scenarios boundary case throws |
| AC06 (auditable, fails safe) | `auditFeatureModes()`; every invalid/unknown input in `getPortMode` falls back to `SIMULATION`, tested explicitly |

## Two real bugs found by actually running the app, not by trusting the code

1. **Passing a `ModulePort` object as a prop from a Server Component page to the Client Component
   `PlaceholderPage` broke `next build`** — `ModulePort.primaryAction` is a function, and React
   Server Components cannot serialise a function across that boundary. Caught immediately by
   running `pnpm build` (not just `tsc`), which failed with a clear RSC serialization error.
   Fixed: pages pass the plain `ServicePort` string; `PlaceholderPage` resolves the real port from
   `MODULE_PORTS` itself, client-side only.
2. **`NEXT_PUBLIC_FEATURE_MODE_<MODULE>` env vars silently never worked** — the first
   implementation read them via `process.env[\`NEXT_PUBLIC_FEATURE_MODE_${module}\`]`, a computed
   key. Next.js's bundler only inlines `NEXT_PUBLIC_*` references it can see literally at build
   time; a computed key can't be statically analysed, so it always resolved to `undefined` in the
   browser regardless of what the env var was actually set to. This would have silently defeated
   the entire "flip a module to LIVE" mechanism the story exists to build. Caught by starting a
   second dev server with `NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE=LIVE` and checking the actual
   rendered result (it stayed on the mock scenario) rather than assuming the code worked because it
   typechecked. Fixed with 13 explicit literal `process.env.NEXT_PUBLIC_FEATURE_MODE_*` references.

A third, smaller issue (a shared module-wide mock-call counter making one module's scenario
sequence depend on unrelated pages' call history) was caught by Playwright test failures during
development and fixed to a per-adapter-instance counter before commit — see
`docs/features/bio-ux-005/README.md`'s architecture notes for all three.

## Manual end-to-end verification (not just unit/e2e assertions)

`evidence/BIO-UX-005/builder/ac03-ac04-manual-verification.txt` records two real runs in this
session:

- A genuine production build (`next build && next start`) with the feature-mode panel confirmed
  absent both in the server-rendered HTML and after full client hydration (Playwright-driven).
- A second dev server started with `NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE=LIVE`: `/devices`'
  primary action genuinely called `fetch('/api/v1/devices/primary-action')` and got a real HTTP 404
  (no such backend route exists yet — correct LIVE-mode behaviour, not a bug), while the panel
  confirmed `IdentityService` stayed at `SIMULATION`, untouched — direct proof of AC02's per-module
  independence and AC04's "no page redesign needed."

## Developer tests (30 unit files / 213 tests, 6 Playwright spec files / 110 tests — all pass)

- New unit: `bio-ux-005-module-port.test.ts` (10), `bio-ux-005-feature-mode.test.ts` (16),
  `bio-ux-005-module-registry.test.ts` (4).
- New Playwright: `bio-ux-005-adapter-framework.spec.ts` (13 tests) — loading state, deterministic
  scenario cycling, per-module independence, the Unassigned honesty case, and the dev panel
  (including a real axe scan that caught and fixed a landmark-region violation).
- `bio-ux-002-shell.spec.ts`'s "Simulate primary action" test was updated to assert the new,
  real port-backed result text instead of the old hardcoded "Simulated — no live service..."
  string, since that button no longer flips local state — the mechanism it tests (a visible,
  non-dead primary action) is unchanged.
- Full suite run against a real Chromium browser and real `next dev`/`next build`/`next start`
  servers in this session (ports 3215/3216/3217/3218/3219/3220 — earlier ports from this session's
  BIO-UX-002/003 work were already released by the time this story ran).

## Decisions and assumptions

- **Card Designer excluded per Kemi's recorded ruling** (this story's own Scope notes) — not
  retrofitted, not touched.
- **Every module gets the same generic `ModulePort` shape** rather than 13 invented domain-specific
  APIs, because no route has shipped real feature behaviour yet (each domain's real methods are
  each domain engineer's own future story's job, not mine to guess).
- **HYBRID is reachable for `ReportingService` (round 2 remediation) and no other module yet** —
  every other module still has exactly one port key today (`MODULE_PORT_KEYS`). Explicitly asserted
  (`bio-ux-005-feature-mode.test.ts`'s "every module has at least one port..." test pins the exact
  set of multi-port modules to `[['ReportingService', [...]]]`) so a future module gaining a second
  port is a deliberate, noticed change, not silent.

## Known gaps / local-session notes (not blocking)

- Full-repo `scripts/quality.sh` was not run (same precedent as BIO-UX-001/002/003: unrelated
  backend/.NET/infra suites, no bearing on a frontend-only story). Direct AC-relevant commands all
  ran to completion and pass, including the manual production-build and live-mode verifications
  above, which go beyond the standard automated suite.
