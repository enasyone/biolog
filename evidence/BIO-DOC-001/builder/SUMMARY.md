# BIO-DOC-001 — Builder evidence

**Story:** Implement Document Verify session and evidence foundation
**Owner:** Ifeanyi — Document Intelligence Engineer
**Worktree:** `biolog-worktrees/BIO-DOC-001`, branch `feature/BIO-DOC-001`
**Dependencies:** BIO-FND-008, BIO-ARC-003, BIO-ARC-004 (all `DONE`)
**Commit:** `3054aec9df1e00a4fcc7489f6b3ba4036575e473`

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — DocumentSession supports person-linked or pre-person onboarding | `Model/DocumentSession.cs`: `Open(id, tenantId, personId: null, ...)` starts pre-person; `LinkPerson(...)` transitions it; relinking to a *different* person is refused (`already_linked_to_different_person`), never silently overwritten. Carries ADR-0001 Decision 3 optimistic-concurrency (`Version`, `ConcurrencyException`). | `dotnet-unit-test-output.txt` (`DocumentSessionTests`, `InMemoryDocumentSessionStoreTests.AC01_*`), `structural-test-output.txt` |
| AC02 — source images are immutable evidence objects with hashes and page/side metadata | `Model/DocumentEvidence.cs`: `sealed record`, `init`-only, no mutator, no `version` field (ADR-0001 §3.3). Carries `Sha256Hash` (validated 64 lowercase hex), `PageNumber`, `DocumentSide`, `EvidenceSource`, and separate device-claimed (`CapturedAt`) vs server-authoritative (`ReceivedAt`) timestamps (ADR-0001 §2.4). | `dotnet-unit-test-output.txt` (`DocumentEvidenceTests`), `structural-test-output.txt` |
| AC03 — evidence records preserve raw, normalized, confidence, source and bounding-box metadata | `Model/DocumentFieldEvidence.cs` + `Model/BoundingBox.cs`: preserves `RawValue`/`NormalizedValue` separately, `Confidence` validated to `[0.0, 1.0]`, `FieldEvidenceSource`, and an optional normalized `BoundingBox` (null for a source with no image location, e.g. NFC). | `dotnet-unit-test-output.txt` (`DocumentFieldEvidenceTests`, `BoundingBoxTests`), `structural-test-output.txt` |
| AC04 — document results never silently overwrite PERSON fields | `Model/PersonFieldChangeProposal.cs`: `Propose` always returns an unapplied instance; `Accept(acceptedBy, acceptedAt)` requires a non-empty actor, refuses while `RequiresManualReview` is true, and returns a **new** record — the original is untouched. No type in this feature writes to a Person aggregate; none exists in this codebase yet. | `dotnet-unit-test-output.txt` (`PersonFieldChangeProposalTests`, `InMemoryDocumentSessionStoreTests.AC04_*`), `structural-test-output.txt` |
| AC05 — manual review state and conflict evidence are supported | `Model/DocumentConflict.cs` + `Model/ManualReviewState.cs`: a conflict requires ≥2 disagreeing `DocumentFieldEvidence` sequence numbers, moves `PendingReview → InReview → Resolved`. `InMemoryDocumentSessionStore` moves the owning `DocumentSession` into `ManualReviewPending` when raised and returns it once every open conflict on the session is resolved. | `dotnet-unit-test-output.txt` (`DocumentConflictTests`, `InMemoryDocumentSessionStoreTests.AC05_*`), `structural-test-output.txt` |

## Developer tests (56 pass)

- `tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/DocumentSessionTests.cs` — AC01 + ADR-0001 Decision 3.
- `tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/DocumentEvidenceTests.cs` — AC02.
- `tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/DocumentFieldEvidenceTests.cs` (incl. `BoundingBoxTests`) — AC03.
- `tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/PersonFieldChangeProposalTests.cs` — AC04.
- `tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/DocumentConflictTests.cs` — AC05.
- `tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/InMemoryDocumentSessionStoreTests.cs` — end-to-end across AC01–AC05, plus the ADR-0002 §4.3 cross-tenant negative test every story touching tenant data ships (a session id belonging to a different tenant reports the identical `DocumentSessionNotFoundException` as a genuinely unknown one).
- `tests/foundation/BIO-DOC-001.test.sh` (43 checks) — offline-safe structural wiring checks, no `dotnet` toolchain required, matching the BIO-FND-008/010 convention.

