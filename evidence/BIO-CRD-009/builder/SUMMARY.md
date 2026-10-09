# BIO-CRD-009 — Builder evidence

**Story:** Add photo element to the card designer canvas
**Result:** PASS

## Restated scope (per the story-stage prompt)

**User story.** As a card designer, I need to place, move, resize, rotate and layer the
cardholder's photo on the card canvas, the same way I already can with logos and watermarks, so a
designed template is actually usable to issue real cards.

**Acceptance criteria:** AC01 (`PhotoElement` extends the shared `bio-crd-003/canvasElementOps.ts`
`BaseElement`, same move/resize/rotate/layer/lock as Logo/Watermark), AC02 (`content` is an opaque
reference string only - BIO-PHO-001 owns real photo bytes), AC03 (addable/removable on front and
back independently), AC04 (BIO-CRD-008's `CardTemplateSnapshot`/`CardTemplateElement` capture
correctly includes a placed photo, or an explicitly documented HYBRID boundary if genuinely out of
reach), AC05 (a real, usable "add photo" control on `/card-designer`, connected end-to-end, no
unapproved redesign).

**Dependencies (build-wave gating).** BIO-CRD-002, BIO-CRD-003, BIO-CRD-008, BIO-PHO-001 - all four
already accepted, and all four are Ngozi's own prior stories.

**Conflict group.** `card-designer`.

**Owned write paths.** `web/features/bio-crd-009/**` (new), `tests/frontend/playwright/
bio-crd-009-*.spec.ts`, `tests/frontend/unit/bio-crd-009-*.test.ts`, `docs/features/bio-crd-009/**`,
`evidence/BIO-CRD-009/**`.

