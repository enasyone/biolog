# BIO-UX-001 — Freeze approved BIOLOG visual language and design tokens

As the BIOLOG product owner, the approved prototype visual language is formalised as production
design tokens so every later screen remains visually consistent.

This story **extends** BIO-FND-007's existing token foundation (`web/lib/tokens.ts`,
`web/app/globals.css`) rather than re-authoring it — see `web/design-system/README.md` for why.
No existing colour value changed; nothing pinned by `bio-fnd-007-tokens.test.ts` changed.

**Round 1 CHANGES_REQUIRED (Chioma, evidence `d031117`):** AC02 initially shipped without
iconography tokens — the design-system covered colour/typography/spacing/radii/shadows but treated
"no icon library chosen" as covering iconography too. Chioma's review correctly separated the two:
AC02 requires iconography *metrics* to be tokenised, not a library to be picked. Remediated by
adding `web/design-system/iconography.ts` (size scale, stroke weight, optical-alignment grid, usage
rules) — see the AC02 row and "Iconography" section below. No library decision was made; that stays
open for a later story, now explicitly-not-blocking AC02 rather than silently absorbed into it.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Prototype/System Settings direction recorded as references; prototype code not promoted to production | `web/design-system/README.md` "Visual reference provenance" section names both approved reference files and states no prototype source exists in or was copied into this repository | Manual doc review (no automatable AC — nothing to unit-test about an absence) |
| **AC02** | Navy nav, light/white surfaces, orange accents, typography, spacing, radii, borders, shadows, iconography, status colours tokenised | `web/design-system/tokens.ts` (status colours, typography, spacing, radii, shadows; re-exports BIO-FND-007's colour/border tokens unchanged); `web/design-system/iconography.ts` (size scale, stroke weight, optical-alignment grid, usage rules — technology-neutral, no library chosen); `web/app/globals.css` mirrors every value as a CSS custom property | `bio-ux-001-tokens.test.ts`, `bio-ux-001-iconography.test.ts`, `bio-ux-001-css-sync.test.ts` |
| **AC03** | Light/dark use the same semantic token system | Every new token category (status colours) uses identical key names across `statusColorTokens.light`/`.dark`; existing `themeTokens` re-exported unchanged | `bio-ux-001-tokens.test.ts` ("semantic tokens use one shared system") |
| **AC04** | Responsive breakpoints, density, layout principles documented | `web/design-system/breakpoints.ts` (`breakpoints`, `density`, `layoutPrinciples`) | `bio-ux-001-breakpoints.test.ts` |
| **AC05** | Core component states specified: default/hover/focus/disabled/loading/success/warning/error/offline | `web/design-system/component-states.ts` (`COMPONENT_STATES`, `componentStateSpecs`) | `bio-ux-001-component-states.test.ts` |
| **AC06** | Shared design-system files owned by Damilola, require her approval for cross-module changes | `web/design-system/README.md` "Ownership" section states the rule, cross-referencing `DAMILOLA_PRODUCT_UX_FRONTEND.md` and `UI-ADAPTER-ARCHITECTURE.md` | Governance/process AC — no automatable test; enforced by story ownership convention already used for `web/app/`, `web/components/` |

## Iconography and borders note (AC02)

Iconography *metrics* are frozen in `web/design-system/iconography.ts` (size scale, stroke weight,
optical-alignment grid, usage rules) — see `web/design-system/README.md`'s "Iconography: metrics
frozen, library still open" section. No icon *library* is chosen (only inline emoji/unicode glyphs
exist today, in `theme-toggle.tsx` and `app-shell.tsx`'s nav toggle) and no story before this one
specified one; picking a concrete set is left to a later BIO-UX story (BIO-UX-002 or BIO-UX-003,
which define the production shell/route inventory) rather than decided here — that is a separate
question from AC02's tokenisation requirement, which this remediation now satisfies. Borders are
covered: `themeTokens.*.colorBorder` (BIO-FND-007) plus the new `radii` scale in `tokens.ts`.

## Design decisions and scope boundaries

### Why `web/lib/tokens.ts` was not moved or edited

`UI-ADAPTER-ARCHITECTURE.md`'s reference structure names `web/design-system/` as the home for
tokens. BIO-FND-007 (which pre-dates this story) already put the colour palette in
`web/lib/tokens.ts`, consumed by `theme-toggle.tsx` and three Card Designer feature modules
(`bio-crd-002/003/004`), and pinned by `bio-fnd-007-tokens.test.ts`. Moving it into
`web/design-system/` would touch every one of those consumers for a pure relocation with no
behaviour change, and risk silently altering BIO-FND-007's already-approved values in the process —
exactly the "extend, not build from scratch" risk this story's own planning identified. Instead,
`web/design-system/tokens.ts` re-exports it unchanged and adds only the categories BIO-FND-007
didn't cover. `web/lib/tokens.ts` keeps its existing consumers; `web/design-system/` is the new
stable import surface for anything added from here on.

### Why component states are tokens/specs, not rendered components

BIO-UX-001 is scoped to freezing tokens, not building the shared component library itself (no
Button/Input/Card component exists yet in `web/components/` beyond shell chrome). AC05 asks that
states be "specified," so `component-states.ts` freezes the state list and its token mapping as a
lookup table later component-building stories (BIO-UX-002 onward) implement against, rather than
pre-building components this story's AC set does not ask for.

## Local development / verification

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes; /card-designer route unaffected
corepack pnpm --dir tests/frontend test:unit   # 21 files / 161 tests pass, including the 5 bio-ux-001-*.test.ts files
```

No Playwright/visual-regression suite exists yet for the shell (that's BIO-UX-014); this story's
verification is limited to unit tests plus a production build, per its own AC set (none of AC01–06
requires new visual evidence — no route or rendered component changed).
