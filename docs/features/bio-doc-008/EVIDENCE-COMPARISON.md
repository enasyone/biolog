# BIO-DOC-008 — Evidence Cross-Comparison Engine

`api/features/bio-doc-008/Biolog.Features.EvidenceComparison` is a plain .NET 10 class library (no
ASP.NET Core dependency, no NuGet package references) that compares field-level evidence across
sources and produces an explicit, versioned, audited comparison result. Three `ProjectReference`s -
BIO-DOC-002 (MrzEngine), BIO-DOC-005 (ParserRegistry), BIO-DOC-007 (NfcReader) - exactly this
story's declared dependencies.

## Model

| Type | Kind | Purpose |
| --- | --- | --- |
| `EvidenceSourceKind` | enum | `Visual`/`Mrz`/`Barcode`/`Nfc` - the four sources the user story names. |
| `SourceFieldValue` | record (plain input) | One source's raw + normalized value + confidence for a field. |
| `FieldMatchStatus` | enum | `Match`/`Mismatch`/`Unknown` - AC01's exact three states. |
| `FieldComparison` | record (locked down) | One field compared across whichever sources supplied a value. |
| `ComparisonEngine` | static | `Compare(fieldName, valuesBySource)` - derives `Status` (AC01). |
| `SourcePrecedencePolicy` | record | An explicit, caller-declared source order (AC04). |
| `PreferredValueResolver` | static | The ONLY way to resolve a single preferred value (AC04). |
| `NfcCorroboration` | record (locked down) | NFC's authenticity/availability signal - not a decoded field value. |
| `CrossComparisonResult` | record (locked down) | The full versioned, audited result (AC03/AC05). |
| `Audit.IEvidenceComparisonAuditSink`/`InMemoryEvidenceComparisonAuditSink` | interface/class | The durable append-only audit store `Compute` writes to before returning (AC05). |
| `Audit.EvidenceComparisonAuditEvent`/`EvidenceComparisonAuditEventBuilder` | record/static | ADR-0003 Decision 1's envelope, scoped to this feature's one action. |

## Why MRZ and barcode get real adapters, but visual/OCR and NFC don't

This story's own DEPENDENCIES line names BIO-DOC-002, BIO-DOC-005 and BIO-DOC-007 - deliberately
**not** BIO-DOC-001/003/004/006, matching the "reference exactly the declared dependency, nothing
more" discipline every sibling BIO-DOC-00x story's own csproj comment establishes. This has two
concrete consequences for how each of the user story's four named sources is represented:

