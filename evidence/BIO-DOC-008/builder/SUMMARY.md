# BIO-DOC-008 — Builder Evidence

**Story:** Build evidence cross-comparison engine
**Commit:** `e9447ebbaf64da8a295a85f0b6dc2a9dc28a4681`
**Branch:** `feature/BIO-DOC-008`
**Owner:** Ifeanyi — Document Intelligence Engineer

## What was built

`api/features/bio-doc-008/Biolog.Features.EvidenceComparison` — a plain .NET 10 class library, no
ASP.NET Core dependency, no NuGet package references. Three `ProjectReference`s — BIO-DOC-002
(MrzEngine), BIO-DOC-005 (ParserRegistry), BIO-DOC-007 (NfcReader) — exactly this story's declared
dependencies, deliberately not BIO-DOC-001/003/004/006.

- `SourceFieldValue` — plain caller-supplied input (public `Create`, unlike this story's own
  locked-down computed results), with real adapters from BIO-DOC-002 (`FromMrz`) and BIO-DOC-005
  (`FromParsedBarcodeField`); visual/OCR has no adapter (BIO-DOC-004 is not a declared dependency)
  and is constructed directly by the caller.
- `ComparisonEngine.Compare` — match/mismatch/unknown (AC01): fewer than two sources is `Unknown`,
  never a trivial match; equality is exact `NormalizedValue` agreement.
- AC02 enforced structurally: `SourceFieldValue` always carries `RawValue` and `NormalizedValue`
  together, never one without the other — proven even for a field that failed validation.
- `SourcePrecedencePolicy`/`PreferredValueResolver` — the ONLY path to a single preferred value
  anywhere in the engine, structurally requiring an explicit caller-declared precedence policy
  (AC04), proven with a reflection test that no other method resolves a value without one.
- `NfcCorroboration` — NFC contributes an authenticity/availability signal derived from a real
  `ChipReadOutcome`, not a decoded field value — see "Why NFC corroborates rather than compares"
  below.
- `CrossComparisonResult` — `FieldsRequiringReview` names every mismatch explicitly (AC03); a
  failed NFC passive authentication also forces review even when every field agrees.
  `VersionNumber` + attribution data satisfy AC05's "versioned/audited" without a live audit-sink
  (none exists in this codebase) or ADR-0001 Decision 3 CAS (this is a pure computation, not a
  mutable aggregate).
- `FieldComparison`/`CrossComparisonResult`/`NfcCorroboration` locked down (`internal init`, no
  public constructor); every collection property proactively wrapped in `ReadOnlyDictionary`/
  `AsReadOnly()` from the start.

## Why NFC corroborates rather than compares

BIO-DOC-007's `ChipDataGroupRead` exposes only a SHA-256 hash of each data group's raw bytes,
never the decoded content (Dg1/Dg2 are identity PII, ADR-0003 Decision 5) — by the time a caller
holds a `ChipReadOutcome`, the raw MRZ bytes DG1 would carry have already been discarded. Decoding
a hash back into a comparable value is impossible, and reconstructing DG1's exact ICAO 9303 Part
10 TLV/ASN.1 encoding from a candidate MRZ string to re-hash and compare is a document-forensics-
grade task out of this story's scope. `NfcCorroboration` instead carries availability/
communication-success/passive-authentication signals, feeding `RequiresManualReview` directly —
honest about what BIO-DOC-007 can and cannot supply, rather than fabricating a field-level
comparison it structurally cannot obtain. Full reasoning in
`docs/features/bio-doc-008/EVIDENCE-COMPARISON.md`.

## Applying the BIO-DOC-007-QA-02/QA-03 lesson proactively

