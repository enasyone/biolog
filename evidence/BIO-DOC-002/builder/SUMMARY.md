# BIO-DOC-002 — Builder Evidence

**Story:** Build native MRZ detection, recognition, parsing and validation
**Commit:** `79316681f4d6e363d538a893f38e639ca40c06f2`
**Branch:** `feature/BIO-DOC-002`
**Owner:** Ifeanyi — Document Intelligence Engineer

## What was built

`api/features/bio-doc-002/Biolog.Features.MrzEngine` — a plain .NET 10 class library, no
ASP.NET Core dependency, no NuGet package references, no image-decoding/CV/ML library (none
exists anywhere in this repo; see `docs/features/bio-doc-002/MRZ-ENGINE.md`). One-way
`ProjectReference` to BIO-DOC-001's `Biolog.Features.DocumentVerify` (this story's own declared
dependency), never the reverse.

Full pipeline, one file per stage:
- `DocumentRaster` / `MrzGlyphFont` — native, dependency-free synthetic-image and glyph-template
  representation (AC01).
- `MrzRegionDetector` — row/column ink-projection profiling to locate the MRZ block; a
  grid-hypothesis-verification algorithm (not contiguous-ink-run based, since several real
  glyphs have blank edge/internal columns that a run-based scan would fragment) (AC01).
- `MrzGlyphRecognizer` — nearest-template glyph matching with a reported confidence (AC01/AC04).
- `MrzFormatRegistry` (`MrzFormat.cs`) — TD1/TD2/TD3 plus configured visa formats (MRV-A/MRV-B)
  as registered data with a `Register()` extension seam (AC02).
- `MrzCheckDigit` — ICAO Doc 9303 7/3/1-weight check-digit algorithm, verified by hand against
  ICAO's own published TD3 worked example before being written into the test suite (AC03).
- `MrzParser` / `MrzReader` — turns recognized glyphs into `MrzFieldRead` (raw + normalized +
  confidence + bounding box + independently-computed check digit, AC02–04) and an
  `MrzReadOutcome` whose `RequiresManualReview` is a derived flag from actual
  confidence/check-digit signals — never a substituted "probably meant" value (AC05).
- `MrzFieldRead.ToDocumentFieldEvidence` — adapts a parsed field into BIO-DOC-001's
  `DocumentFieldEvidence` via the `FieldEvidenceSource.Mrz` member that story reserved for this
  one (AC04, concrete evidence-model round-trip).

## QA-03 lesson applied proactively

`MrzFieldRead` and `MrzReadOutcome` carry the fields a downstream caller actually trusts
(`CheckDigitValid`, `RequiresManualReview`). A plain positional record's public `init` would let
any caller launder a genuinely-failed validation into a record that looks like it passed via
`with { CheckDigitValid = true }`, without re-running the engine at all — the same shape of gap
Chioma's BIO-DOC-001 QA-03 finding caught in `DocumentEvidence`/`DocumentFieldEvidence`/
`BoundingBox`, just via mutation-after-construction instead of construction-bypass (these types
have no business-rule validation of their own to bypass). Both types were built from the start
with `internal init` properties and an explicit `internal` constructor — construction and
copy-modification restricted to this assembly — with reflection-based standing regression
coverage (`MrzResultTypesAreLockedDownTests`, mirroring BIO-DOC-001's
`ConstructionIsMandatoryTests`).

## Self-caught defect before submission

Initial column-cell detection assumed every glyph forms one solid contiguous ink run exactly
`GlyphWidth` (5px) wide. Several hand-authored glyph templates (`1`, `I`, `J`, and others) have
blank edge or internal columns, which fragmented or misaligned cell detection for almost any
realistic MRZ line content (58 test failures on the first `dotnet test` run, including every
TD1/TD2/TD3/visa parsing test). Root-caused via the test failure pattern (systematic, not
content-specific), and replaced with a grid-hypothesis-verification algorithm: read every
ink-bearing column in a row band, then test whether a hypothesized (line length, left edge) pair
explains every ink column falling inside some cell's window at the known fixed pitch — independent
of any single glyph's own ink contiguity. Re-ran the full suite after the fix: 59/59 passing, no
regression to the tests that were already passing.

## Test/quality evidence

- `dotnet test tests/features/bio-doc-002/Biolog.Features.MrzEngine.Tests/...` — 59/59 passed.
- `bash tests/foundation/BIO-DOC-002.test.sh` — 32/32 structural checks passed.
- `bash scripts/quality.sh` (baseline) — see `quality-output.txt` in this directory.
- `bash scripts/quality.sh --full` — every BIO-DOC-002-specific check (structural harness 32/32,
  `.NET mrz-engine unit tests`, traceability inventory, module-project-reference direction,
  secret scan, vulnerability audit) passes. Full run reports `QUALITY: FAIL` from two pre-existing
  gaps unrelated to this story and not touched by it: `tests/frontend`/Playwright have no
  `node_modules` installed (same pre-existing gap noted in BIO-FND-002/008/BIO-DOC-001's own
  evidence), and `Biolog.IntegrationTests.ObservabilityTests.AC04_...` fails with a
  `StringBuilder`/`chunkLength` `ArgumentOutOfRangeException` in `tests/dotnet/Biolog.IntegrationTests/ObservabilityTests.cs:159` — a BIO-FND-009 observability test this story never touches.

## Scope notes (deliberately not built)

- No HTTP endpoint or `IBiologModule`/DI registration: `MrzReader` is entirely static and
  stateless, no AC asks for an endpoint, and BIO-DOC-001 itself stopped short of endpoints for
  the identical reason (no real tenant-resolved HTTP surface exists yet).
- No real JPEG/PNG/TIFF pixel decoding: `DocumentRaster` is a native ink/no-ink grid, matching
  the story's own "synthetic/reference images" AC01 wording and the "synthetic data only"
  builder constraint; real image decoding is a scanner/camera-integration concern for a later
  story.
- TD1/TD2 field-layout correctness rests on structural fidelity to the public ICAO Doc 9303
  standard plus internally self-consistent generated test data (`MrzSampleData`, using
  `MrzCheckDigit.Compute` itself to derive matching check digits); only the TD3 vector is an
  independent, hand-verified literal oracle (ICAO's own published worked example). Documented
  explicitly in `docs/features/bio-doc-002/MRZ-ENGINE.md` rather than left implicit.
