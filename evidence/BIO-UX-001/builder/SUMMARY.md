# BIO-UX-001 — Builder evidence

## Round 2 — CHANGES_REQUIRED remediation (Chioma, evidence `d031117`)

Round 1 acceptance rejected: AC02 requires iconography to be tokenised, and treating "no icon
library chosen" as covering that was wrong — the token contract (sizes, stroke weight, optical
alignment, usage rules) does not require choosing a library. Remediated:

- Added `web/design-system/iconography.ts`: `iconSize` (sm/md/lg), `iconStrokeWeight`
  (regular/bold), `iconAlignment` (grid/optical inset/baseline align), `iconUsageRules` (colour
  inheritance, accessible naming, size-by-surface).
- Mirrored as CSS custom properties in `globals.css` (`--biolog-icon-size-*`,
  `--biolog-icon-stroke-*`), pinned by an extended `bio-ux-001-css-sync.test.ts`.
- New `tests/frontend/unit/bio-ux-001-iconography.test.ts` (4 tests). Full suite now **21 files /
  161 tests** (up from 20/156), zero regressions.
- `web/design-system/README.md`'s new "Iconography: metrics frozen, library still open" section
  and `docs/features/bio-ux-001/README.md`'s updated AC02 row/note explain the boundary: metrics
  are frozen now; the library choice is still explicitly deferred to BIO-UX-002/003, and that
  deferral no longer stands in for AC02 itself.
- `evidence/BIO-UX-001/acceptance/iconography-probe.sh` (Chioma's reproduction script) requires
  `rg` (ripgrep), not installed in this session's sandbox; `round2-iconography-probe-output.txt`
  records the identical `grep -E` check against the same regex and same paths, non-empty (PASS).

Commit: see round-2 commit hash in `metadata.json`.

## Round 1

**Story:** Freeze approved BIOLOG visual language and design tokens
**Owner:** Damilola — Product UI/UX & Frontend Lead
**Worktree:** `biolog-worktrees/BIO-UX-001`, branch `feature/BIO-UX-001` off `develop`
**Dependency:** BIO-ARC-006 (DONE)
**Conflict group:** `ui-design-system`
**Commit:** `68cd42c0a34a15ea16b2bb7967e8ce539b04f67b`

## AC to evidence map

See `docs/features/bio-ux-001/README.md` for the full AC table with implementation file references
and design-decision rationale. Summary:

| AC | Evidence |
|---|---|
| AC01 (visual reference provenance) | `web/design-system/README.md` — doc-only AC, no test |
| AC02 (colour/typography/spacing/radii/shadow tokens) | `web/design-system/tokens.ts`; `bio-ux-001-tokens.test.ts` (9 tests), `bio-ux-001-css-sync.test.ts` (5 tests) |
| AC03 (light/dark share one semantic system) | `bio-ux-001-tokens.test.ts` "semantic tokens use one shared system" describe block |
| AC04 (breakpoints/density/layout) | `web/design-system/breakpoints.ts`; `bio-ux-001-breakpoints.test.ts` (4 tests) |
| AC05 (nine component states) | `web/design-system/component-states.ts`; `bio-ux-001-component-states.test.ts` (5 tests) |
| AC06 (Damilola ownership) | `web/design-system/README.md` "Ownership" — doc-only AC, no test |

## Developer tests (23 new, 156 total passing)

- `tests/frontend/unit/bio-ux-001-tokens.test.ts` (9), `bio-ux-001-breakpoints.test.ts` (4),
  `bio-ux-001-component-states.test.ts` (5), `bio-ux-001-css-sync.test.ts` (5) — 23 new tests, all
  pure-function/file-content assertions (`environment: node`, no browser needed), matching the
  established `bio-fnd-007-tokens.test.ts` pattern.
- Full `tests/frontend` unit suite: **20 files / 156 tests passing** (up from 19/151 pre-change) —
  no existing test's assertions changed or needed adjustment. See `frontend-unit-test-output.txt`.
- `web/`: `tsc --noEmit` clean and `next build` succeeds, still producing exactly the same three
  routes (`/`, `/_not-found`, `/card-designer`) — no route added or removed by this story. See
  `web-typecheck-output.txt`.

All commands were actually executed against real installed dependencies in this session (`corepack
pnpm install` in both `web/` and `tests/frontend/`), not just written and left unverified.

## Decisions and assumptions

- **`web/lib/tokens.ts` was not moved into `web/design-system/`.** BIO-FND-007 already pinned it
  there with an existing test and three live consumers (`theme-toggle.tsx` and three Card Designer
  feature modules). `web/design-system/tokens.ts` re-exports it unchanged instead of relocating it,
  per my own W01.5 plan's finding that this story should "extend, not rebuild." See
  `web/design-system/README.md` "Why `web/lib/tokens.ts` was not moved or edited."
- **`body`'s CSS was deliberately left visually unchanged.** `globals.css`'s `body` rule keeps its
  original literal `font-family` value rather than switching to the new `--biolog-font-family` var,
  and no `font-size`/`line-height` was added to it. Doing either would be a rendered-output change
  with no visual-regression baseline yet in place to catch an unintended drift (that's BIO-UX-014).
  This story tokenises typography; it does not yet apply the scale to shell markup.
- **Icon library still not chosen (metrics are, as of round 2).** AC02's iconography *metrics*
  (size/stroke/alignment/usage) are now frozen in `iconography.ts`. No icon *set* exists anywhere
  in the repository yet and no prior story specified one; which library to use remains an open
  decision for BIO-UX-002/003, but — corrected in round 2 — that deferral does not itself satisfy
  AC02's tokenisation requirement. See `docs/features/bio-ux-001/README.md`'s "Iconography and
  borders" note.
- **AC01 and AC06 have no automated test.** Both are documentation/process requirements (a
  provenance statement, an ownership rule) with nothing executable to assert — satisfied by
  `web/design-system/README.md`'s corresponding sections, reviewed manually.
- **`governance/feature-path-ownership.json` was not touched.** That file's `expected_story_keys`
  list has no `BIO-UX-*` entries at all (it's the v2.1-vintage backend-story ownership schema,
  confirmed by grep before starting this story) — extending it is a shared-contract change outside
  this story's scope and Kemi's call, not mine, if it's ever needed for BIO-UX stories generally.

## Known gaps / local-session notes (not blocking)

- **Full `scripts/quality.sh` run: overall `QUALITY: FAIL`, but confirmed pre-existing and
  unrelated to this story.** It failed on `BIO-ARC-006 contract freeze harness` and
  `BIO-FND-008 api-shell harness` (`quality-sh-tail-output.txt`). Cross-checked by running both
  harnesses (`tests/foundation/BIO-ARC-006.test.sh`, `tests/foundation/BIO-FND-008.test.sh`)
  directly against an untouched `biolog-source` checkout on `develop` with **no BIO-UX-001 changes
  present at all** — same result: `BIO-ARC-006` 26/27, `BIO-FND-008` 36 passed/3 failed. This
  story's change set touches only `web/` and `tests/frontend/`, nothing under `api/`, `contracts/`,
  or `governance/`, so it cannot be the cause. This is the same class of pre-existing gap
  `BIO-FND-007`'s own evidence already flagged for `BIO-ARC-006`; `BIO-FND-008`'s own failures are
  new since then and belong to whoever owns that story's harness, not this one. The direct,
  AC-relevant commands for this story (`web/` typecheck+build, `tests/frontend` full unit suite,
  `bio-ux-001` foundation-equivalent checks) all pass — see the other evidence files.
