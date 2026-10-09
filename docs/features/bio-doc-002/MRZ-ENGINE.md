# BIO-DOC-002 — Native MRZ Engine

`api/features/bio-doc-002/Biolog.Features.MrzEngine` is a plain .NET class library (no ASP.NET
Core dependency, no NuGet package references, no image-decoding library) implementing native
detection, recognition, parsing and check-digit validation of the Machine Readable Zone (MRZ) -
the OCR-B text strip ICAO Doc 9303 defines for passports, ID cards and visas. It builds directly
on BIO-DOC-001's `Biolog.Features.DocumentVerify` evidence model: parsed fields adapt into
`DocumentFieldEvidence` via the `FieldEvidenceSource.Mrz` member that story already reserved for
this one.

## Model

| Type | Kind | Purpose |
| --- | --- | --- |
| `DocumentRaster` | class | Native, dependency-free ink/no-ink pixel grid standing in for a decoded page image (AC01). |
| `MrzGlyphFont` | static | The 37-character MRZ alphabet (`A`-`Z`, `0`-`9`, `<`) as 5x7 bitmap templates; nearest-template matching for a glyph cell (AC01/AC04). |
| `MrzRegionDetector` | static | Row/column ink-projection profiling to locate the MRZ block within a raster (AC01). |
| `MrzGlyphRecognizer` | static | Slices a detected line into cells and recognizes each against `MrzGlyphFont` (AC01/AC04). |
| `MrzFormatKind`, `MrzFieldSpec`, `MrzFormatDescriptor`, `MrzFormatRegistry` | enum/record/static | Data-driven TD1/TD2/TD3 and configured visa (MRV-A/MRV-B) field layouts (AC02). |
| `MrzCheckDigit` | static | ICAO Doc 9303 Part 3 §4.9 check-digit algorithm, computation only - never comparison or correction (AC03). |
| `MrzFieldRead` | record (locked down) | One parsed field: raw + normalized value, confidence, bounding box, read vs. computed check digit (AC02/AC03/AC04). |
| `MrzReadOutcome` | record (locked down) | The pipeline's end-to-end result: format, region, fields, composite check-digit validity, `RequiresManualReview` (AC05). |
| `MrzReader` | static | Single public entry point: `Read(DocumentRaster) -> MrzReadOutcome`, chaining detection, recognition, parsing and validation. |
| `MrzParser` | static | Turns recognized glyphs into `MrzFieldRead`s and an `MrzReadOutcome`, deciding what requires manual review. |

## Design decisions worth a reader's attention

### Why no image library, and what a "synthetic/reference image" means here

No image-decoding, computer-vision or ML library is referenced anywhere in this repo (checked:
no SkiaSharp, ImageSharp, OpenCvSharp, System.Drawing or ML.NET in any `.csproj`). BIO-DOC-001's
own `DocumentEvidence` doesn't store decoded pixel bytes either - only a SHA-256 hash and MIME
type (ADR-0003 Decision 5: "evidence references, not evidence copies"). The closest established
precedent in this codebase is `api/Biolog.Api/Features/BrandAssets/RasterImageInspector.cs`
(BIO-CRD-001): a hand-written, dependency-free PNG/JPEG *header* reader that never decodes pixel
data, paired with a test-only synthetic-image builder
(`tests/dotnet/Biolog.UnitTests/Features/BrandAssets/SyntheticImages.cs`).

`DocumentRaster` follows the same spirit but one level further in, because AC01 explicitly
requires *region detection* - something a header reader alone cannot do. It is a native ink/
no-ink pixel grid: real container-format (JPEG/PNG/TIFF) decoding into this grid is a scanner/
camera-integration concern for a later story, exactly as BIO-DOC-001 scoped out HTTP endpoints
and persistence. `MrzGlyphFont`'s bitmap templates are a native stand-in for the real OCR-B font
MRZ documents are printed in - what matters for this engine's correctness is that recognition is
genuine nearest-template matching against a known alphabet, not a pass-through of pre-labelled
input; the specific glyph shapes are this engine's own, not a reproduction of OCR-B itself.

### Detection is real row/column ink-projection profiling, not given the region

`MrzRegionDetector` groups a raster's rows into text-line bands by blank-row gaps, keeps only
bands whose height exactly matches `MrzGlyphFont.GlyphHeight` (a non-text region such as a photo
block will not, by construction, share that height in these fixtures), profiles each surviving
band's column-ink projection into evenly-pitched glyph cells, and only accepts a run of 2 or 3
vertically-consecutive lines sharing an identical, registered column count (30/36/44) as the MRZ
block. A single stray matching line, zero matching lines, or two independently-valid candidate
blocks are all refused (`mrz_region_not_detected` / `multiple_mrz_candidates`) rather than picked
between - the same "never guess" discipline AC05 requires downstream is applied here too.

### "Configured visa formats" is a registry, not a fourth/fifth hard-coded case

`MrzFormatRegistry` holds every format (TD1/TD2/TD3 plus the two visa sizes, MRV-A/MRV-B) as data
- a `MrzFormatDescriptor` with a field-position table and a document-code discriminator function -
not a switch statement the detector/parser special-cases per format. `Register()` is the same
seam a later story would use to add a further country-specific visa variant without touching this
engine's detector, recognizer or parser. TD3 and MRV-A share 2x44 geometry (as do TD2/MRV-B at
2x36); `ResolveByDocumentCode` disambiguates from the document-code character actually read at
line 0 column 0 (ICAO's own convention: a visa's document code starts with `V`), returning `null`
- never an arbitrary pick - if zero or more than one candidate matches.

