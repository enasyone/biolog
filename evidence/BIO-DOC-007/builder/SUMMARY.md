# BIO-DOC-007 — Builder Evidence

**Story:** Build NFC electronic identity document reader and passive authentication foundation
**Commit:** `21f9d27efc2ea0897a7dc8521a5fb1e773584c95`
**Branch:** `feature/BIO-DOC-007`
**Owner:** Ifeanyi — Document Intelligence Engineer

## What was built

`api/features/bio-doc-007/Biolog.Features.NfcReader` — a plain .NET 10 class library, no
ASP.NET Core dependency, no NuGet package references. One `ProjectReference` to BIO-DOC-006's
`Biolog.Features.DocumentClassifier` — this story's declared dependency — so a chip read carries
which classified document identity it was performed for.

- `NfcAccessKeySeedMaterial` (`Mrz` / `CardAccessNumber`) + `ChipAccessKeyDerivation` — ICAO 9303
  Part 11 Appendix D.1's SHA-1 seed/KDF, DES-parity adjusted, covering both BAC's "permitted
  document data" (MRZ) and PACE's "user interaction" (CAN) sources AC01's own wording names.
- `ChipDataGroupId`/`IChipDataGroupTransport`/`ChipDataGroupRead`/`ChipReadSession` — the
  replaceable, standards-focused ICAO 9303 LDS1 data-group reading abstraction (AC02). No real NFC
  transport ships — the same "no image-decoding library" boundary every BIO-DOC-00x engine before
  it kept, applied to NFC hardware instead of a camera. Data-group content is SHA-256 hashed, never
  carried as raw bytes (Dg1/Dg2 are identity PII, ADR-0003 Decision 5).
- `ChipReadOutcome` — carries `DocumentSessionId` + BIO-DOC-006 `Classification` (AC03's
  cross-link), `ChipCommunicationSucceeded`/`ReadFailureReason` as the type's only `bool`, and a
  separate `PassiveAuthentication` sub-result — AC05's read-failure-vs-authenticity-failure
  distinction is enforced structurally, not just by naming.
- `PassiveAuthenticationResult`/`PassiveAuthentication` — explicit `DataGroupHashesMatch`/
  `MismatchedDataGroups` and `CertificateChainValid`, using .NET's real `X509Chain` (base class
  library, no NuGet) against a caller-supplied trusted CSCA set, with `NotAttempted` as a third
  explicit state (AC04).
- `NfcAccessKeys`/`ChipDataGroupRead`/`ChipReadOutcome`/`PassiveAuthenticationResult` locked down
  (`internal init`, no public constructor) — the QA-03 lesson carried forward from every prior
  BIO-DOC-00x result type this epic.

## Why an OCR/MRZ-style oracle wasn't hand-recalled for the key-derivation KAT

ICAO 9303 Part 11 Appendix D.2 publishes a numeric worked example for the exact key-derivation
algorithm this story implements. Rather than transcribe a remembered hex constant and claim it
"matches the standard" without an independent way to verify the transcription, correctness is
proven two ways: (1) implementing the algorithm's own documented steps (SHA-1, 4-byte big-endian
counter, DES odd-parity adjustment) directly, and (2) cross-checking this implementation's output
against an independently-computed SHA-1 digest from Python's `hashlib` (a wholly separate
implementation) for the same input — ICAO's own published TD3 "ERIKSSON ANNA MARIA" fields, the
same fields BIO-DOC-002's `MrzSampleData.Td3Line2` already uses. Full reasoning in
`docs/features/bio-doc-007/NFC-READER.md`.

## Test/quality evidence

- `dotnet test tests/features/bio-doc-007/Biolog.Features.NfcReader.Tests/...` — 30/30 passed on
  the first run, including the cross-implementation known-answer test and real synthetic-certificate
  `X509Chain` validation (self-signed CSCA + a DSC it actually signed, built via
  `CertificateRequest`, base class library, no NuGet, no network).
- `bash tests/foundation/BIO-DOC-007.test.sh` — 30/30 structural checks passed.
- `bash scripts/quality.sh` (baseline) — PASS.
- `bash scripts/quality.sh --full` — every BIO-DOC-007-specific check passes; see
  `quality-output.txt`.

## Review round 1: CHANGES_REQUIRED, fixed and resubmitted

**Commit:** `858d9934ad06eba0b7280005bab00584f192092e`

Seyi's independent review (`evidence/BIO-DOC-007/codex/REVIEW-Seyi-round1.md`, BIO-DOC-007-QA-01,
Functional/Security/Regression FAIL against AC04) found `PassiveAuthentication.Evaluate` only
iterated `readDataGroups` when comparing hashes against the SOD's declared per-data-group hash
table, never checking whether every SOD-declared data group had actually been read at all. A chip
that simply omitted a declared group (e.g. DG2) from what it returned could still report
`DataGroupHashesMatch == true` on the strength of the groups it did hand over — a false
authenticity signal over incomplete evidence. Confirmed as a real, valid defect and fixed:

- Added `MissingDataGroups` to `PassiveAuthenticationResult` (SOD-declared ids never present among
  the actually-read groups), computed alongside the existing `MismatchedDataGroups`.
- `DataGroupHashesMatch` is now true only when nothing is mismatched AND nothing is missing.
- Added `PassiveAuthenticationTests.AC04_QA01_a_sod_declared_data_group_that_was_never_read_at_all_is_reported_missing_never_a_silent_pass`
  using Seyi's own exact reproduction shape (one group read and matching, a second declared but
  never read), plus a new structural harness check.

