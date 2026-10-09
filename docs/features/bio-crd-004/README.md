# BIO-CRD-004 — Dynamic barcode/QR/signature/card objects

As a designer, dynamic barcode and optional QR/PDF417/Data Matrix/Aztec profiles with quiet-zone/
size/contrast validation, versioned signature references, a logo object, and conditional
visibility by card class or barcode profile for any card object.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Default ID-card barcode profile is alphanumeric | `BarcodeProfileCatalog.cs` (backend) and `DEFAULT_BARCODE_PROFILE` (frontend) both pin `Alphanumeric` as the only default-eligible profile; `createBarcodeElement` starts every new barcode object on it | `BarcodeProfileCatalogTests.cs`, `bio-crd-004-barcode-objects.test.ts`, `bio-crd-004-card-objects.spec.ts` (AC01) |
| **AC02** | QR/PDF417/Data Matrix/Aztec are optional profiles, not default | `BarcodeProfileCatalog.OptionalProfiles`/`OPTIONAL_BARCODE_PROFILES` name exactly the four 2D symbologies; each requires its own explicit toolbar button - none is ever auto-selected | `BarcodeProfileCatalogTests.cs`, `bio-crd-004-barcode-objects.test.ts`, `bio-crd-004-card-objects.spec.ts` (AC02) |
| **AC03** | Barcode objects validate quiet zone/size/contrast | `BarcodeValidation.cs`/`barcodeObjects.ts`'s `validateBarcode` - three independent checks (quiet-zone minimum, width/height bounds, WCAG-formula contrast ratio), aggregated into named violations, never an exception for a well-formed request | `BarcodeValidationTests.cs`, `bio-crd-004-barcode-objects.test.ts`, `bio-crd-004-card-objects.spec.ts` (AC03) |
| **AC04** | Signature assets are versioned | `SignatureAsset.cs`/`ISignatureAssetRepository` - immutable, append-only, tenant-scoped, versioned by `(TenantId, LineageId, Version)` starting at 1; a canvas `SignatureElement` pins a specific version, never "always latest" | `SignatureAssetRepositoryTests.cs`, `SignatureAssetEndpointsIntegrationTests.cs`, `bio-crd-004-signature-objects.test.ts`, `bio-crd-004-card-objects.spec.ts` (AC04) |
| **AC05** | Conditional visibility supports profile/card class rules | `visibility.ts`'s `VisibilityRule` (independent optional card-class and barcode-profile allowlists, combined with AND, `null` meaning unrestricted) - shared by every card object this story defines (barcode, signature, logo) | `bio-crd-004-visibility.test.ts`, `bio-crd-004-card-objects.spec.ts` (AC05) |

## Design decisions and scope boundaries

### A third coordinated tool on the shared `/card-designer` route

`web/app/card-designer/page.tsx` is not declared as owned by BIO-CRD-002, BIO-CRD-003 or this
story in `governance/feature-path-ownership.json` - the same shared, unlisted entry point every
`card-designer`-conflict-group story updates (the role `Program.cs`/`Biolog.Api.csproj` play for
backend stories). This story's write scope is `web/features/bio-crd-004/**` only; it does not
reopen either sibling's own component. `CardObjectsApp.tsx` renders as a third, independent
section beneath BIO-CRD-002's canvas and BIO-CRD-003's typography tool - three coordinated tools,
not one merged canvas, the same disclosed scope boundary BIO-CRD-003's own README already
established for the BIO-CRD-002/003 pair.

### `canvasElementOps.ts` is a fresh, independent copy, not an import

Following BIO-CRD-003's own precedent exactly: TypeScript has no C#-style one-way
`ProjectReference` enforcement, so nothing stops this story's own `web/features/bio-crd-004/**`
from importing a sibling story's move/resize/rotate/lock/layer logic directly - but doing so
would make this story's correctness depend on a file outside its own write scope. This story's
own `canvasElementOps.ts` is a third independent copy of the same generic (`<T extends
BaseElement>`) logic, reused once within this story for all three of its own element kinds
(barcode, signature, logo). The one legitimate reuse of a sibling story is a read-only import of
BIO-CRD-002's already-exported pure `cardFormats.ts` helpers (`STANDARD_FORMATS`, `mmToPx`) to
size this story's own canvas to a real card format.

### Signature-asset versioning follows BIO-CRD-001's own `BrandAsset` precedent, not `PhotoMaster`'s

