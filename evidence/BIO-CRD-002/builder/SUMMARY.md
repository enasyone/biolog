# BIO-CRD-002 — Builder evidence

**Story:** Implement WYSIWYG card canvas and physical card formats
**Owner:** Ngozi — Credential & Card Engineer
**Conflict group:** `card-designer`
**Dependencies:** BIO-CRD-001, BIO-PHO-001 (both sequencing only — neither brand-asset upload nor photo-master promotion is referenced anywhere in this feature)

## Source hierarchy check

No conflict found between the Master Technical Specification, accepted ADRs and the approved
story/AC text. No "Card Designer v0.3" specification file exists anywhere in this repository
(referenced by name in `docs/BRAND-ASSETS.md`, never actually committed) — the same gap
BIO-CRD-001 and BIO-PHO-001 each found and disclosed for their own citations of it. CR80/CR79/
CR100's physical dimensions are documented in `docs/features/bio-crd-002/README.md` as the
real, widely-published industry-standard sizes these names refer to, not invented values, and
not something this story has standing to redefine.

## AC-to-evidence map

- **AC01 (standard sizes use correct physical dimensions):** `api/features/bio-crd-002/Domain/CardFormatCatalog.cs`
  (backend) and `web/features/bio-crd-002/cardFormats.ts` (frontend) — mirrored by hand, same
  as `web/lib/tokens.ts`'s own established pattern. Builds the real `/card-designer` route
  (`web/app/card-designer/page.tsx`) `web/lib/nav-items.ts` has pointed at since BIO-FND-007.
  Tests: `CardFormatCatalogTests.cs`, `bio-crd-002-card-formats.test.ts`,
  `bio-crd-002-card-designer.spec.ts` (AC01 tests).
- **AC02 (landscape/portrait switch preserves geometry without cumulative drift):**
  `web/features/bio-crd-002/orientation.ts` — a real 90° rotation built only from
  addition/subtraction against the canvas's own pixel dimensions, never trigonometry (a
  `sin`/`cos` of 90° is not exactly 0/±1 in IEEE-754 floating point, and repeated multiplication
  by that noise is exactly the drift this AC forbids). Two DIFFERENT formulas for the two
  directions, each the other's exact algebraic inverse (derived and verified directly — see
  that file's own header). Tests: `bio-crd-002-orientation.test.ts` (exact-equality round-trip,
  a 50-switch/25-round-trip drift test, and a regression guard proving no floating-point residue
  even for non-round pixel dimensions), `bio-crd-002-card-designer.spec.ts` (AC02, a real-browser
  round-trip through the rendered canvas).
- **AC03 (custom width/height validation exists):** `Domain/CustomCardSizeValidator.cs`
  (backend, served via `POST /api/v1/card-designer/custom-size/validate`) and
  `validateCustomSize` (frontend, mirrored bounds for instant feedback). An invalid size is
  data (`200 OK` with `is_valid: false`), never an HTTP error, since the request itself was
  well-formed. Tests: `CustomCardSizeValidatorTests.cs`, `CardFormatEndpointsIntegrationTests.cs`,
  `bio-crd-002-card-formats.test.ts`, `bio-crd-002-card-designer.spec.ts` (AC03).
- **AC04 (elements support move/resize/rotate/layer/lock):** `web/features/bio-crd-002/elements.ts`
  — pure functions for each operation; a locked element refuses move/resize/rotate but
  lock/unlock itself is never refused (there must always be a way out of a locked state).
  Implemented as accessible, keyboard-and-button-driven controls (arrow-key nudge, number
  inputs, rotate-by-15° buttons, layer-order buttons, a lock checkbox), not raw mouse-drag — see
  "Decisions" below. Tests: `bio-crd-002-elements.test.ts`, `bio-crd-002-card-designer.spec.ts`
  (AC04).
- **AC05 (front and back are independent):** `web/features/bio-crd-002/cardDesignState.ts` —
  every mutating operation is explicitly parameterised by `side` and only ever constructs a new
  object for that side. Tests: `bio-crd-002-card-design-state.test.ts` (asserts the untouched
  side is the exact same object reference via `toBe`, not merely equal via `toEqual`),
  `bio-crd-002-card-designer.spec.ts` (AC05).

## Decisions and assumptions (read `docs/features/bio-crd-002/README.md` for the full reasoning)

- **The first real feature page in the Next.js shell.** `web/features/**` was empty before this
  story; `web/app/card-designer/page.tsx` replaces the 404 placeholder `docs/MANUAL.md`
  explicitly said "the story that owns that area" would build.