Reproduce with:
```sh
dotnet test tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/Biolog.Features.DocumentVerify.Tests.csproj
bash tests/foundation/BIO-DOC-001.test.sh
bash scripts/quality.sh
```
(the exact `test_command` in `metadata.json`, chained with `&&`, exits 0 — see `quality-output.txt`.)

`scripts/quality.sh --full` was also run in full (`quality-full-output.txt`): `.NET unit tests`,
`.NET integration tests`, `.NET document-verify unit tests` and `.NET vulnerability audit` all
`PASS` (also captured standalone in `dotnet-unit-test-output.txt`,
`dotnet-integration-test-output.txt`, `dotnet-vulnerability-audit-output.txt`). `Flutter tests`
also `PASS`. The overall command exits non-zero only because of `frontend unit tests` /
`Playwright tests` failing with `vitest: command not found` / `playwright: command not found` —
`tests/frontend`'s `node_modules` has never been installed on this machine (identical,
pre-existing, unrelated gap already documented in BIO-FND-002/BIO-FND-008's own evidence); this
story touches no frontend/web code.

## Design decisions (full reasoning in `docs/features/bio-doc-001/DOCUMENT-VERIFY-FOUNDATION.md`)

- **No new canonical identifier type was invented.** ADR-0001 registers exactly six prefixes and
  forbids a feature story inventing a seventh locally. Evidence identity is
  `(document_session_id, sequence_number)` plus the evidence's own content hash — the same shape
  ADR-0001 §1.6 already uses for a business sequence, and a stronger anchor than an opaque id.
- **Identifiers/instants are plain strings, not validated against ADR-0001's canonical-id/UTC-
  instant contracts.** No .NET binding of `contracts/js/identifiers.mjs`/`clock.mjs` exists
  anywhere in this codebase yet (confirmed by inspection); `api/Biolog.Api/Tenancy/TenantContext.cs`
  makes the identical documented choice for the identical reason (BIO-FND-008).
- **No HTTP endpoints yet.** Every tenant-plane write must be constructed from a resolved tenant
  context (ADR-0002 §1.5); `TenantContextMiddleware` unconditionally clears the context
  (BIO-FND-008-AC04) — no real resolver exists yet. `Modules/DocumentModule.cs` registers the
  store into DI and maps no routes, mirroring `HealthModule`'s/`TenantModule`'s own documented
  "seam, not yet wired" choices.
- **No migration proposal (ADR-0005) was submitted.** No persistence layer exists anywhere in
  `api/Biolog.Api` — no `DbContext`, no connection string, no ORM reference — so there is no real
  schema to change yet. `IDocumentSessionStore` is the seam a future persistence story implements
  against a real tenant database and proposes its schema for at that time.
- **`evidence/BIO-DOC-001/` rather than `evidence/features/bio-doc-001/`.** The dispatched builder
  packet (`automation/inbox/Ifeanyi/NEXT.md`) explicitly instructs "Produce builder evidence under
  `evidence/BIO-DOC-001/builder/`," which outranks `governance/feature-path-ownership.json`'s
  per-feature template per the source hierarchy — the identical, already-ratified precedent
  recorded in `evidence/BIO-FND-008/builder/SUMMARY.md`'s "Ownership-template gap" section. `api/`,
  `tests/` and `docs/` for this story still follow the registry's `.../bio-doc-001/**` template,
  since the packet says nothing about those paths.

## Traceability

