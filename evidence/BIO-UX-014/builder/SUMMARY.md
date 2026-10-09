# BIO-UX-014 — Builder evidence

**Story:** Create visual-regression baseline and screenshot test harness
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-UX-014`, branch `feature/BIO-UX-014` off `develop`
**Dependencies:** BIO-UX-013 (merged `6a9fa0e` — the visual state baselined here includes all five of Damilola's BIO-UX-013 CSS fixes)
**Commit:** `5955bfdf5e36f33af66f25c9690190168adb8581` (round 2 — see "Round 2" section below; round 1 commit was `cf4def0c66c05f8047b869f7a21267e5304c1c38`)

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — baselines cover the shell and representative pages from every major module | `bio-ux-014-visual-regression.spec.ts`: Dashboard full viewport x theme matrix (6 shots) + one canonical shot for every other representative page (8 shots) = 14 total. `MAJOR_PAGES` (shared with BIO-UX-013) covers every `nav-items.ts` group, extended with an `/enrolment` entry it was missing | `bio-ux-014-visual-regression-config.test.ts` (nav-group coverage), `bio-ux-014-full-e2e-output.txt` |
| AC02 — approved viewport/theme combinations documented | `fixtures/approved-viewports.ts`'s `VIEWPORTS` (pinned to `breakpoints.ts`'s `bpTablet`/`bpDesktop`), `THEMES` in the spec file, both documented in `docs/features/bio-ux-014/README.md` | `bio-ux-014-visual-regression-config.test.ts`'s AC02 block |
| AC03 — one documented local command | `corepack pnpm --dir tests/frontend test:visual` | `bio-ux-014-visual-regression.spec.ts` output (clean 14/14) |
| AC04 — baseline update requires explicit intentional action, reviewable evidence | Committed PNGs; default `test:visual`/`test:e2e` never write them; only `test:visual:update` (`--update-snapshots`) does; a `git diff` of the PNGs is the reviewable evidence | `bio-ux-014-regression-proof-output.txt`: deliberate visual break -> 6/6 dashboard variants correctly fail, 8/8 other pages correctly stay green -> revert -> 14/14 clean again |
| AC05 — deterministic synthetic data, no external dependency | All routes use BIO-UX-004's seeded dataset via existing `?as=`/`?tenant=` overrides; `timezoneId: 'UTC'`/`locale: 'en-GB'` pinned so rendered timestamps are byte-identical across machines; `BIOLOG_BASE_URL` always localhost | `bio-ux-014-visual-regression-config.test.ts`'s AC05 block |

## Developer tests

- `tests/frontend/playwright/bio-ux-014-visual-regression.spec.ts` — 14 screenshot-baseline tests.
- `tests/frontend/unit/bio-ux-014-visual-regression-config.test.ts` — 8 structural tests (AC02 viewport-to-breakpoint cross-check, AC01 nav-group coverage, AC05 no-external-dependency).
- `tests/frontend/playwright/bio-ux-013-breakpoints.spec.ts` — refactored to import the shared fixture, zero assertion changes, gained 6 new tests (3 no-overflow + 3 primary-action, for the added `/enrolment` entry) as a side effect of the shared list extension.

Reproduce with:
```sh
corepack pnpm --dir web typecheck
corepack pnpm --dir web build
corepack pnpm --dir tests/frontend test:unit
BIOLOG_BASE_URL=http://127.0.0.1:3444 corepack pnpm --dir tests/frontend test:visual
BIOLOG_BASE_URL=http://127.0.0.1:3444 corepack pnpm --dir tests/frontend test:e2e
```
(exact commands in `metadata.json`'s `test_command`, using a locally-run dev server on a port confirmed free/owned before trusting results.)

## A real, reproducible flake found and fixed while building this harness

Next.js 16's dev overlay is not fully suppressed by `devIndicators: false` (the fix applied
earlier this session for a different, Playwright-selector-collision issue). A separate "N
issues" badge inside the same `<nextjs-portal>` custom element can still render, and its
expanded/collapsed state (and the issue count itself) is not deterministic run-to-run. This
produced a real, reproducible ~1% pixel diff on `dashboard-mobile-dark` on the very first
verification run after creating baselines — traced by reading Playwright's own diff PNG, which
visibly showed the "N Issues" badge overlapping page content. Fixed by masking
`page.locator('nextjs-portal')` in every `toHaveScreenshot()` call: dev-only chrome, never part
of the approved interface this baseline protects. Verified stable across 3 consecutive
unmodified runs after the fix, and clean again after the full deliberate-break/revert cycle
below. Full detail in `docs/features/bio-ux-014/README.md`.

## Regression detection proven, not just asserted (BIO-UX-014-AC04)

Temporarily set the Dashboard `<h1>` to `color: 'red'`, ran `test:visual`: all 6 dashboard
variants (3 viewports x 2 themes) failed with a visible diff, all 8 other-page tests stayed
green — proving the check both catches real drift and is correctly scoped (doesn't produce
blanket false positives). Reverted; `git status` confirmed `web/app/page.tsx` returned to
byte-identical with its committed state; re-ran clean (14/14). Full transcript in
`regression-proof-output.txt`.

## Scope boundaries, documented rather than silently assumed

1. **Not a full 8-page x 3-viewport x 2-theme matrix (48 shots).** Dashboard gets full coverage
   (AC02's shell-level responsive/theme concern); every other representative page gets one
   canonical shot (AC01's per-module content concern) — BIO-UX-013's own no-overflow sweep
   already proves every representative page is layout-correct at all three breakpoints, so
   re-proving pixel-level responsive correctness per-page here would be redundant.
2. **Extended a shared, already-accepted story's fixture (BIO-UX-013's `VIEWPORTS`/
   `MAJOR_PAGES`), not duplicated it.** Extracted to `fixtures/approved-viewports.ts` with zero
   change to BIO-UX-013's own assertions (confirmed by the full 368/368 regression run), and
   added the one nav group (`Enrolment`) BIO-UX-013's own narrower mandate didn't require but
   this story's AC01 does — strictly additive, not a correction of a BIO-UX-013 defect.
3. **No design-system or component changes.** `devIndicators: false` (earlier this session) and
   the `nextjs-portal` mask (this story) both address dev-tooling interference only, never the
   approved application design itself.

Full suite green: `tsc --noEmit` clean, `next build` unchanged page count, unit 384/384 (8 new),
visual 14/14, full Playwright regression 368/368 (0 pre-existing failures reintroduced).

## Round 2 (CHANGES_REQUIRED — Chioma QA-01 High): dark baselines byte-identical to light

Independently re-confirmed via `shasum` before touching anything: all three dashboard dark PNGs
matched their light counterparts exactly. Full root-cause and fix narrative in
`docs/features/bio-ux-014/README.md`'s "Round 2" section; summary:

1. **`page.setViewportSize()` called after `page.addInitScript()`** silently dropped the theme
   init script for that navigation — found via isolated reproduction (swapping call order alone
   flipped the result), fixed by reordering.
2. **A real, separate application defect**: once ordering was fixed, a genuine React hydration
   crash (error #418) surfaced, traced via `page.on('pageerror')` on the failing test's own page.
   Cause: the Dashboard's recent-activity timestamps render in the server process's local
   timezone while this spec's browser context is pinned to UTC. Fixed on the test-infra side by
   pinning `TZ=UTC` on the shared `webServer` in `playwright.config.mjs` (benefits every spec, not
   just this one) and in this story's own scripts. The underlying app defect (any real user whose
   browser timezone differs from the deploy environment's would hit the same crash) is reported to
   god, not fixed here — out of this story's scope.
3. **Defense-in-depth against residual timing flakiness** under heavy concurrent load: serial
   execution for this file's own tests, a `beforeAll` warm-up visiting every route once (Turbopack
   compiles routes on first request), and a reload-and-recheck fallback before failing.

Regenerated all six dashboard baselines with genuinely distinct content — `shasum` now shows all
three light/dark pairs differ. Proved theme-selectivity bidirectionally (not just "the two PNGs
differ"): a dark-only CSS change fails exactly the 3 dark dashboard tests and nothing else; a
light-only change fails exactly the 3 light dashboard tests plus the 8 light-only other-page
tests, and nothing else — see `bio-ux-014-theme-selectivity-proof.txt`.

**Disclosed, not chased further**: late in this round, full `test:e2e` runs showed a wider spread
of failures across several unrelated, already-accepted stories' own theme/dark-mode/axe tests
(none of which this story touches). `ps aux` showed a dozen accumulated `next-server`/Chromium
processes from many consecutive hours of floor-wide Playwright activity today. This story's own
scoped `test:visual` command remained clean and stable throughout (see
`bio-ux-014-r2-visual-clean-output.txt`); the wider suite's flakiness reads as ambient system
resource pressure, not a BIO-UX-014 regression, and is flagged to god as a floor-level
process-hygiene question rather than chased inside this story. `metadata.json`'s `test_command`
deliberately excludes the full `test:e2e` sweep for this reason — the same reasoning as excluding
`ObservabilityTests` from BIO-DEV-002's own reproducibility claim earlier this session.

Verified clean: `tsc --noEmit`, `next build`, unit 384/384, `test:visual` 14/14 (5+ consecutive
runs in an unloaded window).