- **Move/resize/rotate/layer/lock are accessible, keyboard-and-button-driven controls, not raw
  mouse-drag** — this repository holds a zero-tolerance axe-accessibility bar
  (BIO-FND-007-AC05: every violation, any impact level, fails the scan). Raw pointer-drag is
  hard to make keyboard-accessible and axe-clean without significant extra engineering, and far
  harder to test reliably than discrete operations. The canvas still renders every element at
  its true, exact geometry — real WYSIWYG, just driven by accessible controls. Verified: the
  Playwright accessibility-baseline tests (including with the properties panel open, an element
  selected) pass with **zero** axe violations.
- **Backend is deliberately lean**: a stateless, non-tenant-scoped format catalogue and
  validator only — no AC asks for server-side design persistence, so no tenant context,
  repository, lock registry or audit sink exists anywhere in this feature, matching RBAC's own
  open role-catalogue endpoint shape.
- **No new ADR-0001 canonical identifier type minted** — this feature has no tenant-scoped
  aggregate at all.
- **"Dependencies: BIO-CRD-001, BIO-PHO-001" is sequencing, not coupling** — confirmed by direct
  inspection: neither is referenced anywhere in this feature's code.

## Test results (commit `00f417dd4f6f1f4a7165e80f756273035f643e15`)

- `bash tests/features/bio-crd-002/BIO-CRD-002.test.sh` — 27/27 PASS
- `dotnet test tests/features/bio-crd-002/Biolog.Features.CardDesigner.Tests.csproj` — 20/20 PASS
- `pnpm --dir tests/frontend test:unit` — 57/57 PASS (45 new BIO-CRD-002 tests + 12 pre-existing, unaffected)
- `pnpm --dir tests/frontend test:e2e` (BIO-CRD-002 spec only, verified directly against a scratch-port dev server since port 3000 was occupied by an unrelated stray process during development) — 14/14 PASS, including zero axe violations; full suite (30/30, BIO-CRD-002 + pre-existing BIO-FND-007 shell spec) also verified clean
- `dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj` — 200/200 PASS
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj` — 104/104
  PASS when run alone (confirmed clean twice, once in isolation and once immediately after a
  failure). Inside `scripts/quality.sh --full`'s combined run, `ObservabilityTests` produced a
  single failure on **three separate runs**, each time a **different** specific test method
  (`AC04_a_plain_string_under_a_generic_name_is_withheld_by_default_through_the_real_wired_console_provider`,
  then `AC04_a_classified_object_under_a_generic_name_is_withheld_by_the_real_wired_console_provider`,
  then `AC01_a_real_request_logs_correlation_and_tenant_scope_to_the_console_view`), always the
  identical `System.ArgumentOutOfRangeException` inside `StringBuilder.ToString()`. A different
  method failing each run with the same exception shape, always within the same test class, is
  the signature of a pre-existing race condition in `ObservabilityTests`' own shared
  console-capture buffer under parallel test execution — not a BIO-CRD-002 regression (this
  story touches no observability code) and not this story's (BIO-FND-009's) file to fix.
- `pnpm typecheck` (`web/`) — clean, 0 errors
- `next build` (`web/`) — succeeds, `/card-designer` generated as a static route
- `bash scripts/check-module-project-references.sh` — PASS
- `python3 scripts/check-contract-freeze.py` — PASS (no shared-contract file touched)
- `python3 scripts/check-traceability.py --all` — PASS (22 implemented stories; this story is
  correctly outside the `governance/stories/*.json` gate, same as every project-per-feature
  sibling)
- `bash scripts/quality.sh` (base tier) — **PASS**, see `evidence/BIO-CRD-002/builder/quality-output.txt`
- `bash scripts/quality.sh --full` (run with `BIOLOG_BASE_URL` pointed at a scratch-port `next dev`
  instance in this worktree, since port 3000 was occupied by an unrelated stray process during
  development) — every check this story owns passes: `.NET card designer unit tests` PASS,
  `BIO-CRD-002 card designer harness` PASS, `frontend unit tests` PASS (57/57), `Playwright
  tests` PASS (all 30, including the pre-existing BIO-FND-007 suite and this story's 14 new
  tests with zero axe violations), `.NET vulnerability audit` PASS, `.NET unit tests` PASS
  (200/200), `Flutter tests` PASS. The only failing line is `.NET integration tests`, entirely
  attributable to the pre-existing `ObservabilityTests` flake described above. See
  `evidence/BIO-CRD-002/builder/quality-full-output.txt`.

Full transcripts captured alongside this file.
