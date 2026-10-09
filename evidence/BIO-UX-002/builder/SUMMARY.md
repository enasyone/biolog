# BIO-UX-002 — Builder evidence

**Story:** Build production application shell and permission-aware navigation
**Owner:** Damilola — Product UI/UX & Frontend Lead
**Worktree:** `biolog-worktrees/BIO-UX-002`, branch `feature/BIO-UX-002` off `develop`
**Dependency:** BIO-UX-001 (DONE, merged `a4fdfb9`)
**Conflict group:** `ui-shell`
**Commit:** `61a5ed4cec3b5414ee0c5c0fdad90b6b729da4c0`

## AC to evidence map

See `docs/features/bio-ux-002/README.md` for the full AC table with implementation references and
design-decision rationale (route-inventory sourcing, the tenant/role selector redesign, the
on-accent contrast fix). Summary:

| AC | Evidence |
|---|---|
| AC01 (search, tenant/role selectors, user menu, theme) | `app-shell.tsx`, `search-bar.tsx`, `user-menu.tsx`, `session-context.tsx`; `bio-ux-002-shell.spec.ts` |
| AC02 (approved product areas, permission visibility) | `nav-items.ts` (27-item catalog, 7 groups), `groupNavItems`; `bio-ux-002-nav-groups.test.ts`, `bio-fnd-007-permissions.test.ts` (unmodified, still passing) |
| AC03 (responsive, keyboard, focus states) | Native controls throughout; `bio-ux-002-shell.spec.ts` keyboard describe block |
| AC04 (every route real) | 34 routes (was 2); `bio-ux-002-shell.spec.ts` route-inventory loop; `next build` route manifest |
| AC05 (no feature logic in shell) | `placeholder-page.tsx` (plain-string props only); `bio-ux-002-shell.spec.ts` no-`/api/`-call test |
| AC06 (approved branding, no redesign) | Brand-token pin test unchanged; grouped-sidebar visual matches the reference screenshot directly; one documented colour decision (see README "On-accent text contrast") |

## Developer tests (23 unit files / 172 tests, 5 Playwright spec files / 102 tests)

- New unit: `bio-ux-002-nav-groups.test.ts` (6), `bio-ux-002-search-fixtures.test.ts` (5).
- New Playwright: `bio-ux-002-shell.spec.ts` (47 tests across route inventory, no-dead-button,
  search, tenant/role selectors, user menu, keyboard/focus, no-feature-logic, branding, and a
  4-test axe accessibility block).
- `bio-fnd-007-shell.spec.ts`: 2 of its AC03 nav-content assertions were updated to the new nav
  catalog's item names (e.g. "Biometrics" doesn't exist as a single nav label any more — replaced
  by "Fingerprints"/"Facial Recognition" — so the assertion now checks "Devices" instead, which is
  equally admin-only). The underlying mechanism under test (permission-gated visibility) is
  unchanged BIO-FND-007 code; every other test in that file, and every BIO-CRD-002/003/004 spec,
  passes unmodified.
- Full suite run against a real Chromium browser and a real `next dev` server in this session
  (port 3212 — 3000/3100/3211 were already bound by other local processes on this shared machine
  across this session's earlier BIO-UX-001 work).

## Real defects found and fixed via the axe accessibility scan (not assumed away)

Two WCAG AA colour-contrast failures were caught by `@axe-core/playwright`, not just written and
left unverified:

1. `.biolog-user-avatar` (initials button): white text on brand orange measured 3.48:1 (axe-core),
   confirmed independently by hand (relative-luminance calculation) — under the 4.5:1 AA minimum
   for normal-size text.
2. `.biolog-button-primary` (every placeholder page's CTA): same root cause, same measurement.

Both were fixed by using `var(--biolog-color-brand-navy)` as the foreground instead of
`--biolog-color-accent-contrast` (~4.65:1, comfortably AA) — a new colour decision for these two
new components, not an edit to BIO-UX-001's frozen token, which stays white and untouched
everywhere BIO-FND-007 already used it. Re-ran the full axe suite after the fix: clean.

A third axe/ARIA issue was found and fixed in the search results: an initial `role="combobox"`/
`role="listbox"`/`role="option"` pattern (without the arrow-key keyboard support that pattern
requires) produced both a "focusable descendants" violation and an "aria-allowed-attr" violation.
Replaced with a plain `<input type="search">` plus an `aria-live="polite"` results region — no
half-implemented ARIA widget, no violations.

## Decisions and assumptions

- **Nav catalog and route sourcing.** Every route traces to `governance/ui-integration-map.json`
  where one exists; the handful that don't (`/alumni`, `/contractors`, `/nfc-uhf`,
  `/mobile-credentials`, `/administration` landing) are individually justified in
  `docs/features/bio-ux-002/README.md`, not silently invented.
- **"Visitor Management" vs. "Visitors" ambiguity.** `PRODUCT-EXPERIENCE.md` names both;
  `ui-integration-map.json` defines only one route (`/visitors`, `VisitorService`). Resolved to one
  nav item pointing at the one mapped route, flagged for BIO-UX-003 (the full route-manifest story)
  to resolve explicitly rather than guessing a second, unmapped route into existence.
- **Card Designer kept as its own nav entry** despite not appearing in the reference screenshot's
  visible CREDENTIALS section — `PRODUCT-EXPERIENCE.md` (a higher source per the reading order)
  names it explicitly, and a real, tested page already exists there from BIO-CRD-002/003/004.
- **Tenant/role now two independent, selectable axes** instead of one atomic demo "profile" — a
  second synthetic tenant (`Beta Polytechnic (SYNTHETIC)`) was added so AC01's tenant selector has
  something real to select between; `?as=` still works for role, `?tenant=` is new.
- **Search has no live backend** — a tiny fixed fixture list (`search-fixtures.ts`) is the
  deterministic SIMULATION, clearly labelled as such in the empty-results state.

## Known gaps / local-session notes (not blocking)

- Full-repo `scripts/quality.sh` was not run for this story (it exercises unrelated backend/.NET/
  infra suites and previously took several minutes with no bearing on a frontend-only change — see
  BIO-UX-001's own evidence for the precedent and the confirmed-pre-existing BIO-ARC-006/BIO-FND-008
  failures it surfaces regardless of this story's changes). The direct, AC-relevant commands (`web/`
  typecheck+build, full `tests/frontend` unit and Playwright suites) all ran to completion and pass.
- No icon library is chosen (BIO-UX-001's own open item); this story's new controls (search,
  selectors, user menu) use text/native-control affordances only, no icons, so that gap is not
  compounded here.
