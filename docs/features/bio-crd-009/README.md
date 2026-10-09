# BIO-CRD-009 — Add photo element to the card designer canvas

**Story:** Add photo element to the card designer canvas
**Wave:** W11 (build batch W11.3, manually added mid-wave — see `governance/runtime-state.json`'s
`BIO-CRD-009` history for the dispatch-flow gap this exposed and how it was fixed).

## User story

As a card designer, I need to place, move, resize, rotate and layer the cardholder's photo on the
card canvas, the same way I already can with logos and watermarks, so a designed template is
actually usable to issue real cards.

## Acceptance criteria

- **AC01** — A new `PhotoElement` (`elementKind: 'photo'`) extends the shared `BaseElement`
  (`bio-crd-003/canvasElementOps.ts`) exactly like `LogoElement`/`WatermarkElement` do - same
  move/resize/rotate/layer/lock operations, no new canvas mechanics invented.
- **AC02** — `PhotoElement.content` is an opaque reference string only - this story does not
  resolve, upload, or validate real photo bytes; BIO-PHO-001 owns that.
- **AC03** — The photo element is addable/removable on both front and back independently
  (BIO-CRD-002-AC05's front/back independence still holds).
- **AC04** — BIO-CRD-008's `CardTemplateSnapshot`/`CardTemplateElement` capture correctly include
  a placed photo element the same way they already capture logos/watermarks/barcodes/signatures.
- **AC05** — The approved web surface (`/card-designer`) shows a real, usable "add photo" control
  (not a dead button) and the mapped end-to-end human workflow (place, move, resize, remove a
  photo element) passes without an unapproved UI redesign.

**Dependencies.** BIO-CRD-002, BIO-CRD-003, BIO-CRD-008, BIO-PHO-001 - all four already accepted.
Every one is also Ngozi's own prior story, so this build needed zero cross-owner coordination.

## Owned write paths

`web/features/bio-crd-009/**` (new), `tests/frontend/playwright/bio-crd-009-*.spec.ts`,
`tests/frontend/unit/bio-crd-009-*.test.ts`, `docs/features/bio-crd-009/**`,
`evidence/BIO-CRD-009/**`.

**Two additive changes to BIO-CRD-008 (Ngozi's own prior story) - see "Capturing a placed photo"
below:** `api/features/bio-crd-008/Domain/CardTemplateElement.cs` gained one new enum member
(`CardTemplateElementKind.Photo`); `web/features/bio-crd-008/ports.ts`/`TemplateLifecycleBar.tsx`
gained one new literal in the `kind` union plus one new, optional, defaulted `photoElements`
parameter/prop. This mirrors BIO-CRD-008's own precedent for touching BIO-CRD-007's
`ProductionJob` (`TemplateId`/`TemplateVersion`): a plain, additive pass-through into an
already-accepted, self-owned story, not a new declared story dependency.

**Shared, unlisted entry points touched** (the same role `Program.cs`/`module-registry.ts` play
for every story in this epic): `web/app/card-designer/page.tsx` (mounts `CardPhotoApp` alongside -
not replacing - the five existing sections).

**No backend feature project.** Unlike most stories in this epic, BIO-CRD-009 needed no new
`api/features/bio-crd-009/**` project and no new `ModulePort`/`live.ts`/`mock.ts` for itself -
placing a photo element is pure client-side canvas state (per Kemi's "zero I/O" ruling, the same
boundary `CardTypographyApp`/`CardObjectsApp` already establish for their own element kinds). The
only I/O this story's data ever reaches is BIO-CRD-008's own already-existing
`cardTemplateCreateDraftPort`, via the additive pass-through above.

## Design decisions

### `PhotoElement` extends the SHARED `bio-crd-003/canvasElementOps.ts`, not a fresh copy

BIO-CRD-004's own `canvasElementOps.ts` is a deliberate, independent fork of BIO-CRD-003's (see
that file's own header: "BIO-CRD-004 does not modify either sibling story's own files"). AC01's
approved text instead names `bio-crd-003/canvasElementOps.ts` directly as the base to extend -
read literally, not as a third fork. `web/features/bio-crd-009/photoElement.ts` therefore imports
`BaseElement`/`normaliseDegrees` straight from `bio-crd-003/canvasElementOps.ts` (a read-only
import of an already-accepted sibling's already-public API, the same kind of cross-feature read
every backend feature in this codebase already does through a narrow interface - here the
"interface" is simply a stable, exported TypeScript type). `bio-crd-003`'s own files are
untouched.

### Front/back independence: a genuine AC requirement its siblings don't have

Neither `CardTypographyApp` (BIO-CRD-003) nor `CardObjectsApp` (BIO-CRD-004) split their own
elements by card side at all - both render one flat, single-sided tool. AC03 explicitly asks for
front/back independence here, so `web/features/bio-crd-009/photoDesignState.ts` mirrors
`bio-crd-002/cardDesignState.ts`'s own front/back split pattern instead (`sideOf`/`withSide`
helpers, every operation parameterised by side, only ever constructing a new object for THAT
side) - proven with the same reference-equality (`toBe`) assertions BIO-CRD-002-AC05's own tests
established, applied here to `PhotoElement` arrays instead of `CanvasElement` arrays.

### `content` is never validated at the element level - matching Logo/Watermark exactly

Neither `createLogoElement` nor `createWatermarkElement` validates its `content` string at
creation (`WatermarkElement`'s own `validateOpacity` guards opacity, never content). AC02 explicitly
places content resolution/validation outside this story's scope (BIO-PHO-001 owns it), so
`createPhotoElement` follows the identical boundary. The one guard that does exist is at the UI
layer, matching every sibling `App` component's own convention: the "+ Photo" button is `disabled`
for an empty/whitespace-only reference, so an invalid reference simply cannot produce an element in
the first place, rather than silently creating one with empty content.

### Capturing a placed photo (AC04) - a genuine inclusion, not a HYBRID boundary

Unlike `CardTypographyApp`/`CardObjectsApp` (which BIO-CRD-008's own `buildSnapshot` places
entirely on the front side as a disclosed simplification - see
`docs/features/bio-crd-008/README.md` "Building one snapshot from three canvases" - because
neither of those tools splits by side itself), `CardPhotoApp` already produces a real
`{front, back}` split (`PhotoDesignState`). That made genuine, correctly-sided inclusion directly
reachable, so this story took it rather than declaring a HYBRID boundary:

- `CardTemplateElementKind` (backend) and `CardTemplateElement['kind']` (frontend) both gained one
  new, purely additive literal: `Photo`/`'photo'`. Neither is matched by any exhaustive switch
  anywhere in the codebase (confirmed by search before adding it) - the backend's own
  `Enum.TryParse<CardTemplateElementKind>(dto.Kind, ignoreCase: true, ...)` and
  `element.Kind.ToString().ToLowerInvariant()` are both already generic over every member, so no
  other backend file needed a change.
- `TemplateLifecycleBar.tsx`'s `buildSnapshot` gained one new, optional, defaulted
  (`= { front: [], back: [] }`) `photoElements` parameter, mapped into `frontElements`/
  `backElements` with `kind: 'photo'` exactly the way a `LogoElement` already maps to
  `kind: 'image'`. Every pre-existing call site and every pre-existing test that doesn't pass it is
  completely unaffected - the same additive-optional shape every sibling canvas prop on this page
  already uses.
- The pre-existing `photo: { required, aspectRatio, minWidthPx, minHeightPx }` top-level snapshot
  field (BIO-CRD-008's own "requires a photo" spec checkbox on `TemplateLifecycleBar`) is left
  completely untouched - it answers a different question (does this template's process REQUIRE a
  photo) from `photoElements` (has a photo actually been PLACED on the canvas). Verified by a
  dedicated test (`bio-crd-009-template-photo-capture.test.ts`) that the two never interfere.
- `buildSnapshot` itself is now exported from `TemplateLifecycleBar.tsx` (it was previously
  module-private) purely so this capture behaviour is directly, precisely unit-testable rather
  than only provable indirectly through a full end-to-end network capture - a test seam, not a
  behavioural change.

## Deliberately out of scope

- **Real photo capture, upload or bytes** - `content` stays an opaque reference string; no `<img>`
  is ever rendered (verified by test). BIO-PHO-001 owns resolving a reference into real image data.
- **Print rendering** - this story never touches BIO-CRD-007's production/print pipeline. A placed
  `PhotoElement` is captured into the template snapshot (AC04); rendering that snapshot onto a
  physical card is out of scope here, the same boundary every other canvas element in this epic
  already has.
- **Merging the three (now four) independent canvases into one true front/back surface** - a
  pre-existing, disclosed gap from BIO-CRD-008's own README, unchanged by this story.
  `CardPhotoApp` is a fourth independent tool on the same page, not a merge.
- **A maximum-one-photo-per-side restriction** - AC01 asks for the same operations Logo/Watermark
  already have, neither of which caps element count; this story does not invent a new constraint
  its siblings don't have. Multiple photos per side are technically placeable (e.g. a front and a
  secondary badge photo); the UI does not stop this, matching Logo/Watermark's own precedent.

## Test evidence

- **Frontend unit tests** (vitest): 21 new tests across three files -
  `bio-crd-009-photo-element.test.ts` (AC01/AC02, the shared-ops inheritance, opaque content),
  `bio-crd-009-photo-design-state.test.ts` (AC03, front/back independence with `toBe` reference
  equality), `bio-crd-009-template-photo-capture.test.ts` (AC04, `buildSnapshot` capture
  correctness including the "omitted `photoElements`" backward-compatible default). Full suite:
  447/447 passing, no regression.
- **Frontend e2e tests** (Playwright + axe-core), SIMULATION mode: 15 new tests in
  `bio-crd-009-card-photo.spec.ts` covering AC01 (move/resize/rotate/layer via the real UI), AC02
  (opaque content, no `<img>` rendered, empty-reference guard), AC03 (front/back independence
  through the real Front/Back tabs), AC05 (mounted alongside the existing five sections, a real
  "+ Photo" control), a regression check that BIO-CRD-002/003/004's own canvases still work
  alongside the new tool, and a zero-violations axe check. Reran the sibling
  `bio-crd-003-card-typography.spec.ts`/`bio-crd-004-card-objects.spec.ts`/
  `bio-crd-008-template-lifecycle.spec.ts` suites afterwards: 29/29 passing (3 pre-existing
  LIVE-only skips), no regression from the additive `TemplateLifecycleBar` prop/`buildSnapshot`
  signature change.
- **Backend build**: `dotnet build api/Biolog.Api/Biolog.Api.csproj` - clean, confirms the
  additive `CardTemplateElementKind.Photo` enum member compiles and the module host still starts.
  No backend feature project or test project was added or needed for this story (see "No backend
  feature project" above).

## Issues found and fixed during development

1. **A Playwright `getByLabel` collision, self-caught.** The toolbar's "add a photo" field and the
   properties panel's "edit the selected photo" field both initially used the label text "Photo
   reference," the same label text both places - identical to a latent (untested) ambiguity in
   BIO-CRD-004's own `LogoProperties`/toolbar ("Asset reference" in both places). Fixed by
   following BIO-CRD-004's OWN signature-field precedent instead (`newSignatureLineageId` labelled
   "New signature lineage id" vs. the properties panel's plain "Lineage id"): renamed the toolbar
   field to "New photo reference," leaving the properties panel as "Photo reference."
2. **A test-authoring bug in the arrow-key nudge test, self-caught.** Clicking the hint paragraph
   text ("Use the arrow keys...") does not focus anything inside the properties `<div>` that owns
   the `onKeyDown` handler (a `<p>` is not focusable), so the keypress never bubbled through it.
   Fixed by clicking the adjacent "Width (px)" input instead (a real focusable descendant of the
   same `<div>`), which keydown correctly bubbles through.
