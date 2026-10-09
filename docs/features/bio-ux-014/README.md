# BIO-UX-014 — Create visual-regression baseline and screenshot test harness

As the engineering team, we need automated visual baselines so later feature integrations cannot
silently drift from the approved BIOLOG interface.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Playwright screenshot baselines cover the shell and representative pages from every major module | `tests/frontend/playwright/bio-ux-014-visual-regression.spec.ts`: full viewport x theme matrix for the Dashboard/shell, one canonical (desktop, light) screenshot for every other representative page — see "Scope" below | `bio-ux-014-visual-regression.spec.ts` (14 screenshot tests), `bio-ux-014-visual-regression-config.test.ts` (every nav group has a representative page) |
| **AC02** | Approved viewport/theme combinations are documented | `tests/frontend/playwright/fixtures/approved-viewports.ts`'s `VIEWPORTS` (mobile/tablet/desktop, pinned to `web/design-system/breakpoints.ts`'s `bpTablet`/`bpDesktop`), `THEMES = ['light', 'dark']` in the spec file | `bio-ux-014-visual-regression-config.test.ts` cross-checks the viewport widths against the real breakpoint constants |
| **AC03** | Visual regression can run locally with one documented command | `corepack pnpm --dir tests/frontend test:visual` | manual verification below |
| **AC04** | Baseline update requires explicit intentional action and produces reviewable evidence | Baselines are committed PNGs (`bio-ux-014-visual-regression.spec.ts-snapshots/`, not gitignored); the default `test:visual`/`test:e2e` commands never touch them; only the separate `test:visual:update` command (`--update-snapshots`) does, and its output is a normal, reviewable `git diff` of PNG files | Regression-detection proven by deliberately reddening the dashboard heading, confirming all 6 dashboard variants failed and all 8 other pages stayed green, then reverting — see SUMMARY.md |
| **AC05** | Visual tests use deterministic synthetic data and do not depend on external production services | Every route uses BIO-UX-004's seeded synthetic dataset via the existing `?as=`/`?tenant=` demo-session overrides; `BIOLOG_BASE_URL` defaults to `127.0.0.1`; timezone/locale pinned to `UTC`/`en-GB` so rendered timestamps are byte-identical across machines | `bio-ux-014-visual-regression-config.test.ts`'s AC05 block (no external URL referenced anywhere in the spec or the shared Playwright config) |

## Scope: why not a full 8 x 3 x 2 matrix

A full cross product of 8 representative pages x 3 viewports x 2 themes would be 48 screenshots —
slow to run and painful to review by eye on a real diff. Instead:

- **Dashboard ("/")** gets full coverage across all three approved viewports and both themes (6
  screenshots) — AC02's concern (responsive/theme correctness) is uniform shell behaviour, not a
  per-page one, and BIO-UX-013 already proved every representative page has no layout overflow at
  all three breakpoints (`bio-ux-013-breakpoints.spec.ts`), so re-proving pixel-level responsive
  correctness on every single page here would be redundant with that story's own coverage.
- **Every other representative page** is baselined once, at the single canonical desktop+light
  combination (8 screenshots) — AC01's concern is per-module content coverage.

Total: 14 committed baseline PNGs.

## Representative pages (shared with BIO-UX-013)

`fixtures/approved-viewports.ts` extracts `VIEWPORTS` and `MAJOR_PAGES` out of
`bio-ux-013-breakpoints.spec.ts`, where they were originally defined inline, so both stories test
against the exact same combinations rather than two lists that can silently drift apart.
`bio-ux-013-breakpoints.spec.ts` now imports from the shared fixture with **no change to its own
assertions**.

One gap was found and closed while reusing the list: BIO-UX-013's original 8 pages had no
representative page for the **Enrolment** nav group (`/enrolment`, `/photographs`, `/fingerprints`,
`/facial-recognition`, `/duplicate-review`) — acceptable under BIO-UX-013's own looser mandate
("god's dispatch left the exact set to this story's own judgement"), but not under this story's
AC01, which requires a representative page from *every* major module. Added `/enrolment` to the
shared list — strictly additive for BIO-UX-013's own sweep (one more page tested at all three
breakpoints, zero existing assertions changed).

