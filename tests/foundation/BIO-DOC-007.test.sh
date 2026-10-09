#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-007 (NFC electronic identity document reader and
# passive authentication foundation). Behavioural assertions (key derivation, data-group reading,
# cross-linking, passive authentication) live in the executable xunit suite under
# tests/features/bio-doc-007/Biolog.Features.NfcReader.Tests (see
# docs/features/bio-doc-007/NFC-READER.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-007/Biolog.Features.NfcReader
MODEL="$FEATURE/Model"

# --- AC01: NFC access keys are derived only from permitted document data/user interaction ---
check "AC01 NfcAccessKeySeedMaterial (Mrz / CardAccessNumber) exists" test -f "$MODEL/NfcAccessKeySeedMaterial.cs"
check "AC01 the seed material type has no public constructor - only the validating Create factories" \
  bash -c "! grep -qE 'public (Mrz|CardAccessNumber)\(' '$MODEL/NfcAccessKeySeedMaterial.cs'"
check "AC01 key derivation uses SHA-1 per ICAO 9303 Part 11 Appendix D.1, no external crypto package" \
  bash -c "grep -q 'SHA1.HashData' '$MODEL/ChipAccessKeyDerivation.cs'"
check "AC01 derived keys are parity-adjusted for DES use" \
  bash -c "grep -q 'AdjustParity' '$MODEL/ChipAccessKeyDerivation.cs'"
check "AC01 no NuGet package reference was added for cryptography" \
  bash -c "! grep -q '<PackageReference' '$FEATURE/Biolog.Features.NfcReader.csproj'"

# --- Acceptance finding (BIO-DOC-007-QA-02, High): key getters exposed mutable shared arrays ---
check "QA-02 NfcAccessKeys returns a defensive copy from every key getter" \
  bash -c "grep -q 'public byte\[\] EncryptionKey =>' '$MODEL/NfcAccessKeys.cs' && grep -q 'Clone()' '$MODEL/NfcAccessKeys.cs'"
check "QA-02 NfcAccessKeys stores keys in private fields, never a directly-exposed mutable array" \
  bash -c "grep -qE 'private readonly byte\[\] _encryptionKey' '$MODEL/NfcAccessKeys.cs'"

# --- AC02: supported data groups are read through a standards-focused abstraction ---
check "AC02 ChipDataGroupId enumerates the ICAO 9303 LDS1 data groups" test -f "$MODEL/ChipDataGroupId.cs"
check "AC02 IChipDataGroupTransport is the replaceable low-level transport seam" test -f "$MODEL/IChipDataGroupTransport.cs"
check "AC02 ChipReadSession orchestrates reading through the transport abstraction, not directly" \
  bash -c "grep -q 'IChipDataGroupTransport transport' '$MODEL/ChipReadSession.cs'"
check "AC02 a data group's raw bytes are hashed, never carried as a property (PII, ADR-0003 Decision 5)" \
  bash -c "grep -q 'Sha256Hash' '$MODEL/ChipDataGroupRead.cs' && ! grep -qE 'byte\[\] *RawBytes' '$MODEL/ChipDataGroupRead.cs'"

# --- Acceptance finding (BIO-DOC-007-QA-03, High): IReadOnlyList collections were downcastable and mutable ---
check "QA-03 ChipReadOutcome wraps its collections in AsReadOnly(), never exposing the caller's own list" \
  bash -c "grep -q 'AsReadOnly()' '$MODEL/ChipReadOutcome.cs'"
check "QA-03 PassiveAuthenticationResult wraps its collections in AsReadOnly() too" \
  bash -c "grep -q 'AsReadOnly()' '$MODEL/PassiveAuthenticationResult.cs'"

# --- AC03: chip data is cross-linked to the document session ---
check "AC03 ChipReadOutcome carries the document session id" \
  bash -c "grep -q 'DocumentSessionId' '$MODEL/ChipReadOutcome.cs'"
check "AC03 ChipReadOutcome carries the BIO-DOC-006 classification this read was performed for" \
  bash -c "grep -q 'DocumentClassCandidate? Classification' '$MODEL/ChipReadOutcome.cs'"

# --- AC04: passive authentication/certificate-chain results are explicit where supported ---
check "AC04 PassiveAuthenticationResult model exists" test -f "$MODEL/PassiveAuthenticationResult.cs"
check "AC04 certificate chain validation uses real X509Chain, no external crypto package" \
  bash -c "grep -q 'X509Chain' '$MODEL/PassiveAuthentication.cs'"
