# BIO-DOC-004 — Template-Driven and Generic Visual OCR

`api/features/bio-doc-004/Biolog.Features.OcrEngine` is a plain .NET 10 class library (no ASP.NET
Core dependency, no NuGet package references, no OCR/image-decoding/CV library — none exists
anywhere in this repo) implementing two OCR engines: one that reads known fields from a declared
document template, and one that reads arbitrary text with no template at all. One-way
`ProjectReference` to BIO-DOC-001's `Biolog.Features.DocumentVerify` (this story's one declared
dependency — deliberately not BIO-DOC-002's `MrzEngine` or BIO-DOC-003's `BarcodeEngine`, neither
of which is a declared dependency).

## Model

| Type | Kind | Purpose |
| --- | --- | --- |
| `DocumentRaster` | class | Native ink/no-ink pixel grid, the same boundary concept as BIO-DOC-002's `DocumentRaster` and BIO-DOC-003's `BarcodeRaster`, duplicated rather than shared. |
| `OcrGlyphFont` | static | English glyph templates: space, digits, uppercase letters, and identity-document punctuation (AC01/AC02/AC03). |
| `IOcrLanguagePack`, `EnglishLanguagePack`, `OcrLanguagePackRegistry` | interface/class | The "next language pack" seam (AC03). |
| `OcrFieldType` | enum | Field semantics a template can assign (AC01) — never assigned by generic OCR (AC02). |
| `OcrTemplateField`, `OcrDocumentTemplate`, `OcrTemplateRegistry` | record/class | Declared field regions for a known document layout (AC01). |
| `OcrFieldRead` | record (locked down) | One template field's result: type, raw value, normalized value, confidence, source coordinates (AC01). |
| `TemplateOcrResult` | record (locked down) | All fields read from a template, plus `RequiresManualReview` (AC04). |
| `TemplateOcrEngine` | static | Reads a raster's declared field regions (AC01/AC04/AC05). |
| `OcrTextLineDetector` | static | Finds arbitrary text lines anywhere on a page — no template, no fixed length whitelist (AC02). |
| `GenericTextBlock`, `GenericOcrResult`, `GenericOcrEngine` | record/class/static | Raw recognized text with no field semantics (AC02). |

## Design decisions worth a reader's attention

### Character set: uppercase, digits and identity-document punctuation, not full prose

`OcrGlyphFont` covers space, `0`-`9`, `A`-`Z` and six punctuation marks (`. , ' - /` and space) —
not lowercase or general mixed-case prose. Identity-document visual zones (the printed area beside
a photo, distinct from the MRZ) are overwhelmingly printed in uppercase for the same legibility/
OCR-friendliness reason MRZ itself is uppercase-only (ICAO Doc 9303's own design choice, already
relied on by BIO-DOC-002). This is a genuine, defensible scope match for *identity-document* OCR
specifically, not a shortcut dressed up as a limitation — full mixed-case prose recognition is a
natural extension for a future language pack, not a gap silently left unaddressed. A duplicate,
larger lowercase-inclusive font was considered and rejected: it roughly doubles hand-authored
glyph-transcription risk for a character range this domain does not actually need.

### Template OCR needs no detection; generic OCR needs real detection

A document template already declares exactly where each field sits (pixel-precise `Left`/`Top`/
`CharacterCount`), so `TemplateOcrEngine` slices that declared region directly into glyph cells at
this engine's fixed pitch — the same "known layout, no detection needed" shape BIO-DOC-002's MRZ
field extraction used for its own fixed field positions. Generic OCR has no such advantage (AC02:
there is no template), so `OcrTextLineDetector` performs real ink-projection text-line detection.

### `OcrTextLineDetector` reuses BIO-DOC-002's proven grid-hypothesis algorithm, generalized

BIO-DOC-002's `MrzRegionDetector` initially inferred glyph cell boundaries from contiguous ink
runs, then had to be rewritten as grid-HYPOTHESIS verification after discovering several real
glyphs (`'1'`, `'I'`) have blank edge/internal columns that fragment a run-based scan. This
detector starts from that already-debugged algorithm directly rather than re-deriving it, with one
generalization: MRZ only ever accepted a line length from a fixed registry (30/36/44); generic
text has no such constraint, so any line length ≥1 is a valid detection, as long as exactly one
`(leftStart, lineLength)` hypothesis explains every ink column in the row band (an ambiguous match
— more than one hypothesis validating — is refused outright, never guessed between, the same
discipline BIO-DOC-002 applied to its own multi-candidate case).

### AC02: a text block is one detected line — no paragraph grouping

Multi-line paragraph/block grouping (deciding which adjacent lines belong to one logical "block")
is out of scope; each detected line is its own `GenericTextBlock`. Documented explicitly rather
than silently narrowing what "block" means.

### AC03: English is the baseline; French is architected, not built

`OcrLanguagePackRegistry.CreateDefault()` registers only `EnglishLanguagePack`. `OcrLanguageCode`
already names `French`, and `Register()` is the seam a future story uses to add a real French pack
(with its accented-character glyphs) without either OCR engine changing —
`OcrLanguagePackRegistryTests` proves this by registering a stub French pack through the exact
same seam a real one would use. No accented-character glyph set was invented for a language this
story does not ship.

### AC04: `Critical` is per-field, not a blanket confidence gate

`OcrTemplateField.Critical` marks which fields a low-confidence read must escalate for human
review; a non-critical field (a decorative note, a watermark caption) can read at any confidence
without gating the result. `TemplateOcrEngine.MinAcceptableConfidence` (0.85) is the same threshold
BIO-DOC-002's `MrzParser` uses for its own low-confidence gate.

### AC05: never a guessed correction

`OcrGlyphFont.MatchCell` always returns its single nearest-template match plus a confidence score
— it never declines to guess a character, and it never hides how unsure it was. Both engines
record exactly what was matched, at whatever confidence resulted; nothing in this engine
substitutes a "probably meant" character to make a low-confidence read look clean. A pure-noise or
blank cell nearest-matches the space glyph deterministically (confidence reflects how close the
match actually was) rather than being omitted or replaced with a placeholder.

### Locked down against post-hoc tampering (the QA-03 lesson, again)

`OcrFieldRead`, `TemplateOcrResult` and `GenericTextBlock` all carry the fields a downstream
caller actually trusts (`Confidence`, `RequiresManualReview`). Every property is `internal init`
with an explicit `internal` constructor — the same reasoning as BIO-DOC-002's `MrzFieldRead`/
`MrzReadOutcome` and BIO-DOC-003's `BarcodeDecodeResult`/`BarcodeDecodeAttempt`.

### Not wired into `Biolog.Api`

Same reasoning as BIO-DOC-002/003: both engines are stateless pure functions from a raster (plus,
for templates, a registry lookup) to a result. No AC asks for an HTTP endpoint, and no real
tenant-resolved HTTP surface exists yet for one to be safe against.

## Reproducing the tests

```sh
dotnet test tests/features/bio-doc-004/Biolog.Features.OcrEngine.Tests/Biolog.Features.OcrEngine.Tests.csproj
bash tests/foundation/BIO-DOC-004.test.sh
```
