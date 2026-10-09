# BIO-OPS-005 — Document Verify evidence coordinator

## Why this story exists

Seyi's own acceptance review (SY-OPS005-01, High) found that the approved `/document-verify` UI
calls four endpoints (`mrz`, `barcode`, `ocr`, `nfc`) that did not exist, and that the API host
referenced only BIO-DOC-001/009/010/011 — never the BIO-DOC-002/003/004/007 engine projects.
Kemi's ruling confirmed the gap was pure composition — BIO-DOC-001's own session/evidence model
already has every operation a coordinator needs — and specified the exact shape to build it in.
This document records that shape as delivered.

## AC → evidence map

- **AC01** (each engine returns provenance/confidence): every `EvidenceStepResult` now carries a
  required `confidence: number` and `provenance: { source, evidenceSequenceNumber?,
  fieldEvidenceSequenceNumbers? }` — `web/features/documents/evidence.ts`.
- **AC02** (cross-source mismatch is visible): `CheckAuthenticityAsync` runs
  `ComparisonEngine.Compare` per field and calls `IDocumentSessionStore.RaiseConflict` on every
  field BIO-DOC-008 found disagreeing — `DocumentVerifyCoordinatorService.cs`.
- **AC03** (unsupported NFC is not pass/fail): `RunNfcAsync` never constructs an
  `IChipDataGroupTransport` (none exists) and reports `state: 'not-checked'`,
  `provenance.source: 'unavailable'` — never fabricated. `AuthenticityPolicyEngine.BuildPassiveAuthenticationCheck`
  already reports `NotPerformed` (not `Failed`) when `NfcCorroboration.Available` is false.
- **AC04/AC05** (manual review cannot rewrite raw evidence; document data does not silently
  overwrite PERSON): `DocumentEvidence`/`DocumentFieldEvidence` stay append-only (BIO-DOC-001's
  own design, untouched); `TryProposePersonFieldChangeAsync` only ever PROPOSES a PERSON-field
  change, never applies one, and only once a person is already linked to the session.

## Dependency shape (Kemi's ruling, point 2)

`Biolog.Features.DocumentVerifyCoordination.csproj` has a direct `ProjectReference` to
BIO-DOC-001/002/003/004/007/008 — the same precedent BIO-DOC-009/010 already used for their own
declared dependencies. `AuthenticityAssessmentService` (BIO-DOC-009) and
`DocumentTemplatePackService` (BIO-DOC-010) are stateful, DI-registered services, reached instead
through this project's own `IAuthenticityAssessmentGateway`/`IDocumentIdentificationGateway`,
implemented as adapters in `Biolog.Api.Modules.BioOps005Module` wrapping those already-registered
services directly — the same `ICardIssueGateway` shape every cross-story seam in this epic uses.
Neither gateway interface references BIO-DOC-009/010's own enum types (`AuthenticityDisposition`,
`AuthenticityCheckOutcome`) — the adapter is the one place either side's enum is known to the
other; the gateway's own return type already carries the mapped
`web/features/documents/evidence.ts` `EvidenceCheckState` wire string.

## The raster boundary (point 6) — disclosed, not solved

No image-decoding library exists anywhere in this repo (confirmed: no SkiaSharp/ImageSharp/
OpenCvSharp/System.Drawing/ML.NET reference in any `.csproj`), and BIO-DOC-002's own
`DocumentRaster.cs` already discloses this as an intentional boundary: "turning real
container-format bytes into a pixel grid is a scanner/camera-integration concern for a later
story." `SyntheticDocumentFixture` builds ONE deterministic Nigerian ePassport — matching
`NigeriaDocumentPackV1.PassportEntry`'s own field vocabulary exactly — and renders it into each
engine's own native raster type using that engine's own public glyph-font/symbology tables (the
same technique every BIO-DOC-002/003/004 test suite already uses to prove itself). Every call from
`RunMrzAsync`/`RunBarcodeAsync`/`RunOcrAsync` onward runs the REAL engine code
(`MrzReader.Read`, `BarcodeDetectionService.Scan`, `TemplateOcrEngine.Read`) against that raster —
check-digit validation, glyph recognition, decode confidence, and bounding boxes are all genuinely
computed, not fabricated. Only the input image is synthetic.

MRZ and OCR each independently compute the fixture's passport number from a correctly-weighted
ICAO 7-3-1 check-digit algorithm (`MrzCheckDigit.Compute`, called at fixture-build time, not
hand-derived) and a Code 39 barcode payload decoded through the real `Code39Decoder`/`Code39Table`
— every decoded/recognized value genuinely round-trips through the real engine, it is not asserted
by the fixture.

## Cross-source field aliasing