`governance/stories/BIO-DOC-001.json` declares only this builder-stage evidence entry —
`review-complete`/acceptance are not this builder's authority to attest, so Seyi (adversarial)
and Chioma (acceptance) each append their own evidence declaration to the same file's `evidence`
array as they complete their stage, matching `required_acceptance_stages`. This is the same
progressive-declaration shape already visible in `governance/stories/BIO-FND-010.json`'s own
builder-time evidence.

## Notes for reviewers (Seyi)

- No HTTP surface, no persistence, no migration — see "Design decisions" above for why each is
  out of this story's scope, not an oversight. `BIO-DOC-002`–`BIO-DOC-011` (MRZ, barcode, OCR,
  NFC, cross-comparison, authenticity, tenant templates) are separate, later stories that consume
  this model.
- `contracts/` (Kemi's, frozen at W01 exit) was read but not modified — no shared-contract or
  migration-core change, no ADR-0001 identifier-registry amendment sought.
- Cross-tenant access is proven, not merely asserted: `InMemoryDocumentSessionStoreTests`
  includes both "different tenant" and "genuinely unknown id" cases reporting the identical
  `DocumentSessionNotFoundException` (ADR-0002 §4.3).

## CHANGES_REQUIRED round 1 (Seyi's independent review): QA-01 High, QA-02 Medium

`evidence/BIO-DOC-001/codex/REVIEW.md`. New commit: `e3e376de7449e9ff96a01ffcf7552b909c7f3233`.

**QA-01 (High, AC05) — conflict evidence did not prove a conflict.**
`InMemoryDocumentSessionStore.RaiseConflict` checked only that the cited
`DocumentFieldEvidence` sequence numbers existed on the session — not that each cited record's
`FieldName` actually matched the conflict's named field, or that the cited values actually
disagreed. A caller could open a `date_of_birth` conflict citing `given_names` and
`document_number` evidence with equal values, moving the session into manual review and gating
a person-field proposal on fabricated provenance. Fixed: every cited record must have
`FieldName == fieldName`, refused as `field_evidence_field_name_mismatch` otherwise; the cited
set must contain at least two distinct `NormalizedValue`s, refused as
`conflicting_field_evidence_does_not_disagree` otherwise.

**QA-02 (Medium, AC03) — bounding boxes accepted non-finite coordinates.**
`BoundingBox.Create`'s range (`< 0`, `>= 1`, `<= 0`, `> 1`) and sum (`x + width > 1`) comparisons
all evaluate `false` for `double.NaN`, so a fully-`NaN` box passed every check silently.
`double.PositiveInfinity`/`NegativeInfinity` were already caught by the existing range checks;
only `NaN` slipped through. Fixed: `RequireFinite` rejects any non-finite x/y/width/height
explicitly, before any range or sum check runs.

**Regression coverage added** (both new xUnit tests and reran Seyi's own probe):
- `InMemoryDocumentSessionStoreTests.AC05_raising_a_conflict_with_evidence_from_an_unrelated_field_is_refused`
- `InMemoryDocumentSessionStoreTests.AC05_raising_a_conflict_with_equal_values_is_refused_there_is_no_real_disagreement`
- `BoundingBoxTests.rejects_a_non_finite_coordinate` (NaN on each of x/y/width/height, plus
  ±Infinity)
- `bash evidence/BIO-DOC-001/codex/independent-review.sh` rerun directly against the fix: both of
  Seyi's own probes now `PASS` (`round2-independent-review-rerun-output.txt`).

64/64 developer tests and 43/43 structural checks pass (`round2-dotnet-unit-test-output.txt`,
`round2-structural-test-output.txt`); `scripts/quality.sh` is clean (`round2-quality-output.txt`,
`QUALITY: PASS`).

## ACCEPTANCE-FAIL round 1 (Chioma's acceptance audit): QA-03 High

`evidence/BIO-DOC-001/acceptance/QA-03.md`, reviewed at commit `e3e376d`. New commit:
`a2a03b3a49712abc0d3c3c306ae0cb37cd7bde2f`.

**QA-03 (High, AC02/AC03) — evidence factory validation was optional.**
`DocumentEvidence`, `DocumentFieldEvidence` and `BoundingBox` used `public required X { get; init; }`
auto-properties with no private constructor. `Create`'s validation only ran if a caller chose to
call it - a plain object initializer (`new DocumentEvidence { TenantId = "", Sha256Hash =
"not-a-hash", ... }`) reached every property directly, producing exactly the kind of invalid,
"unrepresentable" record the type's own documentation claimed could not exist.

**Fix:** every property's `init` accessor is now `private` - the ONLY code that can ever run one
is a member of the type itself, in practice only `Create`. `required` had to be dropped: C#
refuses a `required` member whose accessor is less visible than its (public) containing type
(CS9032). This costs nothing in practice - `Create`'s own object initializer already names every
property explicitly.

**A second, deeper gap found while verifying the fix, before trusting it:** `private init`
properties alone do NOT stop external construction. A `record` with a body (no positional
parameter list) that declares no constructor of its own still gets an implicit **public**
parameterless constructor from the compiler, regardless of how private its properties are - the
build turned clean (0 warnings) after the `private init` change, which briefly looked like a
complete fix. A one-line throwaway probe (`new BoundingBox()`) confirmed it still compiled and
returned an unvalidated `(0, 0, 0, 0)` box - worse than the DocumentEvidence case, since it isn't
even caught by the nullable-reference analyzer the way an uninitialized `string` property is.
Closed by adding an explicit `private` parameterless constructor to all four locked-down types,
which suppresses the compiler-synthesized public one.

**Applied proactively to `PersonFieldChangeProposal` too**, even though QA-03 named only the other
three types: the identical bypass there (`new PersonFieldChangeProposal { AppliedAt = "...",
AppliedBy = "..." }`) would have let a caller fabricate an already-applied proposal with no
`Accept()` call at all - a direct violation of AC04's own guarantee ("document results never
silently overwrite PERSON fields"), a more serious consequence than the other three findings, not
merely the same defect shape recurring.

**Also closed, found while auditing `Create()`'s own validation for gaps of the identical shape**
(not part of QA-03, but the same class of "the factory claims to validate but doesn't"): `Side`/
`Source`/`Classification` (`DocumentEvidence`) and `Source`/`Classification`
(`DocumentFieldEvidence`) accepted any integer cast to the enum type - e.g. `(DocumentSide)999` -
because `Create` never checked `Enum.IsDefined`. Added a shared `RequireDefined<TEnum>` helper,
wired into both factories.

**Regression coverage:**
- The two `with`-expression immutability tests could no longer compile from outside the type
  (that was the point) - rewritten to demonstrate a correction via two independent `Create()`
  calls instead, which is what the type's own documentation already said a correction looks like.
- `ConstructionIsMandatoryTests.cs`: reflection-based tests asserting no public constructor and no
  public/internal property setter exists on any of the four locked-down types. This is the
  standing, executable regression coverage - a compile error alone only protects the exact line
  that once failed to compile, not future code.
- Five new tests rejecting an undefined enum value, across `DocumentEvidence` and
  `DocumentFieldEvidence`.
- Reran Chioma's own `evidence/BIO-DOC-001/acceptance/InvalidEvidenceProbe` directly against the
  fix: it now fails to **compile** (`CS0200`/`CS1729`), which is the strongest possible proof the
  bypass is closed (`round3-chioma-probe-rerun-output.txt`).
- Reran Seyi's own `evidence/BIO-DOC-001/codex/independent-review.sh` directly: both QA-01/QA-02
  probes still `PASS` - this fix did not regress the prior remediation
  (`round3-seyi-probe-rerun-output.txt`).

77/77 developer tests and 43/43 structural checks pass (`round3-dotnet-unit-test-output.txt`,
`round3-structural-test-output.txt`); `scripts/quality.sh` is clean (`round3-quality-output.txt`,
`QUALITY: PASS`).