BIO-PHO-001's own `PhotoMaster` is immutable/versioned too, but wraps that in a full
audit-atomicity/lock-registry apparatus (an audit sink, a per-person in-process lock, an
ambiguous-write-verification retry) because its own ACs and cross-story dependencies (a real
`Person` existence gateway, an audit trail requirement) call for it. Nothing in BIO-CRD-004's own
AC04 text ("Signature assets are versioned") asks for an audit trail, and no cross-story identity
existence check is declared as a dependency for this story. `InMemorySignatureAssetRepository`
instead follows BIO-CRD-001's own `InMemoryBrandAssetRepository` precedent within this same epic:
tenant-scoped, in-memory, `ConcurrentDictionary`-backed, optimistic-retry version assignment (no
lock class needed - a losing `TryAdd` on the next-version key simply retries against a fresh
read), no audit sink. `SignatureAssetRepositoryTests.cs`'s
`Concurrent_appends_to_the_same_lineage_never_collide_or_skip_a_version` test proves the
optimistic-retry scheme is race-safe under 50 concurrent appends to the same lineage.

### No canonical identifier type minted for a signature asset

ADR-0001 registers exactly six identifier types (`ten`/`per`/`crd`/`dev`/`dcs`/`evt`) and states
explicitly that "a feature story may not invent a prefix locally." A signature asset, like
`PhotoMaster` and `BrandAsset` before it, has no dedicated identifier of its own kind - it is
keyed by the natural composite `(TenantId, LineageId, Version)`, where `LineageId` is an opaque,
caller-supplied business key this story has no standing to define the shape of (it is never
validated against a real `Person` or other registry, since no such existence-gateway dependency
is declared for this story).

### `BarcodeValidation`'s thresholds are this story's own disclosed starting point

As with every prior card-designer sibling's own catalogue, no "Card Designer v0.3"-style
specification naming exact barcode thresholds exists anywhere in this repository. `MinQuietZoneMm`
(2.0mm), `MinDimensionMm`/`MaxDimensionMm` (5mm-100mm) and `MinContrastRatio` (4.5:1, borrowed
from the WCAG 2.x AA "normal text" threshold as a disclosed, testable proxy for scannability - see
`BarcodeValidation.cs`'s own header for the full reasoning) are this story's own deliberately-
named values, pinned by `BarcodeValidationTests.cs` and `bio-crd-004-barcode-objects.test.ts`.

### `VisibilityRule` is shared, generic infrastructure, not barcode-specific

AC05's "profile/card class rules" applies uniformly to every card object this story defines, not
only to barcode objects - a logo or a signature can equally be restricted to show only for a
given card class or only when the card's own barcode is set to a given profile. `null` on either
list means "no restriction on that dimension" (matching how every element behaves today, with no
visibility rule applied at all), never "match nothing"; the two dimensions combine with AND, so
an object can be restricted by class, by profile, by both, or by neither.

### Backend is deliberately lean for the stateless parts, matching every card-designer sibling

The barcode-profile catalogue and validator endpoints (`api/v1/card-designer/barcode-profiles`,
`api/v1/card-designer/barcode/validate`) are open, non-tenant-scoped reference data/pure
computation - the same shape BIO-CRD-002's and BIO-CRD-003's own catalogue endpoints use. Only
signature-asset versioning is real, tenant-scoped state, and even there no audit sink, lock
registry or RBAC gate exists - see "Signature-asset versioning" above for why that is the correct,
disclosed shape here, not an omission.

### "Dependencies: BIO-CRD-002" is sequencing plus one narrow, deliberate, read-only reuse

Confirmed by direct inspection: no BIO-CRD-002 file outside `cardFormats.ts` is referenced
anywhere in this feature's code, backend or frontend - the same "sequencing, not coupling"
precedent every sibling card-designer story has documented for its own dependency line.

## Deliberately out of scope for this story

- Merging BIO-CRD-002's, BIO-CRD-003's and this story's own canvases into a single unified
  element/document model (see "A third coordinated tool" above).
- Real barcode/QR symbol rendering (actually encoding data into scannable module patterns) - this
  story validates a barcode object's configuration (profile, quiet zone, size, contrast) and
  positions it on the canvas; a later production/rendering story owns generating the real
  scannable graphic.
- Real image-file upload/validation for a logo - `LogoElement.content` is an opaque reference
  string only, matching BIO-CRD-003's own watermark-image precedent; BIO-CRD-001 owns real asset
  validation.
- Uploading/capturing real signature image bytes from a live signing device - this story's own
  endpoints accept synthetic test fixtures via a JSON base64 body; a real capture device
  integration is a separate concern this story does not own.
- Server-side persistence of a barcode object's or logo's placement on a card design - only
  signature-asset content itself is real backend state; card-canvas placement remains
  client-side, matching BIO-CRD-002's and BIO-CRD-003's own precedent.
- Print/production rendering - a later card production story's concern.
