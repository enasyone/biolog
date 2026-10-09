# BIO-UX-013 — Complete responsive, accessibility, keyboard and application-state coverage

As a BIOLOG user, the complete simulated application must remain usable across supported screens,
keyboard navigation and common loading/error/offline states.

## Scope decision

AC01-06 span the whole app (31 static routes + 1 dynamic), and god's dispatch explicitly left
scope/depth to this story's own judgement. Rather than either a token sample or an exhaustive
33-page-times-6-AC matrix, scope was split by what's actually cheap and valuable to automate
fully versus what benefits more from depth on a representative set:

- **AC03 (axe baseline) and AC06 (dark-mode parity): a full sweep, not a sample.** Every static
  route, light and dark, since looping axe-core over a route list is cheap and gives real total
  coverage rather than a guess at which pages might have a problem.
- **AC01 (breakpoints), AC02 (keyboard), AC05 (edge-case data): a representative set of 8 major
  pages**, one per distinct interaction pattern already in the product (Dashboard, a filterable
  list/table, a dense multi-section form, a multi-step wizard, a table with row actions, an
  operational form + shared scenario picker, Administration, Reports) — not all 31 routes, since
  most routes reuse the same handful of shared patterns (`PlaceholderPage`, `.biolog-table`,
  `.biolog-op-grid`) and testing every instance of the same pattern adds little over testing the
  pattern itself once, well.
- **AC04 (state consistency): started with an audit**, not a test-first pass — read the actual CSS
  for every "empty/loading/error/permission-denied/success/offline" pattern in the app before
  writing anything, since AC04 is fundamentally a consistency question a test can only catch if it
  already knows what "consistent" should mean here.

## Acceptance criteria

| AC | Requirement | Coverage | Real findings |
|---|---|---|---|
| **AC01** | Major pages validated at desktop/tablet/mobile | `bio-ux-013-breakpoints.spec.ts` — 8 pages × 3 breakpoints, no-overflow + nav-pattern + primary-action-reachable checks | 2 shared grid-CSS overflow bugs found and fixed (see below) |
| **AC02** | Keyboard-only nav, focus order, labels, landmarks | `bio-ux-013-keyboard-nav.spec.ts` — landmarks on 5 pages, skip link, focus-visible, no-trap sweeps on Settings and Document Verify | None — already correctly built |
| **AC03** | Axe baseline, no unresolved serious/critical in primary journeys | `bio-ux-013-axe-baseline.spec.ts` — full sweep, 31 static routes + the 1 dynamic route, light mode | None — zero serious/critical violations across every route |
| **AC04** | Empty/loading/error/permission-denied/success/offline states visually consistent | `bio-ux-013-state-consistency.spec.ts`, informed by a direct CSS audit | 2 real inconsistencies found and fixed (see below) |
| **AC05** | Long Nigerian names/missing optional fields/dense tables don't break layout | `bio-ux-013-edge-case-data.spec.ts` | 2 more instances of the AC01 grid bug, plus a shared-table scroll gap (see below) |
| **AC06** | Dark/light theme parity for shared components | Same full sweep as AC03, dark mode, in `bio-ux-013-axe-baseline.spec.ts` | None — zero serious/critical violations in dark mode across every route |

## Real bugs found and fixed (not assumed away)

All five fixes below are in `web/app/globals.css` (shared shell/design-system, this story's own
ownership) — none required touching any domain's own component markup.

### 1. A CSS Grid `1fr`-track overflow bug, found independently 3 times, fixed everywhere it existed

`.biolog-shell`'s mobile-width grid used a bare `grid-template-columns: 1fr`. A bare `1fr` track's
*automatic minimum* is its content's min-content size — so a page with wide-enough content (e.g.
Cards' 5-column table) forced the whole grid, and the page itself, wider than the viewport at
mobile/tablet widths, discovered by `bio-ux-013-breakpoints.spec.ts`'s horizontal-overflow check
on `/cards`. The fix is the standard one: `minmax(0, 1fr)` caps the track's automatic minimum at
zero instead of content-based, letting a wide descendant's own `overflow-x` actually engage
instead of growing its ancestor.