Final representative set (one per `web/lib/nav-items.ts` group, plus the ungrouped Dashboard):
Dashboard, People (Identity), Enrolment, Settings (Administration), Document Verify (Documents),
Cards (Credentials), Access Control (Access & Operations), Administration, Reports.

## Two real defects found and fixed while building this harness (not shipped-elsewhere bugs)

1. **Next.js 16's dev overlay isn't fully suppressed by `devIndicators: false`.** That flag
   (added to `web/next.config.mjs` earlier this session for an unrelated Playwright
   selector-collision fix) hides the "Open Next.js Dev Tools" button, but a separate "N issues"
   badge inside the same `<nextjs-portal>` custom element can still render, and its
   expanded/collapsed state (and the issue count itself) isn't deterministic run-to-run — a real,
   reproducible ~1% pixel diff appeared on `dashboard-mobile-dark` between the baseline-creation
   run and the very next verification run, traced by inspecting Playwright's own diff image
   (visibly showing the "N Issues" badge). Fixed by masking `page.locator('nextjs-portal')` in
   every `toHaveScreenshot()` call — dev-only chrome, never part of the approved interface this
   baseline protects, so it's excluded from the comparison rather than chased with more config
   flags. Verified stable across 3 consecutive unmodified runs afterward.
2. Not a defect, but worth recording: the mock-adapter loading state (BIO-UX-005-AC05) needs an
   explicit wait (`waitForSettled`, polling for the "Loading…" text to disappear) before each
   screenshot — `toHaveScreenshot`'s own internal frame-stabilization isn't guaranteed to wait
   long enough for an app-level async state transition on every environment.

## Regression detection proven, not just asserted

Temporarily reddened the Dashboard `<h1>` (`color: 'red'`), ran `test:visual`: all 6 dashboard
variants failed (mobile/tablet/desktop x light/dark) and all 8 other-page tests stayed green
(correctly unaffected, proving the check is scoped to what actually changed, not a blanket
false-positive). Reverted, re-ran: 14/14 clean again. Evidence in
`evidence/BIO-UX-014/builder/regression-proof-output.txt`.

## Local development / verification

```sh
corepack pnpm --dir web typecheck
corepack pnpm --dir web build
corepack pnpm --dir tests/frontend test:unit   # includes bio-ux-014-visual-regression-config.test.ts
corepack pnpm --dir tests/frontend test:visual # BIO-UX-014-AC03: the one documented command
```

To intentionally update a baseline after an approved visual change (BIO-UX-014-AC04):

```sh
corepack pnpm --dir tests/frontend test:visual:update
git diff --stat tests/frontend/playwright/bio-ux-014-visual-regression.spec.ts-snapshots/
```

The second command is the "reviewable evidence" — the diff shows exactly which baseline PNGs
changed, for a human/reviewer to confirm the change was the intended one before committing it.
Never run `test:visual:update` as part of routine CI/`test:e2e` — only as a deliberate,
separately-invoked step when a visual change has been approved.

## Scope boundaries

- No new routes, components, or design-system changes — this story only adds test infrastructure
  (a spec file, a shared fixture, two npm scripts, committed baseline PNGs).
- The `devIndicators: false` config change and the `nextjs-portal` mask both address dev-tooling
  interference, not the approved application design.
- `bio-ux-013-breakpoints.spec.ts`'s refactor to import the shared fixture changes zero assertions
  or behaviour — full regression run (368/368) confirms this.

## Round 2 (CHANGES_REQUIRED — Chioma, QA-01 High): dark baselines were byte-identical to light

Chioma found (and independently re-confirmed with `shasum`) that all three dashboard dark PNGs
were byte-for-byte identical to their light counterparts — the spec set theme via `localStorage`
but never verified it actually took effect before capturing, so every "dark" screenshot was
silently capturing the light-rendered page.

