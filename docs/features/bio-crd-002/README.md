# BIO-CRD-002 — WYSIWYG card canvas and physical card formats

As a card designer, true front/back WYSIWYG editing with CR80, CR79, CR100, custom sizes and
one-click landscape/portrait switching.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Standard sizes use correct physical dimensions | `CardFormatCatalog.cs` (backend) and `cardFormats.ts` (frontend) both pin CR80/CR79/CR100 to their real, published industry-standard dimensions (ISO/IEC 7810 ID-1 for CR80) | `CardFormatCatalogTests.cs`, `bio-crd-002-card-formats.test.ts`, `bio-crd-002-card-designer.spec.ts` (AC01) |
| **AC02** | Landscape/portrait switch preserves geometry without cumulative drift | `orientation.ts`'s `switchOrientation` - a real 90° rotation using only pixel-value addition/subtraction, never trigonometry, so it is exactly (not approximately) self-inverse | `bio-crd-002-orientation.test.ts` (exact-equality round-trip, 50-switch drift test), `bio-crd-002-card-designer.spec.ts` (AC02) |
| **AC03** | Custom width/height validation exists | `CustomCardSizeValidator.cs` (backend, served via `POST /api/v1/card-designer/custom-size/validate`) and `validateCustomSize` (frontend, mirrored bounds, instant feedback) | `CustomCardSizeValidatorTests.cs`, `CardFormatEndpointsIntegrationTests.cs`, `bio-crd-002-card-formats.test.ts`, `bio-crd-002-card-designer.spec.ts` (AC03) |
| **AC04** | Elements support move/resize/rotate/layer/lock | `elements.ts` - pure functions for each operation; a locked element refuses move/resize/rotate but lock/unlock itself is never refused | `bio-crd-002-elements.test.ts`, `bio-crd-002-card-designer.spec.ts` (AC04) |
| **AC05** | Front and back are independent | `cardDesignState.ts` - every mutating operation is explicitly parameterised by `side` and only ever constructs a new object for that side | `bio-crd-002-card-design-state.test.ts` (asserts the untouched side is the same object reference, not merely equal), `bio-crd-002-card-designer.spec.ts` (AC05) |

## Design decisions and scope boundaries

### The first real feature page in the Next.js shell

`web/features/**` was empty before this story - BIO-FND-007 built only the app shell, with
`web/lib/nav-items.ts` already carrying a `card-designer` nav entry pointing at `/card-designer`
and `docs/MANUAL.md` stating explicitly: "None of these routes resolve to a real page yet ...
until the story that owns that area builds it." `web/app/card-designer/page.tsx` is that page.

### No "Card Designer v0.3" specification exists in this repository

Referenced by name in `docs/BRAND-ASSETS.md` (BIO-CRD-001), the file itself is not present
anywhere in this repository at the time of writing - the same gap BIO-CRD-001 and BIO-PHO-001
each found and disclosed for their own citations of it. CR80/CR79/CR100's physical dimensions
are the real, widely-published industry-standard sizes these names refer to across the
card-printer industry (Fargo/HID, Zebra, Evolis and others all publish the identical figures),
not invented values:

- **CR80** (ISO/IEC 7810 ID-1, the standard credit-card/ID-card size): 85.60mm × 53.98mm.
- **CR79**: 83.90mm × 51.90mm.
- **CR100**: 98.50mm × 67.00mm.

### AC02's real mechanism: no trigonometry, ever

A rotation matrix's `sin`/`cos` of 90° are exactly 0/±1 in theory but not in IEEE-754 floating
point in practice; repeated multiplication by that near-zero/near-one noise is exactly the
"cumulative drift" this AC forbids. `switchOrientation` instead uses two DIFFERENT formulas -
landscape→portrait and portrait→landscape - each the other's exact algebraic inverse (derived
and verified directly; see `orientation.ts`'s own header for the worked reasoning), built only
from addition/subtraction against the canvas's own current pixel dimensions. The result is
provably, not approximately, reversible: `bio-crd-002-orientation.test.ts` asserts exact
(`toEqual`) equality after a round trip and after 50 repeated switches, and a real-browser test
confirms the same property in the rendered canvas.

A single shared formula applied both directions would produce a 180° rotation on the second
switch, not a return to the original layout - which is what a designer actually expects from a
one-click "switch back."

### Accessibility-first controls, not raw mouse-drag

This repository holds a zero-tolerance axe-accessibility bar (BIO-FND-007-AC05: every reported
violation fails the scan, at any impact level). Raw pointer-drag interactions are notoriously
hard to make keyboard-accessible and axe-clean without substantial extra engineering, and are
far harder to test reliably than discrete operations. Move/resize/rotate/layer/lock are
implemented as accessible, keyboard-and-button-driven controls (arrow-key nudge, number inputs,
rotate-by-15° buttons, layer-order buttons, a lock checkbox) rather than freeform mouse-drag -
"supports move/resize/rotate/layer/lock" does not mandate a specific input modality, and the
canvas itself still renders every element at its true, exact position/size/rotation (true
WYSIWYG), just driven by accessible controls. `bio-crd-002-card-designer.spec.ts`'s
accessibility-baseline tests (including with the properties panel open) pass with zero
violations.

### Backend scope: a stateless reference catalogue, not template persistence

No AC asks for saving/loading a card design server-side, and no dependency names a template-storage
story, so `api/features/bio-crd-002/` is deliberately lean: a read-only, non-tenant-scoped
format catalogue and a custom-size validator - the same "fixed baseline catalogue" shape
BIO-IDN-006's `RoleType` and BIO-PHO-002's `CaptureSpecification` use, and the same "open,
non-tenant-scoped reference data" shape RBAC's own `GET /api/v1/roles` catalogue uses. No tenant
context, actor resolution, repository, lock registry or audit sink exists anywhere in this
feature - none is needed. The canvas's element/design state lives entirely client-side for this
story; a later story can add real persistence behind this same catalogue without changing it.

### "Dependencies: BIO-CRD-001, BIO-PHO-001" is sequencing, not coupling

Neither brand-asset upload nor photo-master promotion is called anywhere in this feature - the
same "Dependencies in story metadata is build-wave sequencing, not a runtime service
dependency" precedent every sibling story has documented for its own dependency line when
nothing in its own ACs names a genuine runtime call.

## Deliberately out of scope for this story

- Real freeform mouse-drag move/resize/rotate (see "Accessibility-first controls" above).
- Server-side persistence of a card design (elements, format, orientation) - the backend owns
  only the format catalogue and validator; a later story can add a save/load API behind
  `IPhotoMasterPromotionGateway`-style seams without changing this story's shape.
- Placing an actual photo (BIO-PHO-001) or brand asset (BIO-CRD-001) onto the canvas - no AC
  asks for it, and the element model (`elements.ts`) is deliberately generic (a labelled box),
  not typed to a specific content source.
- Print/production rendering (rasterising the design to a print-ready file) - a later card
  production story's concern.
- Tenant-configurable card formats - `CardFormatCatalog`/`STANDARD_FORMATS` are a fixed
  baseline; no AC asks for per-tenant customisation.
