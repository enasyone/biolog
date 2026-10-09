# BIO-UX-013 — Builder evidence

**Story:** Complete responsive, accessibility, keyboard and application-state coverage
**Owner:** Damilola — Product UI/UX & Frontend Lead
**Worktree:** `biolog-worktrees/BIO-UX-013`, branch `feature/BIO-UX-013` off `develop`
**Dependencies:** BIO-UX-012 (merged; develop was at 376/376 unit tests before this story)
**Conflict group:** `ui-quality`
**Commit:** `48aeb49b21215eaad681ad44269d6c591229e0e9`

Full scope reasoning, every finding, and the fix for each in
`docs/features/bio-ux-013/README.md`. This is a QA-hardening pass across the whole app, not new
screens — per the dispatch's own framing.

## AC to evidence map

| AC | Coverage | Result |
|---|---|---|
| AC01 (breakpoints) | `bio-ux-013-breakpoints.spec.ts` — 8 major pages × 3 breakpoints (50 tests) | 2 real shared-CSS overflow bugs found, fixed |
| AC02 (keyboard/focus/landmarks) | `bio-ux-013-keyboard-nav.spec.ts` (9 tests) | Clean — already correctly built |
| AC03 (axe baseline) | `bio-ux-013-axe-baseline.spec.ts` — full sweep, all 31 static routes, light mode (31 tests) | Zero serious/critical violations |
| AC04 (state consistency) | Direct CSS audit + `bio-ux-013-state-consistency.spec.ts` (3 tests) | 2 real inconsistencies found, fixed |
| AC05 (edge-case data) | `bio-ux-013-edge-case-data.spec.ts` (5 tests) | 2 more instances of the AC01 grid bug + a shared-table scroll gap, all found and fixed |
| AC06 (dark/light parity) | Same full sweep as AC03, dark mode (31 tests) + 1 shared test covering the dynamic route in both modes | Zero serious/critical violations |

## Real bugs found and fixed (5 total, all in web/app/globals.css)

1. **`.biolog-shell`'s bare `1fr` grid column** forced the whole page wider than the viewport
   whenever a page's content (e.g. Cards' table) was wide enough — the classic CSS Grid
   automatic-minimum-size gotcha. Fixed with `minmax(0, 1fr)`. Once understood, grepped for the
   same pattern and found it recurring 4 more times
   (`.biolog-placeholder-cards`, `.biolog-identity-dl`, `.biolog-identity-detail-grid`,
   `.biolog-op-grid`) — fixed all of them pre-emptively rather than waiting for more test
   failures to find each one individually.
2. **`.biolog-identity-dl dd` had no `overflow-wrap`** — a genuinely unbroken long name (AC05's
   own scenario) still overflowed its own now-bounded column by a few pixels. Added
   `overflow-wrap: break-word`.
3. **`.biolog-table` had no `overflow-x` boundary of its own** (unlike Cards' page-specific
   `.tableWrap`) — Audit/Reports/Attendance/Devices, which use the shared class directly, had
   nowhere for a too-wide table to scroll once the grid-track fix stopped the page itself from
   growing to fit it. Fixed at the shared class (`display: block; overflow-x: auto`), benefiting
   every consumer, not just Audit.
4. **`.biolog-identity-status--empty` was missing `font-style: italic`** — the one visible
   difference between three otherwise-identical empty-state patterns in the app
   (`.biolog-empty-state`, `.biolog-op-empty`, this one).
5. **`.biolog-identity-status--offline` used `--biolog-color-error` instead of
   `--biolog-color-offline`** — the same semantic "offline" state rendered two different colours
   depending on which part of the app you were in; `.biolog-placeholder-status--offline` already
   had this right.

## What was checked and found clean (not assumed clean)

- AC02: landmarks (banner/navigation/main), skip link, focus-visible indicator, and a bounded
  no-keyboard-trap sweep (60 Tab presses, asserting no 3-in-a-row repeat on the same control)
  across Settings and the multi-step Document Verify journey. All passed first try.
- AC03/AC06: axe-core serious/critical violations across every one of the app's 31 static routes
  plus the one dynamic route (`/people/[personId]`), in both light and dark mode — 63 checks, 0
  violations. An independent re-verification of every prior story's own axe work, not a re-run of
  the same assertions.

## Developer tests (5 new spec files, 76 new Playwright tests; 376 unit tests unchanged)

- `bio-ux-013-axe-baseline.spec.ts` (63 tests): AC03/AC06, full route sweep.
- `bio-ux-013-breakpoints.spec.ts` (50 tests): AC01, 8 pages × 3 breakpoints, no-overflow +
  nav-pattern-matches-BIO-UX-001 + primary-action-reachable checks.
- `bio-ux-013-keyboard-nav.spec.ts` (9 tests): AC02.
- `bio-ux-013-state-consistency.spec.ts` (3 tests): AC04, proving both fixes.
- `bio-ux-013-edge-case-data.spec.ts` (5 tests): AC05, incl. a real 30+ character unbroken typed
  name through the Enrolment wizard.
- Full suite run against a real Chromium browser and real `next dev`/`next build` in this session:
  348/348 Playwright (up from 331 pre-existing), 376/376 unit, zero regressions from any of the
  five CSS fixes.

## Decisions and assumptions

- **Scope split by what's cheap to fully automate vs. what benefits from depth on a
  representative set** — full sweep for AC03/AC06 (axe), a curated 8-page set for AC01/AC02/AC05.
  Full reasoning in `docs/features/bio-ux-013/README.md`'s "Scope decision" section, since god's
  dispatch explicitly left this call to the story owner.
- **All fixes stayed in `web/app/globals.css`** (this story's own shared-shell ownership) — no
  domain component markup was touched, consistent with this being a QA-hardening pass, not new
  feature work in any one domain.
- **`.biolog-table`'s fix (`display: block; overflow-x: auto` on the table itself, not a wrapper
  div)** was chosen over updating every consuming page's markup to add its own wrapper — smaller
  blast radius, and every current and future consumer of the shared class benefits automatically.

## Known gaps / local-session notes (not blocking)

- Full-repo `scripts/quality.sh` was not run (same precedent as every prior BIO-UX-00x story this
  session: unrelated backend/.NET/infra suites, no bearing on a frontend-only story).
- `next-env.d.ts` (auto-regenerated by `next dev`, "should not be edited" per its own header) was
  intentionally left out of the commit.
- The representative-set choice for AC01/AC02/AC05 (8 pages) does not individually re-verify
  every one of the app's 31 routes for breakpoint/keyboard/edge-case behaviour — the axe sweep
  (AC03/AC06) is the one dimension checked exhaustively. If a future story adds a genuinely new
  layout pattern (not sharing `.biolog-table`/`.biolog-op-grid`/`PlaceholderPage`), it should get
  its own breakpoint/keyboard check rather than assuming this story's coverage extends to it.
