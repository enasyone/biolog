# TD-SHELL-NAVY-INVERSION — shell chrome colours and structure were backwards

The human compared the running app to the approved reference screenshots
(`reference/ui/INITIAL_PROTOTYPE_REFERENCE.png`,
`reference/ui/APPROVED_SYSTEM_SETTINGS_DIRECTION.png`) and caught a real defect no prior QA pass
checked for: the shell's navy/white chrome was backwards, and not just as a colour swap.

## What was verified against the reference images directly (not assumed from the dispatch's own read)

Both reference screenshots show the same structure:

- A **full-height NAVY sidebar** as its own left column — the BIOLOG wordmark at its top, then the
  grouped primary navigation, spanning the entire viewport height.
- A **WHITE top bar**, confined to the content column only (to the right of the sidebar) — global
  search, tenant/role selectors, theme toggle, notifications, user avatar. It does **not** span
  above the sidebar.

The running app (confirmed on the live dev server before any fix) had this backwards on both axes:

- `.biolog-header` (top bar) was hardcoded navy, `.biolog-primary-nav` (sidebar) was the light
  surface colour — the colours were swapped.
- `.biolog-shell`'s grid used `grid-template-areas: 'header header' 'nav main'` — the header spanned
  the **full width above both** the sidebar and the content, not confined to the content column.

This has been wrong since BIO-FND-007's original shell. Every QA pass since (axe, BIO-UX-013's
app-wide responsive/keyboard/state sweep, BIO-UX-014's visual-regression baselines) checked function
and internal consistency, never fidelity against the actual approved reference screenshots — that
blind spot could be hiding elsewhere (spacing, typography, iconography), flagged for whoever picks
up UI-fidelity work next.

## Fix

All changes are in `web/app/globals.css` (shell classes) and `web/components/app-shell.tsx` (the
shared shell component) — no domain page/component was touched.

### 1. Grid restructure (`.biolog-shell`, `>= 768px`)

`grid-template-areas` changed from `'header header' 'nav main'` to `'nav header' 'nav main'` — the
sidebar now spans both grid rows (full height, one column), the header occupies only the top-right
cell (confined to the content column), matching the reference exactly.

### 2. Colour swap (`.biolog-header`, `.biolog-primary-nav`)

`.biolog-header` now uses `--biolog-color-surface`/`--biolog-color-text` (light); `.biolog-primary-nav`
now uses `--biolog-color-brand-navy`/`--biolog-color-on-navy` (navy). Every on-navy-tuned child style
that assumed the *old* (backwards) header colour was updated to its on-light equivalent:
`.biolog-search-input`, `.biolog-context-select select`, `.biolog-context-select-label`. Nav's own
children (`.biolog-nav-group-label`, `.biolog-primary-nav a`/`:hover`) were updated to on-navy
equivalents — `.biolog-nav-group-label` reuses the same "opacity on the on-navy colour" convention
`.biolog-context-select-label` used before this fix (there replaced by a real muted-text token now
that its own background is light).

The border-right that previously separated the (light) sidebar from the (light) main content was
removed — the navy/white colour contrast is now the boundary, matching the reference (no visible
seam line in either screenshot).

### 3. The brand mark moves into the sidebar, without removing it from the mobile header

The reference shows the BIOLOG wordmark living in the navy sidebar, not the top bar. `app-shell.tsx`
gained a new `.biolog-nav-brand` block at the top of `<nav>`, containing its own
`<Link>`/`<BrandMark>` — shown whenever the nav itself is visible (permanently at `>= 768px`, inside
the open drawer on mobile).

