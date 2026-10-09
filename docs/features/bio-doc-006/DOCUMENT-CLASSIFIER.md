# BIO-DOC-006 — Automatic Document Type Identification

`api/features/bio-doc-006/Biolog.Features.DocumentClassifier` is a plain .NET 10 class library (no
ASP.NET Core dependency, no NuGet package references) that composes MRZ/barcode/OCR evidence
BIO-DOC-002/003/004 already produced into a ranked classification: country, document class,
series/version and side, resolving a versioned Document Intelligence Registry entry so the correct
verification template can be selected automatically.

## Model

| Type | Kind | Purpose |
| --- | --- | --- |
| `DocumentSide` | enum | `Front`/`Back`/`Unknown` - this feature's own small vocabulary (AC04). |
| `DocumentIntelligenceRegistryEntry` | record | One versioned catalog entry: (Country, DocumentClass, Series, Side) identity + the OCR `TemplateId` to read it, plus optional expected barcode symbologies (AC03). |
| `DocumentIntelligenceRegistry` | class | The versioned catalog: `Register`, `Match` (by identity), `GetByTemplateId` (reverse lookup). |
| `DocumentClassificationSignals` | record | Input: the MRZ/barcode/OCR evidence already produced for a capture. |
| `DocumentClassCandidate` | record (locked down) | One ranked candidate with confidence and matched-signal provenance. |
| `DocumentClassificationOutcome` | record (locked down) | The end-to-end result: ranked candidates, `Unknown`, detected/expected side, `SideMismatch`, `RequiresOperatorConfirmation`, `SelectedTemplateId`. |
| `Classifier` | static | `Classify(signals, registry)` - the scoring/ranking engine (AC01 through AC05). |

## Acceptance finding, fixed

Chioma's acceptance review (BIO-DOC-006-QA-01, High) found `DocumentIntelligenceRegistry.Register`
replacing `_byIdentity[key]` when a higher version was registered, but never removing the
superseded entry's `TemplateId` from `_byTemplateId`. `Classifier.Classify` resolves every
candidate through `GetByTemplateId`, so a classification signal still keyed by a *retired* template
(e.g. a caller mid-capture against an OCR template that has since been superseded) would still
resolve to the stale identity mapping the old template pointed at - `Match` correctly returned the
current version, but the reverse lookup a real classification actually uses did not agree with it.
Confirmed as a real, valid defect and fixed: `Register` now removes the superseded entry's
`TemplateId` from `_byTemplateId` in the same call that installs the new version, so both indexes
move together atomically - never a moment where `Match` and `GetByTemplateId` disagree about which
version is current. `DocumentIntelligenceRegistryTests.AC03_QA01_upgrading_a_version_retires_the_previous_versions_template_from_the_reverse_lookup`
and `DocumentClassifierTests.AC03_QA01_a_match_against_a_superseded_template_is_never_classified_as_the_stale_identity`
prove this both at the registry level and end-to-end through `Classifier.Classify`.

Chioma's second acceptance pass (BIO-DOC-006-QA-02, High) found the reverse index still had no
uniqueness guard of its own: nothing stopped two *different* current identities from registering
the same `TemplateId`. Both stayed in `_byIdentity` (different keys), but `_byTemplateId[templateId]`
silently pointed at whichever was registered last - `Match` still claimed both identities were
current while classification, which resolves only through `GetByTemplateId`, could only ever
produce a candidate for the last one, regardless of which template evidence actually matched.
Fixed with an explicit, atomic ownership-transfer rule: if a `Register` call's `TemplateId` already
belongs to a *different* current identity, that identity is evicted from `_byIdentity` in the same
call that installs the new owner - a template can identify at most one current document identity,
and `Match`/`GetByTemplateId` can never disagree about which one that is. (Registering a *new
version of the same identity* with an unchanged `TemplateId` is explicitly not an eviction - the
owner-key comparison only triggers when the template's existing owner is a genuinely different
identity.) `DocumentIntelligenceRegistryTests.AC03_QA02_registering_a_different_identity_with_an_already_owned_template_evicts_the_former_owner`
(plus a same-identity-reuse regression proving that case is *not* treated as an eviction) and
`DocumentClassifierTests.AC03_QA02_a_template_reassigned_to_a_different_identity_only_classifies_as_the_new_owner`
prove this at the registry level and end-to-end.

