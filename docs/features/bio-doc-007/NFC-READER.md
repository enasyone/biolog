# BIO-DOC-007 — NFC Electronic Identity Document Reader and Passive Authentication Foundation

`api/features/bio-doc-007/Biolog.Features.NfcReader` is a plain .NET 10 class library (no
ASP.NET Core dependency, no NuGet package references) implementing ICAO 9303's access-key
derivation, a standards-focused data-group reading abstraction, and passive authentication
evaluation. One `ProjectReference` to BIO-DOC-006's `Biolog.Features.DocumentClassifier` - this
story's one declared dependency - so a chip read can carry which classified document identity it
was performed for.

## Model

| Type | Kind | Purpose |
| --- | --- | --- |
| `NfcAccessKeySeedMaterial` | abstract record | `Mrz` (BAC) or `CardAccessNumber` (PACE-CAN) - the two "permitted document data / user interaction" sources AC01 names. |
| `ChipAccessKeyDerivation` | static | ICAO 9303 Part 11 Appendix D.1's shared SHA-1 seed/KDF, parity-adjusted for DES (AC01). |
| `NfcAccessKeys` | record (locked down) | Derived encryption/MAC key pair. |
| `ChipDataGroupId` | enum | ICAO 9303 Part 10 LDS1 elements (Com, Dg1-Dg16, Sod) (AC02). |
| `IChipDataGroupTransport` | interface | The replaceable low-level NFC/ISO 7816 transport seam (AC02). |
| `ChipDataGroupRead` | record (locked down) | One read data group - SHA-256 hash, never raw bytes (PII). |
| `ChipReadSession` | static | Orchestrates a full read: secure channel, per-data-group reads, optional passive authentication (AC02/AC03/AC05). |
| `ChipReadOutcome` | record (locked down) | The end-to-end result: session id + classification (AC03), communication success/failure (AC05), data groups read/unavailable, passive authentication. |
| `PassiveAuthenticationInputs` | record | The already-extracted SOD hashes + DSC certificate + trusted CSCA set passive authentication needs. |
| `PassiveAuthenticationResult` | record (locked down) | Explicit hash-match and certificate-chain outcomes (AC04). |
| `PassiveAuthentication` | static | `Evaluate(...)` - real `X509Chain` certificate-chain validation. |

## AC01: access keys are derived only from permitted document data/user interaction

AC01's own wording names two legitimate sources, and `NfcAccessKeySeedMaterial` is a closed,
two-variant type mirroring them exactly:

- `Mrz` - ICAO 9303 Part 11 Basic Access Control's seed: document number, date of birth and date
  of expiry, each with its own check digit. The type structurally cannot carry anything beyond
  these six fields (no name, no nationality, no photo) - "derived only from permitted data" is
  enforced by what the type CAN hold, not by convention. Check digits are taken as already-computed
  input rather than this story depending on BIO-DOC-002's MRZ engine to compute them - the same
  "plain domain parameter, not a resolved dependency" pattern BIO-DOC-005's `ParserCaller` used
  (this story's DEPENDENCIES line names only BIO-DOC-006).
- `CardAccessNumber` - PACE's CAN-based seed: a 6-digit number the user reads directly off the
  document and enters - "user interaction" in the AC's own wording.

Both variants feed the identical `ChipAccessKeyDerivation` SHA-1 seed/KDF machinery (ICAO 9303
Part 11 Appendix D.1 defines one shared key-derivation function both BAC and PACE use) - only
`SeedInputBytes` differs.

**Scope note:** this story does not implement BAC's challenge-response mutual authentication or
PACE's Diffie-Hellman/ECDH key-agreement handshake - those need an actual chip to negotiate with,
the same "no image-decoding library, so recognition works from an already-captured raster"
boundary every prior BIO-DOC-00x engine kept, applied here to NFC protocol negotiation instead of
camera capture. `IChipDataGroupTransport.EstablishSecureChannel` is the seam a real driver
implements that handshake behind.

### On not hard-coding a published ICAO worked-example digest

ICAO 9303 Part 11 Appendix D.2 publishes a numeric worked example for this exact key-derivation
algorithm. `ChipAccessKeyDerivation` deliberately does not claim to reproduce that specific
published byte sequence from memory - transcribing a remembered hex constant and asserting it
"matches the standard" without an independent way to check the transcription would be exactly the
kind of unverified claim this codebase's engineering discipline exists to avoid (the same reasoning
behind BIO-DOC-002's MRZ check-digit oracle choices). Instead, correctness is verified two ways
that do not depend on trusting a memorized constant:

1. The algorithm steps themselves - SHA-1, a 4-byte big-endian counter, truncation to 16 bytes, DES
   odd-parity adjustment - are standard, independently-specified operations implemented directly
   against their own definitions, not against a memorized worked example.
2. `ChipAccessKeyDerivationTests.AC01_key_derivation_matches_an_independent_python_hashlib_implementation_of_the_same_algorithm`
   cross-checks this implementation's output against an independently-computed SHA-1 digest
   (Python's `hashlib`, a wholly separate implementation from .NET's `SHA1.HashData`) for the same
   input (ICAO's own published TD3 "ERIKSSON ANNA MARIA" fields), over the same documented
   algorithm steps - proving this implementation's SHA-1 usage, counter encoding and parity
   adjustment agree with an independent implementation, without needing to trust a hand-recalled
   ICAO numeric constant.

## AC02: supported data groups are read through a standards-focused abstraction

`ChipDataGroupId` names ICAO 9303 Part 10's LDS1 elements. `IChipDataGroupTransport` is the
replaceable seam - the same "BIOLOG owns the standard/orchestration, low-level hardware access is
replaceable" shape BIO-DOC-003's `IBarcodeDecoder` established for barcode decoding, applied here
to NFC transport. No implementation of this interface ships in this story (no real NFC hardware/
driver exists in this codebase); `ChipReadSession.Read` orchestrates against a caller-supplied
implementation. `ChipDataGroupRead` carries a SHA-256 hash of the raw bytes, never the bytes
themselves - Dg1 (MRZ) and Dg2 (encoded face) are directly identity PII, the same "hash, not
payload" reasoning BIO-DOC-003's `BarcodeDecodeResult` and BIO-DOC-001's `DocumentEvidence` already
apply (ADR-0003 Decision 5).

## AC03: chip data is cross-linked to the document session

`ChipReadOutcome.DocumentSessionId` is a plain string identifier - this story does not depend on
BIO-DOC-001 (not a declared dependency), so the id is taken as a plain parameter the same way
BIO-DOC-002's `MrzFieldRead.ToDocumentFieldEvidence` adapter and BIO-DOC-005's `ParserCaller` take
their own session/tenant context. `ChipReadOutcome.Classification` additionally carries the
BIO-DOC-006 `DocumentClassCandidate` this chip read was performed for, when known - the concrete
reason this story declares BIO-DOC-006 as its one dependency.

## AC04: passive authentication/certificate-chain results are explicit where supported

`PassiveAuthenticationResult` reports `DataGroupHashesMatch` (with `MismatchedDataGroups` naming
exactly which ones, never a bare bool) and `CertificateChainValid` as two independently-visible
outcomes - a caller can tell "the chip's own data was tampered with" apart from "the signer isn't
trusted", rather than one outcome silently masking the other. `Attempted` is a third, explicit
state: `PassiveAuthenticationResult.NotAttempted` is what `ChipReadOutcome.PassiveAuthentication`
defaults to whenever a caller does not supply `PassiveAuthenticationInputs` to
`ChipReadSession.Read` - the absence of an attempt is itself explicit, never silently coerced into
a pass or a fail.

**Fixed review finding (BIO-DOC-007-QA-01, High):** Seyi's independent review found
`PassiveAuthentication.Evaluate` only iterated `readDataGroups`, never checking whether every data
group the SOD *declared* a hash for was actually read at all - a chip that simply omitted a
declared group (e.g. DG2) from what it returned could still report `DataGroupHashesMatch == true`
on the strength of the groups it DID hand over, a false authenticity signal over incomplete
evidence. Fixed by computing `MissingDataGroups` (SOD-declared ids never present among
`readDataGroups`) alongside `MismatchedDataGroups`, and folding both into `DataGroupHashesMatch`:
it is true only when nothing is mismatched AND nothing is missing.
`PassiveAuthenticationTests.AC04_QA01_a_sod_declared_data_group_that_was_never_read_at_all_is_reported_missing_never_a_silent_pass`
uses Seyi's own exact reproduction shape (one group read and matching, a second declared but never
read) to prove the fix.

Certificate-chain validation uses .NET's real `System.Security.Cryptography.X509Certificates.X509Chain`
(base class library, no NuGet package) with `X509ChainTrustMode.CustomRootTrust` against a
caller-supplied trusted CSCA set. `X509RevocationMode.NoCheck` is set explicitly and documented,
not silently defaulted - no CRL/OCSP infrastructure exists in this environment for synthetic,
offline test certificates to check against.

**Scope note - this does not parse a raw EF.SOD.** A real ICAO 9303 SOD is a CMS/PKCS7
`SignedData` structure wrapping an ASN.1 `LDSSecurityObject` (the per-data-group hash table) and
embedding the Document Signer Certificate. Parsing that raw byte structure end to end -
`System.Security.Cryptography.Pkcs.SignedCms` could parse the CMS envelope, but nothing in the
base class library parses `LDSSecurityObject`'s own ASN.1 shape - is a document-forensics-grade
parser out of proportion for this story. `PassiveAuthentication.Evaluate` instead takes the
already-extracted `sodDeclaredHashes`/`documentSignerCertificate` as input, the same "the caller
already ran X, hand us the result" shape BIO-DOC-006's classifier used for already-run OCR
template matches. A later story that adds real SOD parsing feeds this method's inputs from that
parser's output without this method changing.

## AC05: failure to read NFC is distinct from authenticity failure

Enforced at the type level, not just by field naming. `ChipReadOutcome.ChipCommunicationSucceeded`/
`ReadFailureReason` describe purely whether the secure channel and data-group reads happened at
all - a protocol/hardware-layer question. Whether the data that WAS successfully read is genuine is
`PassiveAuthenticationResult`'s question entirely, held in its own
`ChipReadOutcome.PassiveAuthentication` property and evaluated by a separate call - the only `bool`
property on `ChipReadOutcome` itself is `ChipCommunicationSucceeded`; there is structurally no
property on that type that could conflate "we couldn't talk to the chip" with "what we read wasn't
genuine" (`NfcResultTypesAreLockedDownTests`'s sibling test,
`ChipReadSessionTests.AC05_a_failed_secure_channel_is_reported_as_a_read_failure_with_no_data_groups_and_no_authenticity_claim`,
asserts this reflectively).

