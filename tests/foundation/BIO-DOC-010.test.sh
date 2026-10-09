#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-010 (Nigeria Document Intelligence Pack v1).
# Behavioural assertions (entry construction, publish/immutability, verification, samples, demo
# cycle) live in the executable xunit suite under
# tests/features/bio-doc-010/Biolog.Features.DocumentTemplatePack.Tests (see
# docs/features/bio-doc-010/NIGERIA-DOCUMENT-PACK.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-010/Biolog.Features.DocumentTemplatePack
MODEL="$FEATURE/Model"
AUDIT="$FEATURE/Audit"
STORE="$FEATURE/Store"
DATA="$FEATURE/Data"
APID="$FEATURE/Api"
TESTS=tests/features/bio-doc-010/Biolog.Features.DocumentTemplatePack.Tests

# --- AC01: pack entries include country, issuer, document type, series/version, sides, expected
# fields, MRZ/barcode/NFC/security metadata and validation rules ---
check "AC01 DocumentTemplatePackEntry carries every declared metadata kind" \
  bash -c "grep -q 'string Country' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'string Issuer' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'string DocumentClass' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'string Series' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'int VersionNumber' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'IReadOnlyList<DocumentTemplatePackSide> Sides' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'IReadOnlyList<string> ExpectedFields' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'MrzMetadata Mrz' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'NfcMetadata Nfc' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'IReadOnlyList<string> SecurityFeatures' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'IReadOnlyList<FieldValidationRule> ValidationRules' '$MODEL/DocumentTemplatePackEntry.cs'"
check "AC01 barcode corroboration is carried per-side via the reused BIO-DOC-006 registry entry (Publish), not duplicated" \
  bash -c "grep -q 'DocumentTemplatePackSide' '$MODEL/DocumentTemplatePackSide.cs'"
check "AC01 Create validates structural shape (non-empty sides, non-empty expected fields, rules reference declared fields)" \
  bash -c "grep -q 'at least one physical side' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q 'at least one expected field' '$MODEL/DocumentTemplatePackEntry.cs' \
    && grep -q \"not one of this entry's own expected fields\" '$MODEL/DocumentTemplatePackEntry.cs'"
check "AC01 the actual governed Nigeria pack content exists with three entries" \
  bash -c "grep -q 'PassportEntryId' '$DATA/NigeriaDocumentPackV1.cs' \
    && grep -q 'NinSlipEntryId' '$DATA/NigeriaDocumentPackV1.cs' \
    && grep -q 'DriversLicenceEntryId' '$DATA/NigeriaDocumentPackV1.cs'"
check "AC01 test coverage exists" test -f "$TESTS/DocumentTemplatePackEntryTests.cs"

# --- AC02: templates are immutable once published ---
check "AC02 DocumentTemplatePackStatus is a two-state Draft/Published lifecycle" \
  bash -c "grep -q 'Draft,' '$MODEL/DocumentTemplatePackStatus.cs' && grep -q 'Published,' '$MODEL/DocumentTemplatePackStatus.cs'"
check "AC02 AddEntry throws once the pack is published" \
  bash -c "grep -q 'DocumentTemplatePackAlreadyPublishedException' '$MODEL/DocumentTemplatePack.cs'"
check "AC02 there is no RemoveEntry/ReplaceEntry/UpdateEntry method anywhere on the aggregate" \
  bash -c "! grep -qE 'void (Remove|Replace|Update)Entry' '$MODEL/DocumentTemplatePack.cs'"
check "AC02 Publish is durably audited before returning, the same audit-before-commit discipline as BIO-DOC-009" \
  bash -c "grep -q 'WriteAuditEventOrThrow' '$MODEL/DocumentTemplatePack.cs'"
check "AC02 test coverage exists, including the structural no-remove-method proof" \
  bash -c "grep -q 'there_is_no_way_to_remove_or_replace_an_entry_once_added' '$TESTS/DocumentTemplatePackTests.cs'"