## Why this composes evidence rather than decoding anything itself

This story's own declared dependencies are BIO-DOC-002, BIO-DOC-003 and BIO-DOC-004 - deliberately
**not** BIO-DOC-001, even though `MrzReadOutcome`/`BarcodeDecodeResult`/`TemplateOcrResult` all
transitively expose BIO-DOC-001's `BoundingBox` type. Every sibling BIO-DOC-00x project's own
csproj comment already establishes the rule this story follows: a `ProjectReference` matches the
story's own DEPENDENCIES line exactly, nothing more. `DocumentSide` is therefore this feature's own
small local enum rather than a reuse of BIO-DOC-001's `DocumentVerify.Model.DocumentSide` - the
same "define a small local type rather than reach for an undeclared dependency" precedent
BIO-DOC-003/004 set for their own raster types.

Consistent with every BIO-DOC-00x engine before it, this story classifies from evidence the three
sibling engines already produced (`DocumentClassificationSignals`) rather than decoding a raster
itself - the same "compose already-produced evidence" shape BIO-DOC-001's
`MrzFieldRead.ToDocumentFieldEvidence` adapter used.

## Why an OCR template match is the primary signal, and MRZ/barcode only corroborate

AC01 asks the classifier to identify country, document class **and** series/version together.
ICAO Doc 9303 MRZ carries an issuing-state code and a coarse document-code letter, but no
series/version field at all - two different print runs of the same passport type read an identical
MRZ. A raw barcode payload is opaque bytes at this layer (interpreting it is BIO-DOC-005's job, and
BIO-DOC-005 is deliberately not a dependency of this story). Only a registered
`OcrDocumentTemplate` match can genuinely name a specific series, because the template itself is
what a human operator registered as "this is what series X's layout looks like".

So `Classifier.Classify` only ever produces a candidate from a template match
(`DocumentClassificationSignals.TemplateMatches`, keyed by `TemplateId` - the caller has already run
`TemplateOcrEngine.Read` against every template it wants tried, ordinarily every template the
registry has registered), and uses MRZ issuing-state and barcode-symbology evidence purely to
**corroborate** (raise confidence in) a template match that has already happened, never to invent a
candidate on its own. This is a narrower reading of "classifier" than the AC's wording might first
suggest, documented here explicitly for the same reason BIO-DOC-004's AC03 scope note was: the AC's
own wording (all three of country/class/series together) traces back directly to what each engine
can actually prove.

## AC02: unknown is reported, ranked candidates are still shown

`Unknown` is true whenever no candidate clears `Classifier.MinAcceptableConfidence` (0.70) -
including when zero candidates were produced at all (no template matches, or every matched
template is unregistered in the catalog). Crucially, `Candidates` is **not** cleared when
`Unknown` is true: AC01's ranked list is still reported (a caller may still want to show "closest
matches" to an operator), it is only `SelectedTemplateId` and the implicit "trust this" signal that
are withheld. This mirrors the same "report what was actually detected, never fabricate a pass"
discipline every prior BIO-DOC-00x `RequiresManualReview`/`ReviewReasons` flag already used.

## AC03: "versioned" is a monotonic-version guarantee, not full optimistic-concurrency CAS