## Locked down against post-hoc tampering (the BIO-DOC-001 QA-03 lesson, again)

`NfcAccessKeys`, `ChipDataGroupRead`, `ChipReadOutcome` and `PassiveAuthenticationResult` all carry
data a downstream caller actually trusts (derived key bytes, a read's hash, communication/
authenticity flags). Every property is `internal init` with an explicit `internal` constructor -
the same reasoning as every prior BIO-DOC-00x result type this epic: a public `init` would let a
caller substitute forged key bytes that never went through the real key-derivation function, or
launder a failed/not-attempted authenticity result into one that claims `Passed`, via `with`.

**Fixed acceptance finding (BIO-DOC-007-QA-02, High):** `internal init` only blocks REASSIGNING a
property - it does nothing to stop a caller mutating a `byte[]` property's CONTENTS in place
through the public getter, since arrays are reference types and an auto-property's getter returns
the very array the record stores. `keys.EncryptionKey[0] = 0xFF;` compiled and silently corrupted
the key every later caller would see, defeating AC01's "derived only from permitted data" once the
mutation happened. `NfcAccessKeys` now stores `_encryptionKey`/`_macKey` in private fields never
exposed directly, and `EncryptionKey`/`MacKey` are get-only properties that return
`(byte[])field.Clone()` - a fresh defensive copy on every access, and a defensive copy of the
input on construction too. The properties have no setter of any kind now, a strictly stronger
lockdown than the previous accessibility-only restriction (`with { EncryptionKey = ... }` no
longer compiles for ANY caller, not just external ones).
`ChipAccessKeyDerivationTests.AC01_QA02_mutating_a_returned_key_array_never_corrupts_the_stored_key_for_later_callers`
proves a mutated returned array never reaches the stored key.

**Fixed acceptance finding (BIO-DOC-007-QA-03, High - the story's own second acceptance finding,
not to be confused with the epic-wide "BIO-DOC-001 QA-03 lesson" this section is named after):**
the same class of bug as QA-02, one layer up. `ChipReadOutcome.DataGroupsRead`/
`DataGroupsUnavailable` and `PassiveAuthenticationResult.MismatchedDataGroups`/`MissingDataGroups`/
`CertificateChainStatus` were `IReadOnlyList<T>`-typed properties directly storing the
`List<T>` a caller (`ChipReadSession`/`PassiveAuthentication`) had built. `IReadOnlyList<T>`
removes mutation methods only at COMPILE time - the stored object was still genuinely a
`List<T>` at runtime, so `(List<ChipDataGroupRead>)outcome.DataGroupsRead` compiled and let a
caller clear or alter chip evidence after passive authentication had already run against it,
while `PassiveAuthentication.Passed` kept reporting the original, now-stale verdict. Both
constructors now wrap a FRESH copy of every such collection in `List<T>.AsReadOnly()` - a
`ReadOnlyCollection<T>`, a genuinely distinct type from `List<T>` that cannot be downcast back to
one, so a caller can never mutate what these types expose regardless of how many times or from
where they access it.
`ChipReadSessionTests.AC02_QA03_DataGroupsRead_cannot_be_downcast_back_to_a_mutable_list` and
`PassiveAuthenticationTests.AC04_QA03_result_collections_cannot_be_downcast_back_to_a_mutable_list`
prove the downcast itself fails (`Assert.IsNotType<List<T>>`), not merely that values happen to
match after the fact.

## Reproducing the tests

```sh
dotnet test tests/features/bio-doc-007/Biolog.Features.NfcReader.Tests/Biolog.Features.NfcReader.Tests.csproj
bash tests/foundation/BIO-DOC-007.test.sh
```

## Scope notes (deliberately not built)

- No HTTP endpoint or `IBiologModule`/DI registration - consistent with every BIO-DOC-00x engine
  story before it.
- No real BAC/PACE protocol negotiation (challenge-response, Diffie-Hellman/ECDH) and no real
  ISO 7816 APDU transport - both need actual NFC hardware this codebase has no driver for; see
  `IChipDataGroupTransport`'s remarks.
- No raw EF.SOD (CMS/ASN.1 `LDSSecurityObject`) parsing - see AC04's scope note above.
- No Active Authentication (chip-signed nonce challenge, Dg15's public key) - not named by any AC
  in this story; a later story's job if the platform needs it.