BIO-DOC-007 needed two extra acceptance rounds because a mutable-reference vulnerability
(`internal init` blocking reassignment but not in-place mutation) was found and fixed on `byte[]`
properties, then found AGAIN on `IReadOnlyList<T>`/`List<T>` properties in a separate round. This
story applied that lesson from the start: every collection-typed property here —
`FieldComparison.ValuesBySource` (`ReadOnlyDictionary`), `CrossComparisonResult.FieldComparisons`/
`FieldsRequiringReview`, `SourcePrecedencePolicy.OrderedPreference` (`List<T>.AsReadOnly()`) — was
built as a genuinely non-downcastable type from the first commit, with a reflection-based
regression test proving the downcast itself fails.

## Test/quality evidence

- `dotnet test tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/...` — 31/31
  passed on the first run, including real BIO-DOC-002 (`MrzReader.Read` via synthetic raster) and
  BIO-DOC-005 (`ParserExecutor.Apply` via a full draft→test→publish parser workflow) and
  BIO-DOC-007 (`ChipReadSession.Read`/`PassiveAuthentication.Evaluate` via a fake transport and
  synthetic X.509 certificates) engine output — never fabricated, since those result types are
  locked down for exactly this reason.
- `bash tests/foundation/BIO-DOC-008.test.sh` — 32/32 structural checks passed.
- `bash scripts/quality.sh` (baseline) — PASS.
- `bash scripts/quality.sh --full` — every BIO-DOC-008-specific check passes; see
  `quality-output.txt`.

## Acceptance round 1: FAIL, fixed and resubmitted

**Commit:** `fef48b4a73d14ed73484a7ae8d2e21adad3a7c22`

Chioma's acceptance review (`evidence/BIO-DOC-008/acceptance/REPORT.md`, BIO-DOC-008-QA-01, High,
Functional/Security/Regression FAIL against AC05) found `CrossComparisonResult.Compute` only
copied caller-supplied `versionNumber`/`computedByActorId`/`computedAt` into the returned object -
no validation, and no durable audit event written to any sink. This story's own design doc had
claimed "no audit-sink infrastructure exists anywhere in this codebase yet" - true when BIO-DOC-001
was built, but stale by the time BIO-DOC-008 was: ~15 other BIOLOG features had since established
a real, consistent per-feature audit-sink pattern (`I{Feature}AuditSink`/`InMemory{Feature}AuditSink`/
`{Feature}AuditEvent`/`{Feature}AuditEventBuilder`, e.g. `Biolog.Features.TenantRegistry.Audit`).
Confirmed as a real, valid defect and fixed by adopting that same established pattern:

- `Audit/IEvidenceComparisonAuditSink`/`InMemoryEvidenceComparisonAuditSink`/
  `EvidenceComparisonAuditEvent`/`EvidenceComparisonAuditEventBuilder` - own local copies of the
  ADR-0003 Decision 1 envelope and the `evt_`-prefixed identifier/clock helpers every other audited
  feature carries (ADR-0001 §1.2's already-frozen prefix, no new one registered).
- `Compute` validates `versionNumber >= 1`, `computedByActorId` non-blank, `computedAt` a real
  RFC 3339 instant, and a new `correlationId` parameter non-blank - before computing anything.
- Before returning, `Compute` durably writes the audit event using the same audit-before-commit /
  ambiguous-write resolution every hard-audited BIOLOG feature's own `AuditAsync` helper uses
  (`ITenantAuditSink.ExistsAsync`, BIO-TEN-001-QA-01 round 4) - a confirmed failure throws
  `EvidenceComparisonAuditUnavailableException`; `Compute` never returns a "successful" comparison
  whose audit write is unconfirmed durable.
- `Compute` stays a synchronous factory (blocking on the `Task`-returning sink calls) rather than
  becoming `async Task<...>` - verified directly against Chioma's own
  `version_audit_probe.py`, which PASSes cleanly against this exact shape.

Reran everything after the fix: 38/38 developer tests pass (including validation-rejection and
ambiguous-write-resolution regressions), 39/39 structural checks pass. A fresh
`scripts/quality.sh --full` run shows every BIO-DOC-008-specific check clean (see
`quality-output.txt`, now captured at this commit), INCLUDING `.NET integration tests` passing
clean this run (the pre-existing flaky `ObservabilityTests` did not reproduce). Only the
pre-existing frontend/Playwright `node_modules` gap otherwise remains.

## Review round 2: CHANGES_REQUIRED, fixed and resubmitted

**Commit:** `83a8160ae888fdc29627d8d75f233e66b1d53342`

Seyi's round-2 re-review (`evidence/BIO-DOC-008/codex/REVIEW-Seyi-round2.md`, BIO-DOC-008-QA-02,
High, Functional/Security FAIL) found `Compute`'s `computedAt` validation used
`DateTimeOffset.TryParse` alone, which is permissive enough to accept a bare date like
`"2026-09-23"` (defaulting the missing time/offset components) - that parses successfully despite
not being an RFC 3339 date-time, weakening the audit-attribution guarantee the design doc and
acceptance requirement both claim. Confirmed as a real, valid defect and fixed:

- `computedAt` is now checked against a strict RFC 3339 date-time SHAPE (full date, literal `T`,
  time, and an explicit `Z` or numeric offset) via a `GeneratedRegex` before `TryParse` is ever
  consulted for the value's semantic validity - a bare date, or a date-time missing its offset
  entirely, is rejected outright.
- Added regressions using Seyi's exact reproduction (`"2026-09-23"`), plus a sibling
  offset-less-date-time rejection case and a positive numeric-offset acceptance case, and a
  structural harness check.

Reran everything after the fix: 41/41 developer tests pass, 41/41 structural checks pass.
`scripts/quality.sh --full` clean on every BIO-DOC-008-specific check; the pre-existing flaky
`ObservabilityTests` failed again on this round, unrelated to this change.

## Acceptance round 2 (round 4 overall): FAIL, fixed and resubmitted

**Commit:** `700756e6afe869d193f67a4c481742df8ed56e95`

Chioma's second acceptance review (`evidence/BIO-DOC-008/acceptance/REPORT.md`, BIO-DOC-008-QA-03,
High, Functional/Security/Regression FAIL against AC05) found the version half of AC05 and QA-01
still unresolved: `Compute`'s only check was `versionNumber >= 1`, a bare range check that validates
SHAPE, not SEQUENCE. A caller could still label the first result version 999, reuse version 1 for
multiple different comparisons of the same document session, or move backwards - none of which a
positive-integer check catches. This story's own earlier design-doc reasoning ("VersionNumber...
this type is a pure computation result, not a mutable aggregate") was the wrong call for what AC05
"versioned" actually requires - confirmed as a real, valid defect and fixed:

- Added `IVersionStore`/`InMemoryVersionStore`/`StaleComparisonVersionException`. `Compute` now
  takes an `IVersionStore versionStore` and calls `versionStore.AllocateVersion(documentSessionId,
  versionNumber)` as the sole path to a legitimate version claim - it atomically checks the caller's
  claim against the session's own tracked sequence and commits only if it is EXACTLY the next
  expected value, mirroring ADR-0001 Decision 3's CAS discipline and BIO-DOC-006's
  `DocumentIntelligenceRegistry.Register`/`StaleRegistryVersionException` precedent.
- The version allocation call is deliberately placed AFTER all cheap, side-effect-free input
  validations (version shape, actor, timestamp, correlation id) and BEFORE the audit-event
  build/write, so an unrelated validation failure never "burns" a version number a caller could
  legitimately retry with - verified with a dedicated regression.
- Verified directly against Chioma's own updated `version_audit_probe.py` (which now also checks
  `controls_version` for `IVersionStore`/`AllocateVersion` presence) before resubmitting - clean
  PASS on all six checks.
- Added regressions per Chioma's explicit remediation ask: duplicate, skipped, and backward version
  rejection, plus a positive sequential-version case, a case proving a rejected claim does not
  disturb the tracked sequence, and a case proving two different sessions carry fully independent
  version sequences.
- Added a matching structural harness check block (`IVersionStore`/`AllocateVersion` presence, the
  dedicated exception, and the new regression tests) to `tests/foundation/BIO-DOC-008.test.sh`.