# --- AC03: reference/test samples are governed and synthetic where required ---
check "AC03 DocumentTemplateSample.IsSynthetic is a structural guarantee, not a caller-supplied flag" \
  bash -c "grep -q 'public bool IsSynthetic => true;' '$MODEL/DocumentTemplateSample.cs'"
check "AC03 the sample store rejects a sample naming an entry the pack does not govern" \
  bash -c "grep -q 'UngovernedSampleEntryException' '$STORE/IDocumentTemplateSampleStore.cs'"
check "AC03 governed synthetic reference samples exist for the real Nigeria pack, one per entry" \
  bash -c "grep -q 'static IReadOnlyList<DocumentTemplateSample> Samples' '$DATA/NigeriaDocumentPackV1.cs'"
check "AC03 test coverage exists" test -f "$TESTS/DocumentTemplateSampleGovernanceTests.cs"

# --- AC04: pack version is recorded on every verification ---
check "AC04 DocumentTemplateVerificationOutcome carries PackId/PackVersion/EntryId on every result" \
  bash -c "grep -q 'string PackId' '$MODEL/DocumentTemplateVerificationOutcome.cs' \
    && grep -q 'string PackVersion' '$MODEL/DocumentTemplateVerificationOutcome.cs' \
    && grep -q 'string EntryId' '$MODEL/DocumentTemplateVerificationOutcome.cs'"
check "AC04 Verify refuses to run against an unpublished pack (a draft has no governance standing)" \
  bash -c "grep -q 'PackNotPublishedException' '$MODEL/DocumentTemplateVerification.cs'"
check "AC04 Verify is durably audited before returning" \
  bash -c "grep -q 'WriteAuditEventOrThrow' '$MODEL/DocumentTemplateVerification.cs'"
check "AC04 the audit event's own Detail records pack_version explicitly" \
  bash -c "grep -q 'packVersion' '$AUDIT/DocumentTemplatePackAuditEventBuilder.cs'"
check "AC04 test coverage exists, including a direct proof of the recorded pack version" \
  bash -c "grep -q 'Verify_always_durably_records_an_audit_event_carrying_the_exact_pack_version' '$TESTS/DocumentTemplateVerificationTests.cs'"

# --- AC05: connected through DocumentVerifyService to a real live implementation ---
check "AC05 a real HTTP endpoint exists at POST /api/v1/documents/identify" \
  bash -c "grep -q \"MapPost(BasePath, IdentifyAsync)\" '$APID/DocumentTemplatePackEndpoints.cs' && grep -q '/api/v1/documents/identify' '$APID/DocumentTemplatePackEndpoints.cs'"
check "AC05 the module is registered into the host (Program.cs, one line, additive)" \
  bash -c "grep -q 'new BioDoc010Module()' api/Biolog.Api/Program.cs"
check "AC05 the module adapter lives in api/Biolog.Api/Modules, not inside the feature project" \
  test -f api/Biolog.Api/Modules/BioDoc010Module.cs
check "AC05 web/features/documents already targets this exact route (BIO-UX-009's own LIVE_ENDPOINTS, unmodified contract) - the frontend parsing/routing logic needed no change to connect it" \
  bash -c "grep -q \"'document-identification': '/api/v1/documents/identify'\" web/features/documents/live.ts"
check "AC05 DocumentVerifyService gains a third, independent 'documentIdentification' port key (genuine HYBRID, capture stays mock so it never blocks this step)" \
  bash -c "grep -q \"DocumentVerifyService: \['primaryAction', 'authenticity', 'documentIdentification'\]\" web/lib/feature-mode.ts"
check "AC05 no unapproved UI redesign - DocumentVerifyApp.tsx and its stylesheet are untouched by this story" \
  bash -c "! git diff --name-only HEAD -- web/features/documents/DocumentVerifyApp.tsx web/features/documents/document-verify.module.css 2>/dev/null | grep -q . \
    && ! git diff --cached --name-only HEAD -- web/features/documents/DocumentVerifyApp.tsx web/features/documents/document-verify.module.css 2>/dev/null | grep -q ."
check "AC05 response is a superset of the existing LiveStepBody shape (state/message), no frontend parsing change required" \
  bash -c "grep -q '\"state\"' '$APID/DocumentTemplatePackDtos.cs' && grep -q '\"message\"' '$APID/DocumentTemplatePackDtos.cs'"