check "AC04 data-group hash mismatches are named explicitly, not just a bare bool" \
  bash -c "grep -q 'MismatchedDataGroups' '$MODEL/PassiveAuthenticationResult.cs'"
check "AC04 not-attempted is an explicit, distinct state from a failed attempt" \
  bash -c "grep -q 'bool Attempted' '$MODEL/PassiveAuthenticationResult.cs' && grep -q 'NotAttempted' '$MODEL/PassiveAuthenticationResult.cs'"

# --- Independent review finding (BIO-DOC-007-QA-01, High): SOD-declared groups never read were ignored ---
check "QA-01 a SOD-declared data group that was never read is named explicitly as missing" \
  bash -c "grep -q 'MissingDataGroups' '$MODEL/PassiveAuthenticationResult.cs' && grep -q 'missingDataGroups' '$MODEL/PassiveAuthentication.cs'"
check "QA-01 hashes only match when nothing declared by the SOD is missing from the read" \
  bash -c "grep -qE 'mismatched\.Count == 0 && missing\.Count == 0' '$MODEL/PassiveAuthentication.cs'"

# --- AC05: failure to read NFC is distinct from authenticity failure ---
check "AC05 ChipReadOutcome's own success signal is ChipCommunicationSucceeded, not an authenticity flag" \
  bash -c "grep -q 'bool ChipCommunicationSucceeded' '$MODEL/ChipReadOutcome.cs'"
check "AC05 authenticity lives in its own separate result type, not folded into ChipReadOutcome" \
  bash -c "! grep -qE '\bbool (Authentic|Valid|Passed)\b' '$MODEL/ChipReadOutcome.cs'"
check "AC05 a read failure carries an explicit, honest reason string" \
  bash -c "grep -q 'ReadFailureReason' '$MODEL/ChipReadOutcome.cs'"

# --- QA-03 lesson carried forward: trust-bearing result types cannot be built or copy-modified externally ---
check "NfcAccessKeys properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/NfcAccessKeys.cs'"
check "ChipReadOutcome properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/ChipReadOutcome.cs'"
check "PassiveAuthenticationResult properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/PassiveAuthenticationResult.cs'"
check "standing reflection-based lockdown regression test exists" \
  test -f tests/features/bio-doc-007/Biolog.Features.NfcReader.Tests/NfcResultTypesAreLockedDownTests.cs

# --- Dependency direction: this feature depends on BIO-DOC-006 only, never a sibling raw engine ---
check "NfcReader references BIO-DOC-006's document classifier" \
  bash -c "grep -q 'Biolog.Features.DocumentClassifier.csproj' '$FEATURE/Biolog.Features.NfcReader.csproj'"
check "NfcReader does NOT reference BIO-DOC-001/002/003/004/005 directly (not declared dependencies)" \
  bash -c "! grep -qE 'bio-doc-00[12345]/' '$FEATURE/Biolog.Features.NfcReader.csproj'"
check "BIO-DOC-006's own project takes no dependency back on this feature (no cycle)" \
  bash -c "! grep -q 'bio-doc-007' api/features/bio-doc-006/Biolog.Features.DocumentClassifier/Biolog.Features.DocumentClassifier.csproj"
check "no ASP.NET Core / Biolog.Api project or framework reference was added to this feature" \
  bash -c "! grep -qE '<(ProjectReference|FrameworkReference)[^>]*(Biolog\.Api|Microsoft\.AspNetCore)' '$FEATURE/Biolog.Features.NfcReader.csproj'"

# --- No new canonical identifier prefix invented locally (ADR-0001 S1.2); shared contracts untouched ---
check "ADR-0001-S1.2 the frozen identifier registry was not touched by this story" \
  bash -c "! git diff --cached --name-only HEAD -- contracts/ 2>/dev/null | grep -q . && ! git diff --name-only HEAD -- contracts/ 2>/dev/null | grep -q ."

# --- Test/evidence/traceability scaffolding exists ---
check "feature test project exists" test -f tests/features/bio-doc-007/Biolog.Features.NfcReader.Tests/Biolog.Features.NfcReader.Tests.csproj
check "story traceability spec exists" test -f governance/stories/BIO-DOC-007.json
check "feature design doc exists" test -f docs/features/bio-doc-007/NFC-READER.md

echo "----"
echo "BIO-DOC-007 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