Once this pattern was understood, a direct grep for every remaining bare `1fr`/`2fr 1fr`
`grid-template-columns` in `globals.css` found it recurring twice more, both fixed the same way,
both caught only because the search was done rather than assumed to be a one-off:

- `.biolog-placeholder-cards` (the two-column card grid used across the whole placeholder-shell
  pattern — caught by AC05's dense-table test on `/audit`).
- `.biolog-identity-dl` (BIO-UX-006's definition-list, used on Enrolment's Review step and
  person detail pages — caught by AC05's own long-name test).
- `.biolog-identity-detail-grid` and `.biolog-op-grid` (person detail page; all five BIO-UX-010
  operational pages) — fixed pre-emptively once the pattern was confirmed, not waiting for a 4th
  and 5th test failure to find them independently.

### 2. `.biolog-identity-dl dd` had no `overflow-wrap`

Even with the grid track fixed, a genuinely long *unbroken* value (no spaces to wrap at — exactly
AC05's "long Nigerian names" case, e.g. `Oluwadamilareoluwaseunfunmilayo Adegboyegabalogun` typed
into Enrolment's free-text name fields) still overflowed its own bounded column by a few pixels,
since nothing told the browser it was allowed to break the word itself. Added
`overflow-wrap: break-word` to `.biolog-identity-dl dd`.

### 3. `.biolog-table` had no scroll boundary of its own

Cards' own `.tableWrap` (page-specific CSS) already wraps its table in `overflow-x: auto`, but the
*shared* `.biolog-table` class (used directly, with no wrapper, by Audit, Reports, Attendance,
Devices and others) had none — so a table wider than its container, once the grid-track fix above
stopped the *page* from growing to fit it, had nowhere to go and pushed past the viewport edge by
its own overflow instead (caught by AC05's dense-table test on `/audit`). Added
`display: block; overflow-x: auto` directly to `.biolog-table` itself, so every current and future
consumer of the shared class gets a working scroll boundary without needing its own wrapper.

### 4. `.biolog-identity-status--empty` was missing `font-style: italic`

Three "empty state" patterns exist in the app (`.biolog-empty-state` on the dashboard,
`.biolog-op-empty` on the five operational pages, `.biolog-identity-status--empty` on BIO-UX-006's
identity screens) — the first two are visually identical (muted colour + italic); the third was
missing the italic, a real, if small, AC04 visual-consistency gap. Fixed by adding
`font-style: italic` to `.biolog-identity-status--empty`.

### 5. `.biolog-identity-status--offline` was coloured as an error, not as offline

`--biolog-color-offline` is a distinct, frozen token from `--biolog-color-error` specifically so
"the connection is down" and "the action failed" can look different — `.biolog-placeholder-status
--offline` (BIO-UX-005/011) already gets this right, but `.biolog-identity-status--offline`
(BIO-UX-006) used `--biolog-color-error` instead, meaning the same semantic "offline" state
rendered as two different colours depending on which part of the app you were in. Fixed by
switching `.biolog-identity-status--offline` to `--biolog-color-offline`.

## What was checked and found clean (not assumed clean)

- **AC02**: landmarks, skip link, focus-visible, and a bounded no-keyboard-trap sweep (60 Tab
  presses with no 3-in-a-row repeat on the same control) across Settings and the multi-step
  Document Verify journey. All passed on the first run — no gaps found.
- **AC03/AC06**: axe-core's serious/critical violations, across every static route, light and
  dark. 63 checks, zero violations. This is a genuine, independent re-verification of every prior
  story's own axe work, not a re-run of the same assertions — and it held.

## Local development / verification

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — 34 routes, all prerendered
corepack pnpm --dir tests/frontend test:unit   # 376 tests (50 files) pass, unchanged
corepack pnpm --dir tests/frontend test:e2e    # 348 tests pass — the full existing suite plus
                                                # this story's 5 new spec files, zero regressions
```
