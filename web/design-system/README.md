# web/design-system/

BIOLOG's frozen visual language as production design tokens (**BIO-UX-001**). This directory is
the canonical home for tokens/typography/layout/theme primitives per
`02-ARCHITECTURE/UI-ADAPTER-ARCHITECTURE.md`'s reference structure.

## Visual reference provenance (BIO-UX-001-AC01)

Every token in this directory traces to one of two approved references, both recorded in
`BIOLOG_Orchestration_Pack_v2.3/reference/ui/`:

- `INITIAL_PROTOTYPE_REFERENCE.png` — original information architecture, dark navy left
  navigation, light workspace, tenant/role context in the top bar, existing module naming.
- `APPROVED_SYSTEM_SETTINGS_DIRECTION.png` — approved refinement: cleaner typography/spacing,
  restrained card styling, orange principal actions, navy structure/headings, production form
  density.

Those files (and `reference/ui/README.md`, which states the rule) are visual references only.
**No prototype source code from that reference set is promoted as production code here** — every
file in this directory is original TypeScript/CSS written against those references, not copied
from a prototype codebase (none exists in this repository).

The brand colour values themselves (`web/lib/tokens.ts`, BIO-FND-007) were sourced from the
approved BIOLOG wordmark asset and the Card Designer v0.3 specification, as documented in that
file's own header comment — this story does not re-derive them, only extends the token set around
them (status colours, typography, spacing, radii, shadows — see `tokens.ts`).

## What's frozen here (BIO-UX-001-AC02, AC03)

| File | Covers |
|---|---|
| `tokens.ts` | Re-exports the BIO-FND-007 colour palette (`brand`, `themeTokens`, `tokenCssVar`); adds status colours (success/warning/error/info/offline), typography scale, spacing scale, radii and shadows — each with light/dark values where the category is theme-sensitive. |
| `iconography.ts` | Icon size scale, stroke weight, optical-alignment grid and semantic usage rules (AC02) — technology-neutral; no icon library is chosen (see "Iconography: metrics frozen, library still open" below). |
| `breakpoints.ts` | Responsive breakpoints and density rules (AC04). |
| `component-states.ts` | The nine required component states and which tokens express each (AC05). |
| `index.ts` | Stable barrel import (`from '../design-system'`) for later stories. |

Light and dark use the same semantic token *names* throughout (AC03): a consumer never branches
on theme, it reads `themeTokens[theme].colorAccent` or the matching CSS custom property, and the
value resolves correctly for whichever theme is active. `web/app/globals.css` mirrors every value
in `tokens.ts`/`breakpoints.ts` as a CSS custom property by hand — the same pattern BIO-FND-007
established for the original colour set — and `bio-ux-001-css-sync.test.ts` pins the two together.

`body`'s `font-family` in `globals.css` is intentionally left as a literal value equal to
`typography.fontFamily` rather than switched to `var(--biolog-font-family)`, and no `font-size`/
`line-height` was added to `body`: BIO-UX-001 tokenises typography, it does not yet apply the
scale to the shell's rendered output. Applying `--biolog-font-size-base`/`--biolog-line-height-base`
to `body` would be a visible metric change (currently unset, defaulting to the browser's base
size) outside a "freeze the tokens" story's scope, and the shell has no visual-regression baseline
yet (that's BIO-UX-014) to catch an unintended drift. A later story applies the scale to real
component markup once it exists.

## Iconography: metrics frozen, library still open (BIO-UX-001-AC02)

AC02 requires iconography to be tokenised; it does not require an icon library to be chosen. Round
1 of this story's own acceptance review (Chioma) correctly rejected treating "no library chosen" as
satisfying AC02 — the two are separable, the same way `typography` tokenises a type scale without
this story picking a webfont vendor. `iconography.ts` freezes the technology-neutral part:

- **Size** — `iconSize`: `iconSizeSm`/`iconSizeMd`/`iconSizeLg` (1rem/1.25rem/1.5rem), matched to
  context (inline text / nav-button-row / page-header, respectively).
- **Stroke weight** — `iconStrokeWeight`: `iconStrokeRegular`/`iconStrokeBold`, SVG `stroke-width`
  values for a stroke-based icon set.
- **Optical alignment** — `iconAlignment`: a shared `iconGrid` (24×24) every icon is authored
  against regardless of rendered size, an `iconOpticalInsetPercent` for glyphs that read visually
  heavier than a line icon at the same box, and `iconBaselineAlign` for inline-with-text placement.
- **Usage rules** — `iconUsageRules`: colour inheritance (`currentColor`, status icons use their
  matching status-colour token instead), accessible naming for icon-only controls, and which size
  applies at which surface.

Choosing a concrete icon set (an SVG sprite sheet, an icon font, a component library such as
Lucide/Feather) is still left to a later story (BIO-UX-002 or BIO-UX-003, which define the
production shell/route inventory) — picking one here would bind every later domain story's icon
usage to a decision this story's AC set doesn't ask for. Whichever set is chosen later sizes and
aligns its icons against these tokens rather than introducing new ones.

## Responsive breakpoints and density (BIO-UX-001-AC04)

See `breakpoints.ts` for the exact pixel values and `layoutPrinciples` for the documented nav/
content behaviour per breakpoint. Desktop/tablet/mobile follow "compact enterprise density with
generous whitespace" (`PRODUCT-EXPERIENCE.md`): compact row/control sizing by default at tablet
and desktop widths, comfortable (larger touch targets) at mobile widths.

## Core component states (BIO-UX-001-AC05)

`component-states.ts` freezes nine states — default, hover, focus, disabled, loading, success,
warning, error, offline — as the superset a shared control (button/input/card/badge) must support,
each mapped to the token(s) that express it. Page-level states from `PRODUCT-EXPERIENCE.md`
(empty, permission-denied) are route-level compositions of these primitives plus content, not a
single control's state, so they're a later story's concern (BIO-UX-003's interaction-state
contract), not tokenised here.

## Ownership (BIO-UX-001-AC06)

Per `05-MUNDER/PROMPTS/DAMILOLA_PRODUCT_UX_FRONTEND.md` ("Ownership") and
`02-ARCHITECTURE/UI-ADAPTER-ARCHITECTURE.md` ("Ownership"): **Damilola owns every file under
`web/design-system/`.** Domain engineers must not edit these files directly; a cross-module change
(a new status colour, a new breakpoint, a new component state) requires Damilola's recorded
approval — the same rule that already governs `web/app/` and `web/components/`. Domain feature
work belongs under `web/features/<domain>/` and consumes these tokens, it does not modify them.

## Local development

No new install step — this directory has no dependencies beyond what `web/package.json` already
provides. `corepack pnpm --dir web typecheck` type-checks it as part of the existing `web/`
project.