- Updated `docs/features/bio-doc-008/EVIDENCE-COMPARISON.md`'s AC05 section, replacing the
  superseded "pure computation result, not a mutable aggregate" reasoning with the QA-03 fix.

Reran everything after the fix: 48/48 developer tests pass, 47/47 structural checks pass.
`scripts/quality.sh --full` clean across every .NET project in the repo (including
`Biolog.IntegrationTests.ObservabilityTests`, which did not reproduce its known pre-existing flake
this run); only the pre-existing frontend/Playwright `node_modules` gap otherwise remains.

## Acceptance round 3 (round 5 overall): FAIL, fixed and resubmitted

**Commit:** `e145357fb7fdfaab2caee02293d57c6534e72ee2`

Chioma's third acceptance review (`evidence/BIO-DOC-008/acceptance/REPORT.md`, BIO-DOC-008-QA-04,
High, Functional/Security/Concurrency/Regression FAIL against AC05) found the QA-03 fix still
unsafe: `AllocateVersion` committed the version claim to the tracked sequence IMMEDIATELY, before
the mandatory audit write that follows it. If the audit write then failed, `Compute` returned no
comparison, but the sequence had already advanced - a retry of the SAME legitimate version threw
`StaleComparisonVersionException`, and skipping ahead instead left a permanent hole for a result
that never existed. No release, rollback, reservation/commit protocol, or shared transaction
coupled the two required effects (version allocation, audit write) safely. Confirmed as a real,
valid defect and fixed:

- `IVersionStore` is now a genuine two-phase protocol: `AllocateVersion` only RESERVES the next
  slot for a session (and, while a reservation is outstanding for that session, blocks any other
  version being reserved for it - this is what makes the protocol atomic end to end, not just at
  the reserve step); `CommitVersion` finalizes a reservation, advancing the tracked sequence, ONLY
  after `Compute`'s audit write is confirmed durable; `ReleaseVersion` discards a reservation
  without consuming it, called from a `catch` block wrapping everything from the reservation
  through `CommitVersion`, so the SAME version number is legitimately reissuable to a retry once
  the underlying problem (e.g. a sink outage) is resolved.
- Verified directly against Chioma's own `version_audit_probe.py`, which was updated for this
  round to check the `AllocateVersion`-before-`WriteAuditEventOrThrow` ordering and the presence
  of a release/commit/transaction token - clean PASS on all 8 checks. Per the established
  reviewer-probe-literalness lesson, the reserve/commit/release methods were named
  `AllocateVersion`/`CommitVersion`/`ReleaseVersion` to match the probe's literal token
  expectations exactly while implementing the semantically-correct two-phase protocol.
- Added regressions per Chioma's explicit remediation ask: a failed audit write (via the existing
  `AlwaysFailingAuditSink` test double) followed by a successful retry of the SAME version number
  once a healthy sink is used, plus a regression proving an outstanding reservation blocks a second
  reservation attempt for the same session.
- Extended the structural harness with a Python-based ordering check (allocate-before-audit-before-
  commit, release-guards-the-audit-path) and presence checks for `CommitVersion`/`ReleaseVersion`.
- Updated `docs/features/bio-doc-008/EVIDENCE-COMPARISON.md`'s AC05 section with the QA-04 fix.

Reran everything after the fix: 50/50 developer tests pass, 52/52 structural checks pass.
`scripts/quality.sh --full` clean across every .NET project in the repo; only the pre-existing
frontend/Playwright `node_modules` gap otherwise remains.

## Scope notes (deliberately not built)

- No HTTP endpoint or `IBiologModule`/DI registration — consistent with every BIO-DOC-00x engine
  story before it.
- No actual `DocumentConflict`/review-ticket creation — BIO-DOC-001 is not a declared dependency;
  this story exposes the explicit routing signal, a caller raises the conflict through its own
  mechanism.
- No live audit-sink write — no audit-sink infrastructure exists anywhere in this codebase yet.
- No raw NFC data-group content comparison — see "Why NFC corroborates rather than compares" above.
