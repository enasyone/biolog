# BIO-CRD-003 — Card typography, name fields and watermarks

As a card designer, independently-bound name fields (Title/First/Middle/Last/Full/Display) with
full per-field typography, plus text/image watermarks with opacity, rotation, layer control and
STAFF/STUDENT/VISITOR starter presets.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Each name field binds independently | `nameFields.ts` - `NameFieldElement.binding` is a plain field on each element; six bindings (`title`/`firstName`/`middleName`/`lastName`/`fullName`/`displayName`) are each freely selectable per field, independent of every other field on the canvas | `bio-crd-003-name-fields.test.ts`, `bio-crd-003-card-typography.spec.ts` (AC01) |
| **AC02** | Font family/size/colour/bold/italic/underline/case/alignment/spacing/opacity editable per field | `typography.ts`'s `TypographyStyle` names all ten properties; `updateTypography` is the single validated entry point for changing any of them | `bio-crd-003-typography.test.ts`, `bio-crd-003-card-typography.spec.ts` (AC02) |
| **AC03** | Copy style does not copy data binding | `nameFields.ts`'s `copyStyleTo(source, target)` - its own signature only ever reads `source.typography` and writes `target.typography`; it never reads or writes `.binding` on either argument | `bio-crd-003-name-fields.test.ts` (including an A→B→C chain-of-copies test), `bio-crd-003-card-typography.spec.ts` (AC03) |
| **AC04** | Text/image watermarks support 0-100% opacity, rotation and layer control | `watermarks.ts`'s `WatermarkElement` (`watermarkKind: 'text' \| 'image'`, `opacityPercent` validated 0-100); rotation and layer control (`bringToFront`/`sendToBack`/`bringForward`/`sendBackward`) are inherited from the shared `canvasElementOps.ts` base, since a watermark is a `BaseElement` like any other | `bio-crd-003-watermarks.test.ts`, `bio-crd-003-canvas-element-ops.test.ts`, `bio-crd-003-card-typography.spec.ts` (AC04) |
| **AC05** | STAFF/STUDENT/VISITOR starter watermark behaviors available | `WatermarkPresetCatalog.cs` (backend, served via `GET /api/v1/card-designer/watermark-presets`) and `WATERMARK_PRESETS` (frontend, mirrored by hand) both name the three starter presets with their default text/opacity/rotation; `createWatermarkFromPreset` builds a starting watermark from any of them, still freely editable afterwards | `WatermarkPresetCatalogTests.cs`, `bio-crd-003-watermarks.test.ts`, `bio-crd-003-card-typography.spec.ts` (AC05) |

## Design decisions and scope boundaries

### Two coordinated tools, not yet one merged canvas

`web/app/card-designer/page.tsx` is not declared as owned by either BIO-CRD-002 or BIO-CRD-003 in
`governance/feature-path-ownership.json` - a shared, unlisted entry point every
`card-designer`-conflict-group story updates, the same role `Program.cs`/`Biolog.Api.csproj` play
for every backend story. This story's write scope is `web/features/bio-crd-003/**` only; it does
not reopen BIO-CRD-002's own `CardDesignerApp.tsx`, `elements.ts` or `cardDesignState.ts`.

Rather than stuffing typed name-field/watermark data onto BIO-CRD-002's own `CanvasElement`
model (which is intentionally untyped - "a labelled box", per that story's own README - and would
lose subtype information on read), this story renders its own independent canvas
(`CardTypographyApp.tsx`) with its own flat `elements: (NameFieldElement | WatermarkElement)[]`
state, composed onto the same `/card-designer` route as a second section beneath BIO-CRD-002's
canvas. The result is two coordinated tools on one page, not yet one single merged canvas where a
name field or watermark can be positioned directly alongside BIO-CRD-002's own generic elements
inside the same front/back document. Merging them into one true canvas/document model is left to
a later story - no AC here asks for it, and doing so would require either story to reopen the
other's declared write scope.

### An independent, generic copy of BIO-CRD-002's move/resize/rotate/lock/layer logic

TypeScript has no equivalent of a C# project's one-way `ProjectReference` restriction, so nothing
stops `web/features/bio-crd-003/**` from importing BIO-CRD-002's `elements.ts` directly - but
doing so would make this story's own correctness depend on a file outside its write scope, and
any future BIO-CRD-002 change could silently break it. `canvasElementOps.ts` is a fully
independent, generic (`<T extends BaseElement>`) implementation of the same move/resize/rotate/
lock/layer operations, reused once within this story for both `NameFieldElement` and
`WatermarkElement` (so the logic is not duplicated *within* the story), while accepting
duplication *across* the BIO-CRD-002/BIO-CRD-003 boundary as the correct tradeoff for write-scope
isolation. The one legitimate reuse of BIO-CRD-002 is a read-only import of its already-exported
pure functions (`STANDARD_FORMATS`, `mmToPx` from `cardFormats.ts`) to size this story's own
canvas to a real card format - not a shared mutable model.

### `rotationDeg` is normalised at construction, not only on mutation

`BaseElement.rotationDeg` is documented as always normalised to `[0, 360)`. The VISITOR/STAFF/
STUDENT presets each carry a default rotation of `-30`; `createWatermarkElement` calls
`normaliseDegrees` on construction (not only inside `rotateElement`'s own mutation path) so the
invariant holds from the moment an element is created, not only after its first rotation.

### Discriminated union via a sentinel field, not inheritance

BIO-CRD-002's own `CanvasElement` interface carries no discriminant. `NameFieldElement` and
`WatermarkElement` each carry their own `elementKind: 'nameField' | 'watermark'` literal field and
a type-guard (`isNameFieldElement`/`isWatermarkElement`) for safe narrowing wherever the UI or
tests need to branch on which kind of element they're holding.

### No in-repo specification for the watermark preset catalogue's starting values

As with BIO-CRD-002's card-format dimensions, no "Card Designer v0.3"-style specification exists
in this repository naming exact starter values for STAFF/STUDENT/VISITOR watermarks. The values
chosen (STAFF/STUDENT: 20% opacity, -30° rotation; VISITOR: 35% opacity, -30° rotation - a
visitor badge benefits from a more visible category marker) are this story's own deliberately-
named starting point, pinned by `WatermarkPresetCatalogTests.cs` and
`bio-crd-003-watermarks.test.ts` and freely editable afterwards via the same opacity/rotation
controls every watermark has.

### Backend scope: a stateless reference catalogue, matching BIO-CRD-002's own shape

No AC asks for server-side persistence of a name field's typography or a watermark's placement.
`api/features/bio-crd-003/` is a read-only, non-tenant-scoped preset catalogue only - the same
"fixed baseline catalogue, no tenant/audit/lock machinery" shape `api/features/bio-crd-002/`
already established for this route. All typography/name-field/watermark state lives client-side
for this story.

## Deliberately out of scope for this story

- Merging BIO-CRD-002's and BIO-CRD-003's canvases into a single unified element/document model
  (see "Two coordinated tools, not yet one merged canvas" above).
- Real image-file upload/validation for an image watermark - `WatermarkElement.content` for an
  image kind is an opaque reference string only; BIO-CRD-001 owns real asset validation.
- Server-side persistence of typography, name-field bindings or watermark placement.
- Rendering actual bound name data (a real person's name) - no AC asks for binding to live person
  data yet; the canvas shows each field's binding label (e.g. "[First Name]"), not resolved text.
- Print/production rendering - a later card production story's concern.
