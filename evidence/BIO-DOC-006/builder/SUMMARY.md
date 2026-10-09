# BIO-DOC-006 — Builder Evidence

**Story:** Build automatic document type identification
**Commit:** `99084942159e67b986af73eb1cd445ce654c2ed3`
**Branch:** `feature/BIO-DOC-006`
**Owner:** Ifeanyi — Document Intelligence Engineer

## What was built

`api/features/bio-doc-006/Biolog.Features.DocumentClassifier` — a plain .NET 10 class library, no
ASP.NET Core dependency, no NuGet package references. Three `ProjectReference`s — BIO-DOC-002
(MrzEngine), BIO-DOC-003 (BarcodeEngine), BIO-DOC-004 (OcrEngine) — exactly this story's declared
dependencies, deliberately not BIO-DOC-001 or BIO-DOC-005.

- `DocumentIntelligenceRegistry`/`DocumentIntelligenceRegistryEntry` — the versioned catalog
  mapping a classified (Country, DocumentClass, Series, Side) identity to the OCR template used to
  read it (AC03), with a monotonic-version guarantee (`StaleRegistryVersionException`) on
  `Register`.
- `Classifier` — composes MRZ/barcode/OCR evidence already produced by the three sibling engines
  into a ranked classification (AC01): an OCR template match is the only signal that can name a
  specific series/version, MRZ issuing-state and barcode-symbology evidence only corroborate it.
- `DocumentClassificationOutcome`/`DocumentClassCandidate` — locked down (`internal init`, no
  public constructor) result types carrying `Unknown` (AC02), `DetectedSide`/`ExpectedSide`/
  `SideMismatch` (AC04), and `RequiresOperatorConfirmation`/`ConfirmationReasons` (AC05).
- `DocumentSide` — this feature's own small `Front`/`Back`/`Unknown` vocabulary, not a reuse of
  BIO-DOC-001's `DocumentVerify.Model.DocumentSide` (BIO-DOC-001 is not a declared dependency of
  this story).

## Why an OCR template match is the primary signal, and MRZ/barcode only corroborate

AC01 asks for country, document class **and** series/version together. ICAO Doc 9303 MRZ carries
an issuing-state code and a coarse document-code letter but no series/version field at all — two
different print runs of the same passport type read an identical MRZ. A raw barcode payload is
opaque bytes at this layer (interpreting it is BIO-DOC-005's job, deliberately not a dependency
here). Only a registered `OcrDocumentTemplate` match can genuinely name a specific series, because
the template is what a human operator registered as "this is what series X's layout looks like".
`Classifier.Classify` therefore only ever produces a candidate from a template match, and uses
MRZ/barcode evidence purely to corroborate one, never to invent a candidate on its own. Full
reasoning in `docs/features/bio-doc-006/DOCUMENT-CLASSIFIER.md`.

## AC03's "versioned" registry: monotonic version, not full ADR-0001 Decision 3 CAS

`DocumentIntelligenceRegistry` is a platform-wide reference catalog populated by configuration, not
a mutable per-request aggregate multiple tenants write to concurrently — so there is no
concurrent-writer race for a full compare-and-set to guard against. `Register` still enforces that
an entry can never regress a (Country, DocumentClass, Series, Side) key to an older
`VersionNumber`. Documented explicitly to preempt the "why not full CAS like `ParserRecord`"
question.

## AC02/AC05: ranked candidates are still shown even when the outcome is Unknown