Reran everything after the fix: 31/31 developer tests pass, 32/32 structural checks pass. A fresh
`scripts/quality.sh --full` run shows every BIO-DOC-007-specific check clean (see
`quality-output.txt`, now captured at this commit); `.NET integration tests` failed again on the
same pre-existing flaky `ObservabilityTests` class documented across this epic's evidence (two
different assertion failures than the round-1 run, further confirming nondeterminism rather than a
regression — nothing in this fix touches logging/observability). Only the pre-existing
frontend/Playwright `node_modules` gap otherwise remains.

## Acceptance round 1: FAIL, fixed and resubmitted

**Commit:** `09665c6b6028fdca815cec4f9560ed0f7ea6db7e`

Chioma's acceptance review (`evidence/BIO-DOC-007/acceptance/REPORT.md`, BIO-DOC-007-QA-02, High,
Security/Functional/Regression FAIL against AC01) found `NfcAccessKeys.EncryptionKey`/`MacKey`
declared as `byte[] { get; internal init; }`. `internal init` only blocks reassigning the property
via `with` - it does nothing to stop a caller mutating the array's CONTENTS in place through the
public getter, since arrays are reference types and the getter returned the very array the record
stored. `keys.EncryptionKey[0] = 0xFF;` compiled and silently corrupted the key for every later
reader, defeating AC01's "derived only from permitted data" the moment the mutation happened - both
independent reviews (Seyi, Zainab) had missed this mutable-payload alias. Confirmed as a real,
valid defect (the included Python source probe reproduced it against the reviewed commit) and
fixed:

- `NfcAccessKeys` now stores the bytes in private fields never exposed directly.
- `EncryptionKey`/`MacKey` are get-only properties returning a fresh `(byte[])field.Clone()`
  defensive copy on every access, with a defensive copy also taken on construction.
- No setter of any kind exists on either property now - a strictly stronger lockdown than the
  previous accessibility-only restriction.
- Added `ChipAccessKeyDerivationTests.AC01_QA02_mutating_a_returned_key_array_never_corrupts_the_stored_key_for_later_callers`
  and a same-instance-never-returned-twice regression, plus a structural harness check.
- Re-ran Chioma's own probe against the fix: `mutable_key_source_probe.py` → PASS.

Reran everything after the fix: 33/33 developer tests pass, 34/34 structural checks pass. A fresh
`scripts/quality.sh --full` run shows every BIO-DOC-007-specific check clean (see
`quality-output.txt`, now captured at this commit); `.NET integration tests` failed again on the
same pre-existing flaky `ObservabilityTests` test, unrelated to this change. Only the pre-existing
frontend/Playwright `node_modules` gap otherwise remains.

## Acceptance round 2: FAIL, fixed and resubmitted

**Commit:** `2cecb4ed8399ee67944e316cbdc48bc0569b643a`

Chioma's second acceptance review (`evidence/BIO-DOC-007/acceptance/REPORT.md`, round 2,
BIO-DOC-007-QA-03, High, Security/Functional/Regression FAIL against AC02/AC04) confirmed QA-02
closed, then found the same class of bug one layer up: `ChipReadOutcome.DataGroupsRead`/
`DataGroupsUnavailable` and `PassiveAuthenticationResult.MismatchedDataGroups`/`MissingDataGroups`/
`CertificateChainStatus` stored the caller's own `List<T>` directly, exposed as `IReadOnlyList<T>`.
The interface removes mutation methods only at compile time - the stored object was still
genuinely a `List<T>` at runtime, so a caller could downcast
`(List<ChipDataGroupRead>)outcome.DataGroupsRead` and clear or alter chip evidence after passive
authentication had already run against it, while `PassiveAuthentication.Passed` kept reporting the
original, now-stale verdict. Confirmed as a real, valid defect and fixed:

- Both constructors now wrap a fresh copy of every such collection in `List<T>.AsReadOnly()` - a
  `ReadOnlyCollection<T>`, genuinely distinct from `List<T>` and not downcastable back to one -
  rather than storing the caller's own list.
- Added `ChipReadSessionTests.AC02_QA03_DataGroupsRead_cannot_be_downcast_back_to_a_mutable_list`
  (plus the `DataGroupsUnavailable` sibling) and
  `PassiveAuthenticationTests.AC04_QA03_result_collections_cannot_be_downcast_back_to_a_mutable_list`,
  asserting the downcast itself fails (`Assert.IsNotType<List<T>>`), not merely that values happen
  to match afterward.
- Re-ran Chioma's own probe against the fix: `mutable_evidence_collection_probe.py` → PASS
  (re-verified `mutable_key_source_probe.py` from round 1 still passes too).

Reran everything after the fix: 36/36 developer tests pass, 36/36 structural checks pass. A fresh
`scripts/quality.sh --full` run shows every BIO-DOC-007-specific check clean (see
`quality-output.txt`, now captured at this commit); `.NET integration tests` failed again on the
same pre-existing flaky `ObservabilityTests` test (yet another different assertion), unrelated to
this change. Only the pre-existing frontend/Playwright `node_modules` gap otherwise remains.

## Scope notes (deliberately not built)

- No HTTP endpoint or `IBiologModule`/DI registration — consistent with every BIO-DOC-00x engine
  story before it.
- No real BAC/PACE protocol negotiation (challenge-response, Diffie-Hellman/ECDH) and no real
  ISO 7816 APDU transport — both need actual NFC hardware this codebase has no driver for.
- No raw EF.SOD (CMS/ASN.1 `LDSSecurityObject`) parsing — `PassiveAuthentication.Evaluate` takes
  already-extracted hashes/certificate as input; a later story's job to add real SOD parsing.
- No Active Authentication — not named by any AC in this story.