**Two additive changes to BIO-CRD-008 (Ngozi's own prior story), mirroring BIO-CRD-008's own
precedent for touching BIO-CRD-007's `ProductionJob`:**
`api/features/bio-crd-008/Domain/CardTemplateElement.cs` gained one new, purely additive enum
member (`CardTemplateElementKind.Photo`) - not matched by any exhaustive switch anywhere in the
codebase (verified by search before adding it). `web/features/bio-crd-008/ports.ts` gained one new
literal (`'photo'`) in the `CardTemplateElement['kind']` union; `TemplateLifecycleBar.tsx` gained
one new, optional, defaulted `photoElements` prop/parameter, threaded into `buildSnapshot` (now
exported as a test seam) alongside the pre-existing arguments. Every pre-existing call site is
unaffected by the default.

**Shared, unlisted entry points touched:** `web/app/card-designer/page.tsx` (mounts `CardPhotoApp`
as a sixth, additive section, alongside - not replacing - the five existing ones).

**No backend feature project needed.** Unlike every other story in this epic, BIO-CRD-009 required
no `api/features/bio-crd-009/**` project, no `Biolog.Api.csproj`/`Program.cs` module registration,
and no `ModulePort`/`live.ts`/`mock.ts` of its own - placing a photo element is pure client-side
canvas state (Kemi's "zero I/O" ruling, the same boundary `CardTypographyApp`/`CardObjectsApp`
already establish). The only I/O this story's data ever reaches is BIO-CRD-008's own
already-existing `cardTemplateCreateDraftPort`, via the additive pass-through above.

**Security/privacy risks identified and addressed.**
- `content` never renders as an `<img>` and is never resolved - verified by test (AC02).
- No new I/O surface is introduced by this story at all; it adds no attack surface beyond the
  purely-additive, backward-compatible union/prop widening described above.

**Tests planned and written.** See "Test evidence" below.

## Design decisions

See `docs/features/bio-crd-009/README.md` for the full write-up. Summary: `PhotoElement` extends
the SHARED `bio-crd-003/canvasElementOps.ts` (per AC01's literal text), not a fresh per-feature
fork like BIO-CRD-004's own copy. `photoDesignState.ts` mirrors BIO-CRD-002's own front/back split
pattern (`sideOf`/`withSide`, reference-equality-tested) because AC03 explicitly needs it, unlike
BIO-CRD-003/004's own single-sided tools. AC04 is a genuine, real inclusion (not a HYBRID
boundary): `CardPhotoApp` already produces a real `{front, back}` split, so it maps straight onto
`frontElements`/`backElements` with `kind: 'photo'`, exactly the way `LogoElement` already maps to
`kind: 'image'` - reachable with a purely additive enum/union/prop change, verified by both a
dedicated `buildSnapshot` unit test and the backend build.

## Issues found and fixed during development (not assumed correct, verified)

1. **A Playwright `getByLabel` collision, self-caught.** The toolbar's add-photo field and the
   properties panel's edit-photo field both initially used the identical label text ("Photo
   reference") - the same latent, untested ambiguity BIO-CRD-004's own `LogoProperties`/toolbar
   has (both say "Asset reference"). Fixed by following BIO-CRD-004's OWN signature-field
   precedent instead (distinct toolbar vs. properties labels): renamed the toolbar field to "New
   photo reference."
2. **A test-authoring bug in the arrow-key nudge test, self-caught.** Clicking non-focusable hint
   text never focuses the properties `<div>` that owns the `onKeyDown` handler, so the keypress
   never bubbled through it. Fixed by clicking a real focusable descendant (the "Width (px)"
   input) instead.

Neither was a review-round finding - both were caught and fixed during my own implementation pass,
before any independent review.

## Test evidence

- **Frontend unit tests** (vitest): 447/447 passing across 59 files, including 21 new BIO-CRD-009
  tests across three files (`bio-crd-009-photo-element.test.ts`,
  `bio-crd-009-photo-design-state.test.ts`, `bio-crd-009-template-photo-capture.test.ts`) - see
  `frontend-unit-test-output.txt`. Covers AC01 (shared-ops inheritance: move/resize/rotate/layer/
  lock), AC02 (opaque content, no validation at element level), AC03 (front/back independence with
  `toBe` reference-equality assertions), AC04 (`buildSnapshot` capture correctness, including the
  omitted-`photoElements`-argument backward-compatible default and non-interference with the
  pre-existing top-level `photo` spec field).
- **`tsc --noEmit` + `next build`**: PASS, zero errors, `/card-designer` still statically generated
  - see `web-typecheck-output.txt`, `web-build-output.txt`.
- **Frontend e2e tests** (Playwright + axe-core), SIMULATION mode, single-worker: 44/44 passing (15
  new `bio-crd-009-card-photo.spec.ts` tests covering AC01/AC02/AC03/AC05 through the real UI, plus
  a full rerun of the sibling `bio-crd-003-card-typography.spec.ts`/
  `bio-crd-004-card-objects.spec.ts`/`bio-crd-008-template-lifecycle.spec.ts` suites - 29/29
  passing, 3 pre-existing LIVE-only skips, confirming zero regression from the additive
  `TemplateLifecycleBar` prop/`buildSnapshot` signature change) - see
  `frontend-e2e-test-output.txt`.
- **API host build**: clean, 0 errors/warnings, confirms the additive
  `CardTemplateElementKind.Photo` enum member compiles and the module host still starts (no new
  module was registered by this story) - see `api-host-build-output.txt`.
- **Backend regression** (`dotnet test`, xunit): BIO-CRD-008's own full suite, 29/29 passing, zero
  regression from the additive enum member - see `backend-regression-test-output.txt`.
- **Module project reference direction**: PASS (`check-module-project-references.sh`) - no new
  module, so nothing new to check, confirmed unaffected.
- **Traceability inventory**: PASS, 33 implemented stories (up from 32), confirming
  `docs/features/bio-crd-009/README.md` is correctly picked up.
- **Secret scan**: PASS (`secret-scan: clean`) - see `secret-scan-output.txt`.

## Proving AC05

1. `cd web && node node_modules/next/dist/bin/next build` - confirms `/card-designer` still
   compiles and statically generates with the new `CardPhotoApp` section mounted.
2. `cd tests/frontend && node node_modules/@playwright/test/cli.js test bio-crd-009-card-photo.spec.ts --workers=1`
   - a real browser loads `/card-designer`, clicks the real "+ Photo" button, and asserts a real,
     visible, selected photo element appears (not a dead button) - see
     `frontend-e2e-test-output.txt`, test `BIO-CRD-009-AC05 ... the "add photo" control is real`.
3. The same spec's AC03 tests drive the real Front/Back tabs (the exact `role="tablist"
   aria-label="Front or back"` pattern BIO-CRD-002's own `CardDesignerApp` already established)
   and assert a photo placed on one side never appears on the other, through the real rendered DOM.