`Unknown` (no candidate clears `Classifier.MinAcceptableConfidence`, 0.70) does not clear
`Candidates` — AC01's ranked list is still reported, only `SelectedTemplateId` and the implicit
trust signal are withheld. `RequiresOperatorConfirmation` is `true` exactly when
`ConfirmationReasons` is non-empty, covering four explicit, independently-testable conditions:
`no_candidate_matched`/`confidence_below_threshold` (AC02), `side_mismatch:...` (AC04),
`ambiguous_top_candidates` (top two candidates within `Classifier.AmbiguityMargin`, 0.05, of each
other — never silently picked between), and `template_match_requires_review:...` (the winning
template's own `TemplateOcrResult.RequiresManualReview` reused directly, never re-derived).

## Test/quality evidence

- `dotnet test tests/features/bio-doc-006/Biolog.Features.DocumentClassifier.Tests/...` — 21/21
  passed. Every test exercises the real `MrzReader.Read`/`TemplateOcrEngine.Read`/
  `Code39Decoder.TryDecode` pipeline (own local, trimmed copies of each sibling engine's synthetic
  canvas fixtures) to obtain real `MrzReadOutcome`/`TemplateOcrResult`/`BarcodeDecodeResult`
  instances, never fabricated ones — those types are locked down (`internal` constructor)
  specifically to prevent that, including from this story's own tests.
- Self-caught bug: the first ranking test used `GlyphCorruption` with `flipCount: 6` (the value
  BIO-DOC-004's own tests use to trip its 0.85 critical-field bar) and asserted the resulting
  confidence was below this classifier's 0.70 whole-document bar — it was not (0.83 > 0.70).
  Confidence = `1 - hammingDistance/35` (`OcrGlyphFont.MatchCell`), so a distance of 6 only reaches
  0.83; fixed by computing the flip count actually needed (`flipCount: 13` → 0.371 distance ratio →
  0.629 confidence) and documenting the arithmetic inline rather than picking a number that merely
  happened to work by observation.
- `bash tests/foundation/BIO-DOC-006.test.sh` — 33/33 structural checks passed.
- `bash scripts/quality.sh` (baseline) — PASS.
- `bash scripts/quality.sh --full` — every BIO-DOC-006-specific check passes; see
  `quality-output.txt`. `.NET integration tests` (the `Biolog.IntegrationTests.ObservabilityTests`
  flaky failure documented across BIO-DOC-002/003/004's evidence) passed clean this run — a third
  consecutive story (after both of BIO-DOC-005's runs) confirming it is genuinely nondeterministic,
  not a permanent regression. Overall `QUALITY: FAIL` only from `tests/frontend`/Playwright having
  no `node_modules` installed — the same pre-existing, unrelated gap documented in every prior
  BIO-DOC-00x story's evidence this batch.

## Acceptance round 1: FAIL, fixed and resubmitted

**Commit:** `17498eb4ccbc337ddaf2930a6219a3e4f4841650`

Chioma's acceptance review (`evidence/BIO-DOC-006/acceptance/ACCEPTANCE.md`, BIO-DOC-006-QA-01,
High, AC03 FAIL/Functional-Regression FAIL) found `DocumentIntelligenceRegistry.Register` replacing
`_byIdentity[key]` on a version upgrade but never removing the superseded entry's `TemplateId` from
`_byTemplateId`. Since `Classifier.Classify` resolves every candidate through `GetByTemplateId`, a
classification signal still keyed by a retired template could select the stale identity mapping
even after a newer version was current — `Match` and `GetByTemplateId` could disagree about which
version was current. Confirmed as a real, valid defect (both the included Python source probe and
the C# behavioral probe reproduced it against the reviewed commit) and fixed:

- `Register` now removes the superseded entry's `TemplateId` from `_byTemplateId` in the same call
  that installs the new version — both indexes move together atomically.
- Added `DocumentIntelligenceRegistryTests.AC03_QA01_upgrading_a_version_retires_the_previous_versions_template_from_the_reverse_lookup`
  (registry level) and `DocumentClassifierTests.AC03_QA01_a_match_against_a_superseded_template_is_never_classified_as_the_stale_identity`
  (end-to-end through `Classifier.Classify`), plus a new structural harness check.
- Re-ran both of Chioma's own probes against the fix: `stale-template-source-probe.py` → PASS;
  `dotnet run` on `stale-template-probe` → `current=entry-v2; retired-template=null` → PASS.

Reran everything after the fix: 23/23 developer tests pass, 34/34 structural checks pass. A fresh
`scripts/quality.sh --full` run shows every BIO-DOC-006-specific check clean (see
`quality-output.txt`, now captured at this commit); `.NET integration tests` failed again this run
on the same pre-existing `Biolog.IntegrationTests.ObservabilityTests` test documented as flaky
across BIO-DOC-002/003/004's evidence — a different assertion failure than any prior run
("Expected: Null, Actual: String" vs. the earlier `StringBuilder`/`chunkLength`
`ArgumentOutOfRangeException`), which further confirms nondeterminism rather than a regression;
nothing in this fix touches observability/logging. Only the pre-existing frontend/Playwright
`node_modules` gap otherwise remains.

## Acceptance round 2: FAIL, fixed and resubmitted

**Commit:** `3345e8962f53a778a71833589d9ec78a0b3ec045`

Chioma's second acceptance review (`evidence/BIO-DOC-006/acceptance/ACCEPTANCE.md`, BIO-DOC-006-QA-02,
High, AC03/Functional/Regression FAIL) confirmed QA-01 closed, then found a second gap in the same
`Register` method: nothing stopped two *different* current identities from registering the same
`TemplateId`. Both stayed in `_byIdentity`, but `_byTemplateId[templateId]` silently followed
whichever was registered last, so `Classifier.Classify` (which only resolves through
`GetByTemplateId`) always followed the last registration regardless of which identity a caller
thought was still current — `Match` and `GetByTemplateId` disagreed about which identity a shared
template named. Confirmed as a real, valid defect (both the included Python source probe and the
C# behavioral probe reproduced it against the reviewed commit) and fixed:

- `Register` now checks whether `entry.TemplateId` already belongs to a *different* current
  identity and, if so, evicts that identity from `_byIdentity` in the same call that installs the
  new owner — an explicit, atomic ownership-transfer rule (the alternative to outright rejection
  the finding text offered; chosen because it matches Chioma's own shipped probe's expected control
  flow exactly — a new version of the *same* identity reusing its own `TemplateId` is explicitly not
  treated as an eviction).
- Added `DocumentIntelligenceRegistryTests.AC03_QA02_registering_a_different_identity_with_an_already_owned_template_evicts_the_former_owner`,
  a same-identity-reuse regression proving that case is *not* an eviction, and
  `DocumentClassifierTests.AC03_QA02_a_template_reassigned_to_a_different_identity_only_classifies_as_the_new_owner`
  end-to-end through `Classifier.Classify`.
- Re-ran both of Chioma's probes against the fix: `duplicate-template-source-probe.py` → PASS;
  `dotnet run` on `duplicate-template-probe` → `passport=; national-id=national-id;
  reverse=national-id` → clean exit 0 (no crash, no FAIL message) — confirming the eviction design
  matches exactly what the shipped probe's control flow expected. Also re-ran the round-1
  stale-template probes to confirm no regression: both still PASS.

Reran everything after the fix: 26/26 developer tests pass, 36/36 structural checks pass. A fresh
`scripts/quality.sh --full` run shows every BIO-DOC-006-specific check clean (see
`quality-output.txt`, now captured at this commit); `.NET integration tests` failed again on the
same pre-existing flaky `ObservabilityTests` test, unrelated to this change. Only the pre-existing
frontend/Playwright `node_modules` gap otherwise remains.

## Scope notes (deliberately not built)

- No HTTP endpoint or `IBiologModule`/DI registration — consistent with every BIO-DOC-00x engine
  story before it; this story's dependency scope (BIO-DOC-002/003/004 only) does not include the
  eventual orchestration/session layer this classifier would be wired into.
- No BIO-DOC-005 (parser registry) integration — a parsed barcode payload could further corroborate
  a candidate, but BIO-DOC-005 is not a declared dependency of this story.
- No registry seeding/default entries shipped — like BIO-DOC-004's `OcrTemplateRegistry`, this
  registry ships empty; a caller registers the document identities relevant to what it processes.