The header's *existing* brand link is kept (mobile needs a visible brand even when the drawer is
off-canvas by default) but hidden at `>= 768px` via `.biolog-header .biolog-brand-link { display:
none }`, so desktop never shows the brand twice. `display: none` correctly removes it from the tab
order and accessibility tree at that breakpoint (the same technique `.biolog-nav-toggle` already
used) — verified by a dedicated test (`td-shell-navy-inversion.spec.ts`: "exactly one reachable
'BIOLOG home' link exists, and it is inside the sidebar" at desktop; "the header keeps its own
visible brand link" at mobile).

**Known gap, flagged not silently worked around**: the only wordmark asset in the repo,
`biolog-wordmark-light.svg`, is documented (`web/public/brand/README.md`) as "for use on light
surfaces" — its text is dark grey (`#4D4D4D`), illegible directly on navy. The existing
`.biolog-brand-plate` white-box wrapper (already used for exactly this contrast reason before this
fix) is kept around the logo in its new sidebar position. The reference screenshots appear to show
the wordmark directly on navy without a plate (implying a dedicated on-navy brand asset in the
original prototype's asset pack that was never copied into this repo by BIO-FND-007). Closing this
exactly needs a real on-navy wordmark asset — a brand-asset decision, not a shell-layout one — so
it's flagged here rather than the fix silently inventing a recoloured logo.

### 4. DOM order: nav now precedes header

Moving the header's `.biolog-brand-link` into the sidebar-adjacent visual area is not achievable
with a grid-area reassignment alone: a CSS Grid item's `grid-area` only repositions a *direct child*
of the grid container, and the brand link was nested inside `.biolog-header`, a sibling of `.biolog-primary-nav`
— reassigning `.biolog-header`'s own grid area moves the whole header (search, selectors, theme,
user menu included) as one block, not one child out of it into a different sibling's box.

Because of this, `<nav>` now precedes `<header>` in `app-shell.tsx`'s JSX, matching the sidebar's
new full-height, visually-first position. This is a deliberate, tested tab-order change, not an
oversight:

- **Desktop** (sidebar permanently visible): tab order is now sidebar (brand, nav links) → header
  (search, tenant/role, theme, user menu) → main — matching the new left-to-right visual layout.
  `bio-ux-002-shell.spec.ts`'s "every header control is reachable by keyboard in document order"
  test was updated to start from the search box (the header's own first control) rather than the
  brand link, since the brand link is no longer part of the header's own internal tab sequence at
  this breakpoint.
- **Mobile** (sidebar off-canvas by default): unaffected. `visibility: hidden` already excludes the
  whole closed drawer (BIO-FND-007-QA-01) from the tab order regardless of its DOM position, so tab
  order there is unchanged: header (toggle, brand, search, selectors, theme, user menu) → main. When
  the drawer is opened, its own brand link and nav links become reachable ahead of the header's
  remaining controls — reasonable for an actively-opened overlay, and not something any existing
  test asserted differently.
- `bio-fnd-007-shell.spec.ts`'s "the viewer demo session shows only unrestricted items plus its
  granted group" test asserted an exact link count inside `<nav>` (3) — now legitimately 4, since
  the sidebar's own brand link is an additional, permission-independent link inside the nav
  landmark. Updated the count, not the assertion's intent.

## What did NOT change

- No domain page or component's own markup — every fix is in the shared shell file/component only,
  per the dispatch's explicit scope.
- No new functionality: no sidebar-collapse control was added even though the reference shows a
  hamburger icon next to the navy sidebar's logo at desktop — the app has no desktop sidebar-collapse
  behaviour today, and adding one would be a new feature, not a colour/structure fix. The mobile-only
  nav-toggle (`.biolog-nav-toggle`) is unchanged, still hidden at `>= 768px`.
- No active/current-nav-item highlighting was added, despite the reference showing a highlighted row
  for the current page — this shell has never had that behaviour; out of this fix's scope.
- `--biolog-color-brand-navy`/`--biolog-color-on-navy`/`--biolog-color-surface`/`--biolog-color-text`
  token *values* are untouched (BIO-UX-001's frozen palette) — only which shell element consumes
  which token changed. `bio-ux-002-shell.spec.ts`'s existing token-value pin test
  (`--biolog-color-brand-navy` === `#0b2341`, `--biolog-color-accent` === `#f4502a`) needed no
  change, confirming this.

## BIO-UX-014 visual-regression baselines

Regenerated per `docs/features/bio-ux-014/README.md`'s documented procedure
(`corepack pnpm --dir tests/frontend test:visual:update`), since the shell's chrome legitimately
changed — this is expected baseline drift from an approved visual change, not something to explain
away. All 14 baseline PNGs changed (`git diff --stat` on the snapshots directory); spot-checked the
dashboard desktop light/dark baselines directly and confirmed the corrected navy-sidebar/white-header
structure renders as expected in both themes.

## Verification

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — 34 routes, all prerendered
corepack pnpm --dir tests/frontend test:unit   # 384/384 pass, unchanged
corepack pnpm --dir tests/frontend test:e2e    # 360/360 pass (was 350 pre-existing + 1 new spec's
                                                # 4 tests + 2 test-count updates in existing specs
                                                # for the intentional link-count/tab-order changes)
corepack pnpm --dir tests/frontend test:visual # 14/14 pass against the regenerated baselines
```

New coverage: `tests/frontend/playwright/td-shell-navy-inversion.spec.ts` (4 tests) — desktop colour
assignment (sidebar navy, header surface, via the live `--biolog-color-*` custom property values,
not hardcoded hex), desktop structure (sidebar spans the full shell height, header confined to the
content column, via bounding-box geometry), exactly-one-reachable-brand-link at desktop (inside the
sidebar), and the header's own brand link staying reachable at mobile.