MRZ's own field-naming convention (`MrzParser.BaseName`, e.g. `document_number`) and OCR's own
template-defined field names (this story's own `SyntheticDocumentFixture.BuildOcrTemplate`, e.g.
`PassportNumber`) are independent, standards-driven vocabularies — neither engine knows about the
other's naming. `document_number` is this coordinator's own chosen canonical comparison key:
`RunOcrAsync` aliases its own `PassportNumber` field onto it before calling
`IDocumentSessionStore.AppendFieldEvidence`; `IdentifyDocumentAsync`'s own
`BuildObservedFieldsFromOcrEvidence` aliases it back to `PassportNumber` before calling
`IdentifyFromEvidenceAsync`, so BIO-DOC-010's own governed pack entry sees its own expected field
name. Barcode's own evidence (`BarcodeDecodeResult.RawPayloadHash` — a SHA-256 hash, never the
decoded payload text, by that engine's own design to avoid carrying identity PII in plain text) is
never fed into `ComparisonEngine.Compare` at all — there is no comparable VALUE this layer can
honestly recover from a hash, matching BIO-DOC-008's own declared dependency set (which
deliberately excludes BIO-DOC-003) and BIO-DOC-003-AC04's own rule ("no decoded barcode alone is
labelled authentic"). `date_of_birth` is deliberately NOT cross-compared either: MRZ's own
normalization (`MrzParser.NormalizeDate`, 2-digit ICAO year) and OCR's own raw ISO value (4-digit
year) are genuinely different strings for the same real date — comparing them would report a
false mismatch caused by a formatting difference between two engines' own conventions, not a
real authenticity concern.

## Session threading without a port redesign (point 4)

`web/features/documents/ports.ts`'s `DocumentsPort` interface is unchanged — all eight methods
stay zero-arg. `capture`'s HTTP response sets an `HttpOnly`, `SameSite=Strict`,
`Path=/api/v1/documents` cookie named `document_session_id`; every later step's `fetch` call
(`credentials: 'same-origin'`, the browser default anyway for a same-origin request) sends it
back automatically, and `DocumentVerifyCoordinatorEndpoints.RequireSessionCookie` resolves the
session from it server-side, throwing the well-formed `no_active_session` (409) problem response
(never a 500) when it is missing, expired, or names a session that does not exist for the
resolved tenant.

## Pre-person sessions and PERSON proposals

`CaptureAsync` opens a session with `personId: null` — BIO-DOC-001's own "pre-person onboarding"
path, the identical kiosk scenario that type's own domain-model remarks describe. None of this
story's own eight operations itself links a person to a session — that is a later onboarding
story's job. `TryProposePersonFieldChangeAsync` checks `DocumentSession.PersonId is not null`
before ever calling `IDocumentSessionStore.ProposePersonFieldChange`; a pre-person session
correctly produces no PERSON-field proposals yet, which is correct domain behaviour (there is no
person to propose a change for), not a gap. Once some other flow links a person to the session
(this story's own test suite demonstrates it by calling `IDocumentSessionStore.LinkPerson`
directly, standing in for that future onboarding step), the exact same `RunMrzAsync`/`RunOcrAsync`
code starts producing proposals, with no flag or special-case branch required.

## Concurrency: why a lock registry, not just reordering

BIO-DOC-008's own `IVersionStore.AllocateVersion`/`CommitVersion`/`ReleaseVersion` two-phase
protocol requires the CALLER to track and present its own "next expected version" per document
session (confirmed from that story's own test suite, which tracks it the identical way). Two
concurrent `CheckAuthenticityAsync` calls for the SAME session would otherwise race: each
independently computes its own next version number
(`DocumentComparisonVersioningState.NextVersionNumber`), and whichever one's `AllocateVersion`
call runs second — for a version number that isn't exactly the first call's still-uncommitted
`committed + 1` — is rejected with `StaleComparisonVersionException`, even though neither caller
did anything wrong. `DocumentVerifyLockRegistry` (mirroring BIO-INT-002's own `ImportLockRegistry`/
BIO-DOC-011's own `DocumentTemplateDraftLockRegistry` exactly) serializes every
`CheckAuthenticityAsync` call for a given session, so the "compute my next version" step and the
"reserve it" step always happen as one atomic unit from every other caller's point of view — the
canonical fix this epic has already converged on for this exact class of problem: reordering two
independently-failable operations never eliminates a race, only a true mutual-exclusion lock does.

## Why no provenance/RBAC gate was added to these 8 endpoints

Kemi's ruling does not call for one, and the established precedent for this EXACT route family
(`/api/v1/documents/*`) already explains why: BIO-DOC-009's own `ActorResolver`/`AuthenticityEndpoints`
remarks (Zainab's BIO-DOC-009-F01 review, already reviewed and accepted) record that this is a
kiosk-style evidence-capture flow with no real caller identity to assert (BIO-UX-009 never
collects one), every step here operates only on the caller's own just-captured session (no
cross-caller lookup by a guessable id), and there is still no RBAC/trusted-actor-provenance
boundary anywhere in this codebase for a composition-tier story to build unilaterally. This
mirrors that same disclosed seam rather than inventing a new one.