`DocumentIntelligenceRegistry` is a platform-wide reference catalog populated by configuration, not
a mutable per-request aggregate multiple tenants write to concurrently the way
`DocumentSession`(BIO-DOC-001)/`ParserRecord`(BIO-DOC-005) are - so there is no concurrent-writer
race for ADR-0001 Decision 3's full compare-and-set to guard against. What AC03's "versioned"
wording still buys, and what `Register` enforces, is a monotonic-version guarantee: registering an
entry that does not strictly increase `VersionNumber` for its own (Country, DocumentClass, Series,
Side) key throws `StaleRegistryVersionException`, so a catalog update can never silently regress a
document identity to an older template mapping. This is a deliberately simpler discipline than the
CAS pattern, and is called out explicitly here to preempt the "why not full ADR-0001 Decision 3"
question a reviewer familiar with `ParserRecord.ConcurrencyVersion` might otherwise ask.

## AC04: side is read from the winning candidate's registry entry, never guessed independently

`DetectedSide` is **not** an independent heuristic over raw ink (e.g. "MRZ present implies data
page") - it is read directly from the winning candidate's own registered
`DocumentIntelligenceRegistryEntry.Side`, the side a human operator declared when registering that
template. `SideMismatch` is only ever raised when the caller supplies an `ExpectedSide` (the
document-capture workflow already knows which side it asked the user to photograph) and
classification is not `Unknown` - never a guess about what side "should" be expected.

## AC05: every path to distrust is an explicit, named reason

`RequiresOperatorConfirmation` is `true` exactly when `ConfirmationReasons` is non-empty, and every
reason is one of four explicit, independently-testable conditions - never a single opaque flag:

- `no_candidate_matched` / `confidence_below_threshold` - AC02's unknown outcome.
- `side_mismatch:detected=X,expected=Y` - AC04's wrong-side detection.
- `ambiguous_top_candidates` - the top two candidates scored within `Classifier.AmbiguityMargin`
  (0.05) of each other, both above the confidence threshold. Never silently picked between - the
  same discipline as BIO-DOC-002's `MrzFormatRegistry.ResolveByDocumentCode` and BIO-DOC-004's
  grid-hypothesis line detector, both of which refuse rather than guess on an ambiguous match. This
  is a deliberately different response to ambiguity than those two: they return `null`/refuse
  outright (because they must resolve to exactly one geometry to extract fields byte-precisely),
  where AC01 explicitly asks this classifier to still return ranked candidates even when uncertain,
  with confirmation required instead of a hard refusal - the AC wording itself is the boundary.
- `template_match_requires_review:{templateId}` - the winning template's own
  `TemplateOcrResult.RequiresManualReview` (a critical OCR field read below confidence) is reused
  directly rather than re-derived, so a document that is only classifiable via a shaky OCR read is
  never reported as a confident classification.

## Locked down against post-hoc tampering (the QA-03 lesson, again)

`DocumentClassCandidate` and `DocumentClassificationOutcome` carry the fields a downstream caller
actually trusts (`Confidence`, `Unknown`, `SideMismatch`, `RequiresOperatorConfirmation`). Every
property is `internal init` with an explicit `internal` constructor - the same reasoning as every
prior BIO-DOC-00x result type this epic: a public `init` would let a caller launder a low-confidence
or ambiguous outcome into one that claims confidence via `with`, without `Classifier.Classify`'s own
scoring ever running.

## Reproducing the tests

```sh
dotnet test tests/features/bio-doc-006/Biolog.Features.DocumentClassifier.Tests/Biolog.Features.DocumentClassifier.Tests.csproj
bash tests/foundation/BIO-DOC-006.test.sh
```

## Scope notes (deliberately not built)

- No HTTP endpoint or `IBiologModule`/DI registration - consistent with every BIO-DOC-00x engine
  story before it; this story's own dependency scope (BIO-DOC-002/003/004 only) does not include
  the eventual orchestration/session layer this classifier would be wired into.
- No BIO-DOC-005 (parser registry) integration - a raw barcode payload's parsed fields could in
  principle further corroborate a candidate, but BIO-DOC-005 is not a declared dependency of this
  story; a later integration story is the right place to wire that in.
- No registry seeding/default entries shipped - like BIO-DOC-004's `OcrTemplateRegistry`, this
  registry ships empty; a caller registers the document identities relevant to what it processes.
