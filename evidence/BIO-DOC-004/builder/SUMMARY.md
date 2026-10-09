# BIO-DOC-004 — Builder Evidence

**Story:** Build template-driven and generic visual OCR
**Commit:** `87daeb50fd26bd86570c519ee5bbaf6f0208003b`
**Branch:** `feature/BIO-DOC-004`
**Owner:** Ifeanyi — Document Intelligence Engineer

## What was built

`api/features/bio-doc-004/Biolog.Features.OcrEngine` — a plain .NET 10 class library, no ASP.NET
Core dependency, no NuGet package references, no OCR/image-decoding/CV library (none exists
anywhere in this repo). One-way `ProjectReference` to BIO-DOC-001's `Biolog.Features.DocumentVerify`
(this story's one declared dependency — deliberately not BIO-DOC-002's `MrzEngine` or BIO-DOC-003's
`BarcodeEngine`, neither a declared dependency).

- `TemplateOcrEngine` — reads a document's fields from a declared `OcrDocumentTemplate` (AC01):
  field type, raw value, normalized value, confidence, source coordinates. No dynamic detection
  needed — a template already says where each field sits.
- `GenericOcrEngine` + `OcrTextLineDetector` — reads arbitrary text blocks with no template (AC02),
  via real ink-projection text-line detection: BIO-DOC-002's `MrzRegionDetector` grid-hypothesis
  algorithm reused directly, generalized from a fixed line-length whitelist to any length, keeping
  its "ambiguous hypothesis is refused, never guessed between" discipline. `GenericTextBlock`
  carries no field type or field name at all.
- `OcrGlyphFont` — English glyph templates (space, digits, uppercase, six punctuation marks) — the
  character set identity-document visual zones are actually printed in.
  `IOcrLanguagePack`/`OcrLanguagePackRegistry` is the "next language pack" seam AC03 asks for;
  English ships, French is named (`OcrLanguageCode.French`) and registrable but not built.
- `OcrTemplateField.Critical` (AC04) gates manual review per field, not as a blanket confidence
  check. Recognition always reports its best template match plus a real confidence, never a
  guessed/placeholder character (AC05).
- `OcrFieldRead`/`TemplateOcrResult`/`GenericTextBlock` are locked down (internal init, no public
  constructor) against post-hoc `with`-tampering of `Confidence`/`RequiresManualReview` — the same
  QA-03 lesson applied proactively in BIO-DOC-002/003.

## Design continuity across the three document-intelligence stories

This is the third native engine built this batch (MRZ, barcode, now OCR), and it reuses two
already-debugged pieces of prior work directly rather than re-deriving them:

- The grid-hypothesis text-line detection algorithm (`OcrTextLineDetector`) is BIO-DOC-002's
  `MrzRegionDetector` column-cell logic, generalized to accept any line length instead of a fixed
  30/36/44 whitelist. BIO-DOC-002 only arrived at this algorithm after a run-based approach
  fragmented on glyphs with blank edge/internal columns (`'1'`, `'I'`) — this story starts from
  that already-correct design.
- The `internal init` + explicit `internal` constructor lockdown pattern for trust-bearing result
  types, applied to all three new types here before any reviewer looked, exactly as it was for
  BIO-DOC-002's `MrzFieldRead`/`MrzReadOutcome` and BIO-DOC-003's `BarcodeDecodeResult`/
  `BarcodeDecodeAttempt`.

## Self-caught defect before submission

One test used a manual single-pixel corruption to prove "a corrupted cell still returns its best
match" for the glyph `'O'` — that specific single-pixel flip happened to move `'O'` closer to `'D'`
in the template table than to `'O'` itself (an artifact of this story's own hand-authored 5x7 font,
not a production defect). Fixed by using the already-established `GlyphCorruption` self-verifying
fixture helper (greedily flips pixels, keeping only flips the real `OcrGlyphFont.MatchCell` itself
confirms preserve the intended nearest match) instead of a manual, unverified flip.

## Test/quality evidence

- `dotnet test tests/features/bio-doc-004/Biolog.Features.OcrEngine.Tests/...` — 34/34 passed.
- `bash tests/foundation/BIO-DOC-004.test.sh` — 28/28 structural checks passed.
- `bash scripts/quality.sh --full` — every BIO-DOC-004-specific check (structural harness,
  `.NET ocr-engine unit tests`, traceability inventory, module-project-reference direction, secret
  scan, vulnerability audit) passes; see `quality-output.txt`. Overall run reports `QUALITY: FAIL`
  from the same three pre-existing gaps unrelated to this story documented in BIO-DOC-002/003's own
  evidence: `tests/frontend`/Playwright have no `node_modules` installed, and
  `Biolog.IntegrationTests.ObservabilityTests` fails with a `StringBuilder`/`chunkLength`
  `ArgumentOutOfRangeException` (a different specific test method again this run, same flaky
  BIO-FND-009 root cause).

## Scope notes (deliberately not built)

- Full mixed-case prose OCR (lowercase, general fonts) — this engine targets identity-document
  visual zones specifically, which are overwhelmingly uppercase for the same reason MRZ itself is;
  documented as a deliberate scope match, not a shortcut.
- A real French language pack — AC03 asks for the seam to be architected, not for French glyphs to
  ship; `OcrLanguagePackRegistryTests` proves the seam works via a stub pack.
- Multi-line paragraph/block grouping for generic OCR — each detected line is its own block.
- No HTTP endpoint or `IBiologModule`/DI registration — same reasoning as BIO-DOC-002/003.