**Scope note on visa field layouts**: MRV-A/MRV-B here reuse TD3/TD2's column geometry (accurate
per ICAO Doc 9303 Part 7) with `HasCompositeCheckDigit = false` (visas carry no final composite
check digit) and a `V` document-code discriminator. A country-specific visa layout that diverges
further is a new registry entry, not a change to this engine.

### Check-digit computation is independent of comparison, by construction

`MrzCheckDigit.Compute` only computes; it has no notion of "the value that was read" at all. The
parser always reports both `ReadCheckDigit` (verbatim from the raster) and `ComputedCheckDigit`
(freshly computed from the *raw, recognized* field value) side by side, and a mismatch is
reported as `check_digit_mismatch:<field>` - never resolved by trusting one side or "fixing" the
other. `MrzCheckDigitTests` is verified against ICAO Doc 9303 Part 4 Appendix B's own published
TD3 worked example (the fictional "ERIKSSON, ANNA MARIA" passport), hand-checked against the
7/3/1-weight algorithm before being written into the suite - not merely asserted against this
implementation's own output. TD1/TD2/MRV sample data is built programmatically
(`MrzSampleData`, using `MrzCheckDigit.Compute` itself to derive self-consistent check digits from
chosen field values) rather than hand-counted `<`-filler literals, to eliminate a whole class of
off-by-one test-authoring bugs; only the TD3 vector is an independent literal oracle.

### AC05: manual review is a derived flag, never a corrected value

Nothing in this engine ever substitutes a "probably meant" character or check digit to make a
result look clean. `MrzGlyphFont.MatchCell` always returns its single nearest-template match plus
a confidence score - it never declines to guess a character, and it never hides how unsure it was
either. `MrzParser` turns a field confidence below `MinAcceptableConfidence` (0.85) into
`low_confidence:<field>`, and any missing/mismatched check digit into a `check_digit_mismatch`/
`composite_check_digit_mismatch` reason; `RequiresManualReview` is `true` whenever any reason was
recorded. The raw and normalized values a low-confidence or checksum-failing field carries are
exactly what was read - proven by `MrzTd3ParserTests.AC03_AC05_a_mismatched_check_digit_is_reported_not_silently_corrected`
and `MrzReaderTests.AC05_a_low_confidence_character_is_flagged_for_review_without_changing_the_raw_value_read`.

### `MrzFieldRead` and `MrzReadOutcome` are locked down against post-hoc tampering

Both types carry the fields a downstream caller actually trusts (`CheckDigitValid`,
`RequiresManualReview`) - the AC03/AC05 surface this whole engine exists to get right. A plain
positional record's `init` accessors are public, so `with { CheckDigitValid = true }` would let
any caller launder a genuinely-failed validation into a record that looks like it passed, without
re-running the engine at all - the same shape of gap BIO-DOC-001's QA-03 finding caught in
`DocumentEvidence`/`DocumentFieldEvidence`/`BoundingBox` (a public `init` bypassing `Create`'s
validation), just via mutation-after-construction instead of construction-bypass, since these
types have no validation rule of their own to bypass. Every property here is `internal init` with
an explicit `internal` constructor, restricting construction and copy-modification to this
assembly; `ConstructionIsMandatoryTests`-equivalent reflection coverage
(`MrzResultTypesAreLockedDownTests`) asserts no public constructor and no public/internal-from-
outside setter exists, the same standing regression pattern BIO-DOC-001 established. Intermediate
types with no downstream trust decision riding on them (`MrzDetectionResult`, `MrzDetectedLine`,
`MrzRecognizedCell`) are left as ordinary positional records - locking down every type in the
pipeline regardless of whether anything trusts its fields would be exactly the kind of
unnecessary abstraction the project's own guidance warns against.

### Not wired into `Biolog.Api`

Unlike BIO-DOC-001's `DocumentModule`/`IBiologModule` adapter, this story registers nothing into
the API host. `MrzReader` is entirely static and stateless - a pure function from
`DocumentRaster` to `MrzReadOutcome` - so there is no instance service for a DI container to own,
and no HTTP endpoint is in scope (none of AC01-AC05 asks for one; BIO-DOC-001 itself stopped short
of endpoints for the identical reason - no real tenant-resolved HTTP surface exists yet for one to
be safe against). A later orchestration story wiring MRZ capture into a live `DocumentSession`
(consuming `MrzFieldRead.ToDocumentFieldEvidence`) is where a module/endpoint would belong.

### Evidence path

Builder evidence lives at flat `evidence/BIO-DOC-002/` (not `evidence/features/bio-doc-002/`),
matching this story's own dispatched packet instruction ("Produce builder evidence under
`evidence/BIO-DOC-002/builder/`") over `governance/feature-path-ownership.json`'s template -
the same documented "ownership-template gap" BIO-DOC-001, BIO-FND-007 and BIO-FND-008 all followed.

## Reproducing the tests

```sh
dotnet test tests/features/bio-doc-002/Biolog.Features.MrzEngine.Tests/Biolog.Features.MrzEngine.Tests.csproj
bash tests/foundation/BIO-DOC-002.test.sh
```