- **MRZ** (`SourceFieldValue.FromMrz`) and **barcode** (`SourceFieldValue.FromParsedBarcodeField`)
  get real adapters from real decoded field types - `MrzFieldRead` (BIO-DOC-002) genuinely carries
  a raw value, normalized value and confidence; `ParserFieldResult` (BIO-DOC-005, a *parsed*
  barcode field, not BIO-DOC-003's raw decode) carries a raw value and a validity flag, honestly
  mapped to confidence 1.0/0.0 rather than inventing a normalization or confidence scale that
  source never actually computed.
- **Visual/OCR** has no adapter at all - BIO-DOC-004 is not a declared dependency - so a caller
  constructs a `SourceFieldValue` for the `Visual` source directly via the public
  `SourceFieldValue.Create` factory, the same "plain domain parameter, not a resolved dependency"
  pattern BIO-DOC-005's `ParserCaller` and BIO-DOC-007's MRZ seed material already used wherever an
  undeclared dependency would otherwise be needed.
- **NFC** gets a real ProjectReference (BIO-DOC-007), but deliberately does NOT contribute a
  decoded field value the way MRZ/barcode do - see `NfcCorroboration`'s own remarks for why, and
  the "NFC corroborates, it does not compare" section below.

## AC01: match/mismatch/unknown, never a trivial match

`ComparisonEngine.Compare` returns `Unknown` whenever fewer than two sources supplied a value for
a field - there is nothing to agree or disagree on, and this is never silently reported as a
match just because nothing contradicts it. With two or more sources, `Match` requires every
supplied `NormalizedValue` to be exactly (ordinal, case-sensitive) equal; any disagreement is
`Mismatch`. The equality policy is stated explicitly here rather than left implicit: normalization
is each source's own job, so this engine trusts normalized values are already in a canonical
comparable form and does not re-normalize them itself.

## AC02: normalization never destroys raw evidence

`SourceFieldValue.Create` requires both `RawValue` and `NormalizedValue` to be present and
non-empty together - there is no code path anywhere in this type or this engine that accepts only
a normalized value and discards the raw one. `SourceFieldValue.FromParsedBarcodeField` proves this
even for a field that FAILED validation: the raw value is still carried through, only the
confidence reflects the failure (see `SourceFieldValueTests.AC02_FromParsedBarcodeField_a_failed_validation_maps_to_zero_confidence_not_a_fabricated_one`).

## NFC corroborates, it does not compare (AC01/AC03)

BIO-DOC-007's `ChipDataGroupRead` exposes only a SHA-256 hash of each data group's raw bytes,
never the decoded content (Dg1/Dg2 are identity PII, ADR-0003 Decision 5) - by the time a caller
holds a `ChipReadOutcome`, the raw MRZ bytes DG1 would carry have already been discarded. Decoding
a DG1 hash back into a comparable MRZ string is not possible (a hash is one-way), and
reconstructing DG1's exact ICAO 9303 Part 10 TLV/ASN.1 byte encoding from a candidate MRZ string
to re-hash and compare is a document-forensics-grade encoding task out of this story's scope.

`NfcCorroboration` instead carries an authenticity/availability signal derived from a real
`ChipReadOutcome`: whether a chip read was attempted at all (`Available`), whether the secure
channel/read succeeded (`CommunicationSucceeded`), and whether passive authentication passed
(`PassiveAuthenticationPassed`, nullable - never coerced to true/false when it was never
attempted). `CrossComparisonResult.RequiresManualReview` treats an explicit passive-authentication
FAILURE as forcing review even when every field-level comparison agrees - a genuine authenticity
concern that consensus among the other sources cannot outweigh (BIO-DOC-007-AC04's own passive
authentication result feeding directly into BIO-DOC-008's review routing).

## AC03: conflicts route to review

`CrossComparisonResult.FieldsRequiringReview` names every field whose `FieldComparison.Status` is
`Mismatch`, explicit rather than a bare count. `RequiresManualReview` is true whenever
`FieldsRequiringReview` is non-empty OR `NfcCorroboration.PassiveAuthenticationPassed` is
explicitly `false`. Actually *raising* a review ticket/conflict object (e.g. BIO-DOC-001's
`DocumentConflict`) is out of this story's scope - BIO-DOC-001 is not a declared dependency here -
this story's contribution is the explicit, structured signal a caller uses to decide whether to
raise one through whatever mechanism it has.

## AC04: no source is silently preferred without documented policy

`PreferredValueResolver.ResolvePreferredValue` is the ONLY method anywhere in this engine that
picks a single value out of a multi-source `FieldComparison`, and it structurally REQUIRES an
explicit `SourcePrecedencePolicy` - there is no overload, default parameter, or other path that
resolves a preferred value without one. A caller that wants "MRZ over barcode over visual" must
construct that policy explicitly in its own code/configuration, which is itself the "documented
policy" AC04 asks for - the precedence lives in the caller, never silently inside this engine.
`PreferredValueResolverTests.AC04_no_method_picks_a_value_out_of_a_comparison_without_requiring_an_explicit_precedence_policy`
proves this reflectively: no public method anywhere in the assembly takes a `FieldComparison` and
returns a `SourceFieldValue` without also requiring a `SourcePrecedencePolicy` parameter.

## AC05: versioned/audited

**Fixed acceptance finding (BIO-DOC-008-QA-01, High):** the original design carried
`ComputedByActorId`/`ComputedAt` as unvalidated caller-supplied strings and wrote no durable audit
event at all - attribution-shaped fields are not evidence an audit occurred, and Chioma's review
correctly found a caller could forge any actor/time/version into a result with no audit trail
behind it. By the time this story was built, ~15 other BIOLOG features had already established a
real, consistent per-feature audit-sink pattern (`I{Feature}AuditSink`/`InMemory{Feature}AuditSink`/
`{Feature}AuditEvent`/`{Feature}AuditEventBuilder`, e.g. `Biolog.Features.TenantRegistry.Audit`) -
this story's own earlier design doc text ("no audit-sink infrastructure exists anywhere in this
codebase yet") was stale, written against an early-session snapshot of the repo before those
stories landed. Fixed by adopting the SAME established pattern:

- `Audit/IEvidenceComparisonAuditSink`/`InMemoryEvidenceComparisonAuditSink`/
  `EvidenceComparisonAuditEvent`/`EvidenceComparisonAuditEventBuilder` - own local copies of the
  identically-shaped envelope (ADR-0003 Decision 1 / `contracts/schemas/audit-event.schema.json`)
  and identifier/clock helpers (`EvidenceComparisonEventId`/`EvidenceComparisonClock`, minting only
  the already-frozen `evt_` prefix, ADR-0001 §1.2) every other audited feature already carries.
  `EvidenceComparisonAuditEvent.Detail` carries only counts/flags about the comparison (field
  count, mismatch count, review flag), never the compared field values themselves - the same
  "evidence references, not evidence copies" reasoning (ADR-0003 Decision 5) every prior
  BIO-DOC-00x evidence type this epic follows.
- `CrossComparisonResult.Compute` now also takes a `correlationId`, and validates every
  attribution field before computing anything: `versionNumber` must be at least 1,
  `computedByActorId` must be non-blank, `computedAt` must be a full RFC 3339 date-time,
  `correlationId` must be non-blank. Internally it builds a structured `AuditActor` (ADR-0003:
  "never a bare string") as `new AuditActor(ActorType.Unknown, computedByActorId)` for the audit
  event - the actor's TYPE is honestly recorded as unknown rather than guessed, since a bare id
  string alone cannot tell a user from a service account.
- **Fixed re-review finding (BIO-DOC-008-QA-02, High, Seyi round 2):** `DateTimeOffset.TryParse`
  alone is permissive enough to accept a bare date like `"2026-09-23"` (defaulting the missing
  time and offset components) - that parses successfully but is NOT an RFC 3339 date-time, and
  the acceptance/documentation both claim strict RFC 3339 validation. `computedAt` is now checked
  against `Rfc3339Pattern` (a full date-time SHAPE - date, literal `T`, time, and an explicit `Z`
  or numeric offset) before `TryParse` is ever consulted for the value's semantic validity (a real
  calendar date, a valid time-of-day) - a bare date, or a date-time missing its offset entirely,
  is rejected outright.
- Before returning, `Compute` durably writes the audit event and uses the SAME
  audit-before-commit / ambiguous-write resolution every hard-audited BIOLOG feature's own
  `AuditAsync` helper uses (ADR-0002 §5; `ITenantAuditSink`'s `ExistsAsync`, hardened in
  BIO-TEN-001-QA-01 round 4): a `WriteAsync` that throws is not, on its own, proof nothing was
  persisted, so `Compute` checks `ExistsAsync` before concluding the audit genuinely failed.
  A confirmed failure throws `EvidenceComparisonAuditUnavailableException` - `Compute` NEVER
  returns a "successful" comparison whose audit write is unconfirmed durable.
- `CrossComparisonResult.AuditEventId` lets a caller look up the exact durable event a result was
  audited under.
- `Compute` itself stays SYNCHRONOUS and blocks on the `Task`-returning sink calls
  (`GetAwaiter().GetResult()`) rather than becoming `async Task<CrossComparisonResult>`: this
  library has no HTTP/ASP.NET boundary anywhere (no `SynchronizationContext` to deadlock against),
  and `InMemoryEvidenceComparisonAuditSink` always completes synchronously today, so blocking here
  carries none of sync-over-async's usual thread-pool-starvation risk. This keeps `Compute`'s
  public shape a plain synchronous factory, consistent with every other locked-down result type's
  own `Compute`/`Apply`-style factory in this epic. A later story wiring a genuinely I/O-bound
  durable sink should revisit whether `Compute` needs to become properly async at that point - the
  sink interface itself is already `Task`-returning and does not need to change.

- **Fixed acceptance finding (BIO-DOC-008-QA-03, High, Chioma round 4):** the reasoning above - that
  a validated `versionNumber >= 1` was sufficient because this type is "a pure computation result,
  not a mutable aggregate" - was wrong. A bare range check validates SHAPE, not SEQUENCE: it does
  nothing to stop a caller reusing version 1 for two different comparisons of the same document
  session, skipping straight to version 999, or moving backwards. Chioma's required remediation:
  "Allocate the next comparison version from session-scoped trusted state, or require a previous
  version and atomically compare-and-create the next one." `Compute` now takes an `IVersionStore`
  and calls `IVersionStore.AllocateVersion(documentSessionId, versionNumber)` as the sole path to a
  legitimate version claim - it atomically checks the caller's claim against this session's own
  tracked sequence and commits only if it is EXACTLY the next expected value (current + 1, or 1 for
  a session's first comparison). This is the same monotonic, atomically-checked sequence discipline
  ADR-0001 Decision 3 establishes for `DocumentSession.Version`/`ParserRecord.ConcurrencyVersion`,
  and the same "reject a version that doesn't move a key strictly forward" rule BIO-DOC-006's
  `DocumentIntelligenceRegistry.Register`/`StaleRegistryVersionException` already applies within
  this epic - the "simpler than full CAS" carve-out this doc previously claimed does not hold once
  the requirement is genuinely controlled versioning, not just a validated integer. A rejected claim
  throws `StaleComparisonVersionException` and leaves the store's tracked state unchanged, so a
  caller can safely retry the SAME version number after fixing an unrelated validation failure (a
  blank actor id, say) without it counting as a "skip." `InMemoryVersionStore` is the in-process seam
  shipped today; a durable, cross-process store is a later story's job, mirroring every other
  audited BIOLOG feature's own in-memory-sink-first pattern. Regression tests cover: sequential
  versions for one session succeeding in order, reuse/skip/backward rejection, a rejected claim not
  burning the sequence, and two different sessions carrying fully independent version sequences.

- **Fixed acceptance finding (BIO-DOC-008-QA-04, High, Chioma round 5):** the QA-03 fix above still
  had a real gap - `AllocateVersion` committed the version claim to the tracked sequence
  IMMEDIATELY, before the mandatory audit write that follows it. If that audit write then failed,
  `Compute` returned no comparison, but the version store's sequence had already advanced: a retry
  of the SAME legitimate version threw `StaleComparisonVersionException`, and skipping to the next
  version instead left a permanent hole for a result that never existed - an audit outage
  irrecoverably consumed trusted sequence state, with no release, rollback, or shared transaction.
  Chioma's required remediation: "Use an atomic reserve/commit/release protocol or a shared
  transaction so a failed audit does not consume the comparison version." `IVersionStore` is now a
  genuine two-phase protocol: `AllocateVersion` only RESERVES the next slot (and, while a
  reservation is outstanding for a session, blocks any other version being reserved for that SAME
  session - this is what makes the protocol atomic end to end, not just at the reserve step);
  `CommitVersion` finalizes a reservation, advancing the tracked sequence, ONLY after `Compute`'s
  audit write is confirmed durable; `ReleaseVersion` discards a reservation without consuming it
  when anything after the reservation fails (any exception path, wrapped in `try`/`catch` around
  the audit build/write and `CommitVersion` call), so the SAME version number is legitimately
  reissuable to a retry once the underlying problem is resolved. This mirrors the same
  "reserve, only commit once durable, release/rollback on failure" discipline `ITenantAuditSink`'s
  own audit-before-commit ambiguous-write handling applies at its own layer, now applied to
  version-sequence allocation itself. Regression tests cover: a failed audit write followed by a
  successful retry of the same version number, and an outstanding reservation blocking a second
  reservation attempt for the same session.

## Locked down against post-hoc tampering, applied proactively (the BIO-DOC-007-QA-02/QA-03 lesson)

`FieldComparison`, `CrossComparisonResult` and `NfcCorroboration` are locked down (`internal
init`, no public constructor) the same way every prior BIO-DOC-00x result type this epic is.
Learning directly from BIO-DOC-007's own two-round remediation (QA-02: `byte[]` getters exposed a
mutable shared array; QA-03: the SAME class of bug found one layer up on `IReadOnlyList<T>`/
`List<T>` properties), every collection-typed property here was built defensively from the start,
not added reactively after a reviewer found the gap:

- `FieldComparison.ValuesBySource` is wrapped in `ReadOnlyDictionary<TKey,TValue>` over a
  freshly-copied `Dictionary<TKey,TValue>` - genuinely distinct from `Dictionary<TKey,TValue>`,
  not downcastable back to one.
- `CrossComparisonResult.FieldComparisons`/`FieldsRequiringReview` and
  `SourcePrecedencePolicy.OrderedPreference` are all wrapped in `List<T>.AsReadOnly()` -
  `ReadOnlyCollection<T>`, likewise not downcastable back to `List<T>`.

`EvidenceComparisonResultTypesAreLockedDownTests.FieldComparison_and_CrossComparisonResult_collections_cannot_be_downcast_back_to_a_mutable_list_or_dictionary`
proves the downcast itself fails, not merely that values happen to match afterward.

## Reproducing the tests

```sh
dotnet test tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/Biolog.Features.EvidenceComparison.Tests.csproj
bash tests/foundation/BIO-DOC-008.test.sh
```

## Scope notes (deliberately not built)

- No HTTP endpoint or `IBiologModule`/DI registration - consistent with every BIO-DOC-00x engine
  story before it.
- No actual `DocumentConflict`/review-ticket creation - BIO-DOC-001 is not a declared dependency;
  this story exposes the explicit routing signal, a caller raises the conflict through its own
  mechanism.
- No real durable audit sink - `InMemoryEvidenceComparisonAuditSink` is the same in-process seam
  every other audited BIOLOG feature ships with; a real durable store is a later, shared story's
  job (the same seam `InMemoryTenantAuditSink` etc. leave open).
- No raw NFC data-group content comparison - see "NFC corroborates, it does not compare" above.