check "AC05 Map() registers exactly one route on this endpoint class (no unauthenticated GET-by-id surface, applying BIO-DOC-009-F01's lesson from the start)" \
  bash -c "test \"\$(grep -c 'endpoints\.Map' '$APID/DocumentTemplatePackEndpoints.cs')\" -eq 1 && ! grep -q 'MapGet' '$APID/DocumentTemplatePackEndpoints.cs'"
check "AC05 frontend test coverage exists for the new independent port key" \
  test -f tests/frontend/unit/bio-doc-010-documents-document-identification-port.test.ts
check "AC05 a Playwright test exercises the real HTTP endpoint end to end" \
  test -f tests/frontend/playwright/bio-doc-010-identify-live.spec.ts

# --- QA lesson carried forward: locked-down result types, applied proactively ---
check "DocumentTemplateVerificationOutcome/DocumentTemplatePackEntry are internal init/no public constructor, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/DocumentTemplateVerificationOutcome.cs'"
check "standing reflection-based lockdown regression test exists" \
  test -f "$TESTS/DocumentTemplatePackResultTypesAreLockedDownTests.cs"

# --- No hidden shared mutable state (BIO-DOC-009-F01's own follow-on bug, applied proactively) ---
check "DocumentTemplatePackService owns its own instance-level registry/store/audit sink, never static" \
  bash -c "! grep -qE 'private static readonly (DocumentIntelligenceRegistry|IDocumentTemplatePackStore|IDocumentTemplatePackAuditSink)' '$APID/DocumentTemplatePackService.cs'"
check "test coverage proves two independent service instances never share state" \
  bash -c "grep -q 'two_independent_service_instances_never_share_state' '$TESTS/DocumentTemplatePackServiceTests.cs'"

# --- Dependency direction: this feature depends on BIO-DOC-006/009 only ---
check "DocumentTemplatePack references BIO-DOC-006's document classifier" \
  bash -c "grep -q 'Biolog.Features.DocumentClassifier.csproj' '$FEATURE/Biolog.Features.DocumentTemplatePack.csproj'"
check "DocumentTemplatePack references BIO-DOC-009's authenticity assessment orchestrator" \
  bash -c "grep -q 'Biolog.Features.AuthenticityAssessment.csproj' '$FEATURE/Biolog.Features.DocumentTemplatePack.csproj'"
check "DocumentTemplatePack does NOT reference BIO-DOC-001/002/003/004/005/007/008 directly (not declared dependencies)" \
  bash -c "! grep -qE 'bio-doc-00[1234578]/' '$FEATURE/Biolog.Features.DocumentTemplatePack.csproj'"
check "no sibling BIO-DOC-00x project takes a dependency back on this feature (no cycle)" \
  bash -c "! grep -rq 'bio-doc-010' api/features/bio-doc-006/*/*.csproj api/features/bio-doc-009/*/*.csproj"
check "this feature project never references Biolog.Api.csproj (one-way dependency)" \
  bash -c "! grep -qE '<ProjectReference[^>]*Biolog\.Api\.csproj' '$FEATURE/Biolog.Features.DocumentTemplatePack.csproj'"

# --- No new canonical identifier prefix invented locally (ADR-0001 S1.2); shared contracts untouched ---
check "ADR-0001-S1.2 the frozen identifier registry was not touched by this story" \
  bash -c "! git diff --cached --name-only HEAD -- contracts/ 2>/dev/null | grep -q . && ! git diff --name-only HEAD -- contracts/ 2>/dev/null | grep -q ."

# --- Test/evidence/traceability scaffolding exists ---
check "feature test project exists" test -f "$TESTS/Biolog.Features.DocumentTemplatePack.Tests.csproj"
check "story traceability spec exists" test -f governance/stories/BIO-DOC-010.json
check "feature design doc exists" test -f docs/features/bio-doc-010/NIGERIA-DOCUMENT-PACK.md

echo "----"
echo "BIO-DOC-010 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