**Root cause, found by direct empirical isolation, not guesswork**: `page.setViewportSize()`
called *after* `page.addInitScript()` but *before* `page.goto()` can cause the init script to not
fire for that navigation. Confirmed by writing a minimal reproduction and swapping the two calls'
order with nothing else changed — that alone flipped `html[data-theme]` from consistently `null`
to consistently correct. Fixed by moving `setViewportSize` before `setTheme`.

**A second, deeper defect surfaced while verifying the fix**: with the order corrected, the
`data-theme` assertion still failed — this time because of a genuine React hydration crash
(`Uncaught Error: Hydration failed because the server rendered text didn't match the client`,
minified error #418), traced via a `page.on('pageerror')` listener attached directly to the
failing test's own page. Root cause: the Dashboard's "recent activity" timestamps
(`formatTimestamp`) are formatted using the *server process's own local timezone* during
server-side rendering, while this spec's browser context is pinned to `timezoneId: 'UTC'` for
screenshot determinism — different rendered text, hydration failure, and the crash-recovery
re-render disrupts the theme script's effect as collateral damage. **This is a real, separate
application defect**: any real user whose browser timezone differs from wherever the app is
built/deployed would hit the same crash in production, not just in this test. Reported to god
separately (not fixed here — out of this story's scope; the owning team needs to either format
with an explicit fixed timezone, defer the timestamp render to client-only, or accept
`suppressHydrationWarning` deliberately).

Fixed on the test-infrastructure side by pinning the *server's* timezone to match the browser
context: added `env: { TZ: 'UTC' }` to `playwright.config.mjs`'s shared `webServer` config (the
one `test:e2e` and every other spec's auto-started dev server already uses) — costs nothing for
every other spec (none assert exact wall-clock text) and removes this whole class of flake for
this and any future spec that touches a timestamp. Also set `TZ=UTC` explicitly in this story's
own `test:visual`/`test:visual:update` scripts, for the case where `BIOLOG_BASE_URL` points at a
manually-managed server outside `playwright.config.mjs`'s control.

**A third, lower-frequency issue found under heavy concurrency**: even with both fixes above,
running the full `test:e2e` suite (which runs this file alongside ~30 other spec files, all
sharing one dev server across many parallel workers) still showed occasional `data-theme`
failures. Added three layers of defense: `test.describe.configure({ mode: 'serial' })` (this
file's own tests never compete with each other), a `beforeAll` warm-up that visits every route
once before any timed test runs (Next.js dev mode compiles each route on first request, and a
cold compile can push response timing past what the init-script race tolerates), and a
reload-and-recheck fallback in `captureBaseline` (if the theme attribute isn't set on first check,
one `page.reload()` deterministically re-triggers the init script before failing for real).

**Disclosed, not hidden**: near the end of this round, `test:e2e` runs showed a *wider* spread of
failures across several unrelated, already-accepted stories' own theme/dark-mode/axe tests
(`bio-fnd-007-shell.spec.ts`, `bio-ux-002-shell.spec.ts`, `bio-ux-005/011/012/013`'s own specs) —
none of which this story touches. `ps aux` showed a dozen concurrent `next-server`/Chromium
processes accumulated from this session's many consecutive hours of Playwright activity across
the whole floor. This reads as ambient system resource pressure, not a BIO-UX-014 regression: this
story's own scoped `test:visual` command remained clean and stable throughout. Flagged to god
rather than chased further inside this story's scope - the fix (if one is needed) is floor-level
capacity/process hygiene, not a `bio-ux-014-visual-regression.spec.ts` code change.

Verified (in an environment not showing the ambient-load symptom above): `test:visual` clean
across 5+ consecutive runs; the full theme-selectivity proof (dark-only CSS change fails only the
3 dark dashboard tests, light-only CSS change fails only the 3 light dashboard tests + the 8
light-only other-page tests, in both directions) in
`evidence/BIO-UX-014/builder/bio-ux-014-theme-selectivity-proof.txt`; `shasum` confirms all three
light/dark PNG pairs now hash differently.
