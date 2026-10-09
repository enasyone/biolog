#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-011 (tenant visual document-template editor).
# Behavioural assertions (lifecycle transitions, region validation, corpus running,
# concurrency, tenant isolation) live in the executable xunit suite under
# tests/features/bio-doc-011/Biolog.Features.DocumentTemplateEditor.Tests (see
# docs/features/bio-doc-011/DOCUMENT-TEMPLATE-EDITOR.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-011/Biolog.Features.DocumentTemplateEditor
DOMAIN="$FEATURE/Domain"
AUDIT="$FEATURE/Audit"
TENANCY="$FEATURE/Tenancy"
APID="$FEATURE/Api"
TESTS=tests/features/bio-doc-011/Biolog.Features.DocumentTemplateEditor.Tests

# --- AC01: upload reference layout and draw named field/graphic regions ---
check "AC01 DocumentTemplateReferenceLayout carries width/height/content hash" \
  bash -c "grep -q 'record DocumentTemplateReferenceLayout(int Width, int Height, string ContentHash, string UploadedAt)' '$DOMAIN/DocumentTemplateReferenceLayout.cs'"
check "AC01 field regions reuse BIO-DOC-004's own OcrTemplateField directly (zero translation layer)" \
  bash -c "grep -q 'IReadOnlyList<OcrTemplateField> FieldRegions' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC01 graphic regions are this story's own named, non-OCR region type" \
  bash -c "grep -q 'record DocumentTemplateGraphicRegion(string Name, int Left, int Top, int Width, int Height)' '$DOMAIN/DocumentTemplateGraphicRegion.cs'"
check "AC01 DefineRegions refuses before a reference layout exists to validate bounds against" \
  bash -c "grep -q 'ReferenceLayoutRequired' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC01 DefineRegions rejects duplicate region names and out-of-bounds geometry" \
  bash -c "grep -q 'DuplicateRegionName' '$DOMAIN/DocumentTemplateDraft.cs' && grep -q 'RegionOutOfBounds' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC01 test coverage exists" test -f "$TESTS/DocumentTemplateDraftTests.cs"

# --- AC02: template has draft/test/approval/publish lifecycle ---
check "AC02 DocumentTemplateDraftStatus is the exact four-stage draft/test/approval/publish lifecycle" \
  bash -c "grep -q 'Draft,' '$DOMAIN/DocumentTemplateDraftStatus.cs' && grep -q 'Test,' '$DOMAIN/DocumentTemplateDraftStatus.cs' \
    && grep -q 'Approval,' '$DOMAIN/DocumentTemplateDraftStatus.cs' && grep -q 'Published,' '$DOMAIN/DocumentTemplateDraftStatus.cs'"
check "AC02 SubmitForTest moves Draft to Test" \
  bash -c "grep -q 'public DocumentTemplateDraft SubmitForTest' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC02 RecordCorpusRun moves Test to Approval on pass, back to Draft on fail" \
  bash -c "grep -q 'result.Passed ? DocumentTemplateDraftStatus.Approval : DocumentTemplateDraftStatus.Draft' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC02 Publish moves Approval to Published" \
  bash -c "grep -q 'public DocumentTemplateDraft Publish' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC02 test coverage exists for every transition" \
  bash -c "grep -q 'AC02_SubmitForTest_moves_Draft_to_Test' '$TESTS/DocumentTemplateDraftTests.cs'"

# --- AC03: test corpus must pass before publish ---
check "AC03 a real synthetic-corpus runner exists that calls BIO-DOC-004's actual TemplateOcrEngine" \
  bash -c "grep -q 'TemplateOcrEngine.Read' '$DOMAIN/SyntheticCorpusRenderer.cs'"
check "AC03 the corpus run result is computed internally, never a caller-supplied boolean" \
  bash -c "grep -q 'internal DocumentTemplateCorpusRunResult(bool passed' '$DOMAIN/DocumentTemplateCorpusRunResult.cs' && ! grep -q 'bool passed' '$APID/DocumentTemplateDraftDtos.cs'"
check "AC03 an empty corpus can never satisfy the gate" \
  bash -c "grep -q 'EmptyCorpus' '$DOMAIN/SyntheticCorpusRenderer.cs'"
check "AC03 test coverage proves both a genuine pass and a genuine (deliberately corrupted) fail" \
  bash -c "grep -q 'AC03_RunCorpusAsync_actually_runs_the_real_OCR_engine_and_advances_to_Approval_on_a_genuine_pass' '$TESTS/DocumentTemplateDraftServiceTests.cs' \
    && grep -q 'AC03_RunCorpusAsync_genuinely_fails_a_corrupted_sample_and_returns_to_Draft' '$TESTS/DocumentTemplateDraftServiceTests.cs'"

# --- AC04: tenant templates cannot masquerade as official global templates ---
check "AC04 TenantId is required and resolved server-side only, never caller-supplied" \
  bash -c "grep -q 'RequireTenant' '$FEATURE/DocumentTemplateDraftService.cs' && ! grep -q 'tenantId' '$APID/DocumentTemplateDraftDtos.cs'"
check "AC04 PublicReference carries a tenant-prefixed identifier distinct from any global pack entry id" \
  bash -c "grep -q 'PublicReference => \$\"tenant:{TenantId}:{TemplateId}:v{Version}\"' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC04 construction validates a well-formed canonical tenant id (ADR-0001), the same discipline BIO-CRD-008 established" \
  bash -c "grep -q 'CanonicalId.IsTenantId(tenantId)' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC04 this is a wholly separate aggregate/store from BIO-DOC-010's own global pack - no shared type" \
  bash -c "! grep -q 'DocumentTemplatePack' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC04 test coverage exists, including the structural tenant-id-cannot-be-bypassed proof" \
  bash -c "grep -q 'AC04_there_is_no_constructor_that_can_produce_a_draft_without_a_well_formed_tenant_id' '$TESTS/DocumentTemplateDraftTests.cs'"

# --- AC05: published versions are immutable ---
check "AC05 IsImmutable is true only for Published" \
  bash -c "grep -q 'status is DocumentTemplateDraftStatus.Published' '$DOMAIN/DocumentTemplateDraftStatus.cs'"
check "AC05 there is no method to remove/replace/update a region or layout in place once published - only CreateNextDraftVersion" \
  bash -c "! grep -qE 'void (Remove|Replace|Update)(Region|ReferenceLayout)' '$DOMAIN/DocumentTemplateDraft.cs' && grep -q 'CreateNextDraftVersion' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC05 every content mutator checks Draft status explicitly before applying" \
  bash -c "grep -q 'RequireDraft' '$DOMAIN/DocumentTemplateDraft.cs'"
check "AC05 test coverage exists proving every mutator refuses once Published" \
  bash -c "grep -q 'AC05_every_content_mutator_refuses_once_Published' '$TESTS/DocumentTemplateDraftTests.cs'"

# --- AC06: approved web surface / HYBRID disclosure ---
check "AC06 the module is registered into the host (Program.cs, one line, additive)" \
  bash -c "grep -q 'new BioDoc011Module()' api/Biolog.Api/Program.cs"
check "AC06 the module adapter lives in api/Biolog.Api/Modules, not inside the feature project" \
  test -f api/Biolog.Api/Modules/BioDoc011Module.cs
check "AC06 no unapproved UI redesign - no web/ file is touched by this story" \
  bash -c "! git diff --name-only HEAD -- web/ 2>/dev/null | grep -q . && ! git diff --cached --name-only HEAD -- web/ 2>/dev/null | grep -q ."
check "AC06 BIO-DOC-010's own /document-verify DocumentVerifyService wiring is untouched (regression check)" \
  bash -c "grep -q \"DocumentVerifyService: \['primaryAction', 'authenticity', 'documentIdentification'\]\" web/lib/feature-mode.ts"
check "AC06 the design doc explicitly discloses why no new admin UI route was built for this story" \
  bash -c "grep -q 'Why this story ships no new web UI' docs/features/bio-doc-011/DOCUMENT-TEMPLATE-EDITOR.md"
# Note: governance/ui-integration-map.json (BIO-DOC-011 -> surface web, port DocumentVerifyService,
# route /document-verify only - no admin editor route) lives in the orchestration control pack,
# not this repository's own tracked tree - not something a structural check here can read;
# verified once by reading it directly during design (see the design doc's own section), the
# same documented limitation BIO-ACC-003's own foundation test already discloses.

# --- Cross-cutting: dependency direction, module discipline ---
check "No ProjectReference to Biolog.Api.csproj from this feature project" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\\.Api\\.csproj' '$FEATURE/Biolog.Features.DocumentTemplateEditor.csproj'"
check "The only declared ProjectReference is BIO-DOC-004's OcrEngine" \
  bash -c "test \"\$(grep -c '<ProjectReference' '$FEATURE/Biolog.Features.DocumentTemplateEditor.csproj')\" -eq 1 && grep -q 'bio-doc-004' '$FEATURE/Biolog.Features.DocumentTemplateEditor.csproj'"
check "No ProjectReference to BIO-IDN-006 (internal-locked, authorization-decision wall - see module header)" \
  bash -c "! grep -q 'bio-idn-006' '$FEATURE/Biolog.Features.DocumentTemplateEditor.csproj'"
check "Tenant context seam is the zero-reference pattern, adapted only in the host module" \
  bash -c "grep -q 'interface ICurrentTenantProvider' '$TENANCY/ICurrentTenantProvider.cs' && grep -q 'ITenantContextAccessor' api/Biolog.Api/Modules/BioDoc011Module.cs"
check "InternalsVisibleTo is scoped to Biolog.Api and this project's own test assembly only" \
  bash -c "grep -q 'InternalsVisibleTo Include=\"Biolog.Api\"' '$FEATURE/Biolog.Features.DocumentTemplateEditor.csproj' \
    && grep -q 'InternalsVisibleTo Include=\"Biolog.Features.DocumentTemplateEditor.Tests\"' '$FEATURE/Biolog.Features.DocumentTemplateEditor.csproj'"
check "Audit integrity: a lost concurrency race audits Denied, never a false Allowed" \
  bash -c "grep -q 'await AuditAsync' '$FEATURE/DocumentTemplateDraftService.cs' && grep -q 'AuditUnavailable' '$DOMAIN/DocumentTemplateDraftError.cs' \
    && grep -q 'A_lost_concurrency_race_records_a_Denied_audit_event_never_an_Allowed_one' '$TESTS/DocumentTemplateDraftServiceTests.cs'"
check "Service test coverage exists, including concurrency-conflict and tenant-isolation proofs" \
  bash -c "grep -q 'A_stale_expected_revision_throws_a_concurrency_conflict' '$TESTS/DocumentTemplateDraftServiceTests.cs' \
    && grep -q 'Two_tenants_drafts_are_fully_isolated_from_each_other' '$TESTS/DocumentTemplateDraftServiceTests.cs'"

# --- Round 2 (Zainab ZB-DOC011-01, Critical): live provenance + permission gating ---
check "ZB-DOC011-01 a provenance gate exists using the current Kemi-ruled canonical (fail-closed, no fallback key) shape" \
  bash -c "grep -q 'class HmacActorProvenanceStore' '$APID/ActorProvenanceGate.cs' && grep -q 'MinimumSecretLength' '$APID/ActorProvenanceGate.cs' \
    && grep -q 'return false;' '$APID/ActorProvenanceGate.cs'"
check "ZB-DOC011-01 the provenance gate uses its own feature-local configuration key" \
  bash -c "grep -q 'DocumentTemplateEditor:ActorProvenanceSecret' '$APID/ActorProvenanceGate.cs'"
check "ZB-DOC011-01 every mutation route requires a verified provenance proof" \
  bash -c "test \$(grep -c 'provenanceGate.Require' '$APID/DocumentTemplateDraftEndpoints.cs') -ge 9"
check "ZB-DOC011-01 read routes also require a verified provenance proof, not just a resolved actor" \
  bash -c "grep -A4 'ListVersionsAsync(HttpContext' '$APID/DocumentTemplateDraftEndpoints.cs' | grep -q 'provenanceGate.Require' \
    && grep -A4 'GetVersionAsync(HttpContext' '$APID/DocumentTemplateDraftEndpoints.cs' | grep -q 'provenanceGate.Require' \
    && grep -A4 'GetPublishedAsync(HttpContext' '$APID/DocumentTemplateDraftEndpoints.cs' | grep -q 'provenanceGate.Require'"
check "ZB-DOC011-01 a missing/invalid provenance proof maps to HTTP 401" \
  bash -c "grep -q 'ActorProvenanceRequiredException' '$APID/DocumentTemplateDraftEndpoints.cs' && grep -q 'Status401Unauthorized' '$APID/DocumentTemplateDraftEndpoints.cs'"
check "ZB-DOC011-01 a live IPermissionGateway check guards every mutation" \
  bash -c "grep -q 'interface IPermissionGateway' '$DOMAIN/IPermissionGateway.cs' && grep -q 'RequirePermissionAsync' '$FEATURE/DocumentTemplateDraftService.cs'"
check "ZB-DOC011-01 publishing requires a distinct, higher-impact permission from every other mutation" \
  bash -c "grep -q 'PublishPermission = \"document_template_editor.publish\"' '$FEATURE/DocumentTemplateDraftService.cs' \
    && grep -q 'ManagePermission = \"document_template_editor.manage\"' '$FEATURE/DocumentTemplateDraftService.cs'"
check "ZB-DOC011-01 the permission gateway is wrapped in the host module, not referenced directly from this feature project" \
  bash -c "! grep -q 'bio-idn-006' '$FEATURE/Biolog.Features.DocumentTemplateEditor.csproj' && grep -q 'PermissionGatewayAdapter' api/Biolog.Api/Modules/BioDoc011Module.cs"
check "ZB-DOC011-01 maker-checker separation for publish is explicitly disclaimed, not silently skipped" \
  bash -c "grep -qi 'maker-checker' '$FEATURE/DocumentTemplateDraftService.cs'"

# --- Round 2 (Zainab ZB-DOC011-02, High): truthful audit/commit ordering ---
check "ZB-DOC011-02 every mutation to a lineage is serialized through a lock registry" \
  bash -c "grep -q 'class DocumentTemplateDraftLockRegistry' '$FEATURE/DocumentTemplateDraftLockRegistry.cs' && grep -q 'lockRegistry.AcquireAsync' '$FEATURE/DocumentTemplateDraftService.cs'"
check "ZB-DOC011-02 the repository no longer does its own compare-and-set (the lock is now the serialization authority)" \
  bash -c "! grep -qE '(bool|public) TryUpdate\(' '$DOMAIN/IDocumentTemplateDraftRepository.cs' && grep -q 'void Update(DocumentTemplateDraft updated)' '$DOMAIN/IDocumentTemplateDraftRepository.cs'"
check "ZB-DOC011-02 CreateNextVersionAsync computes the next version number and adds it under the same lock (no Max()+1 race)" \
  bash -c "awk '/public async Task<DocumentTemplateDraft> CreateNextVersionAsync/,/^    }/' '$FEATURE/DocumentTemplateDraftService.cs' | grep -q 'lockRegistry.AcquireAsync'"
check "ZB-DOC011-02 an ambiguous audit write-throw is resolved via ExistsAsync, not assumed to be a failure" \
  bash -c "grep -q 'auditSink.ExistsAsync' '$FEATURE/DocumentTemplateDraftService.cs'"
check "ZB-DOC011-02 test coverage proves the ambiguous-write, genuine-failure, permission-denial, and concurrent-next-version cases" \
  bash -c "grep -q 'ZB_DOC011_02_an_ambiguous_write_throw_is_resolved_via_ExistsAsync' '$TESTS/DocumentTemplateDraftServiceTests.cs' \
    && grep -q 'ZB_DOC011_02_a_genuine_total_audit_failure_refuses_the_mutation' '$TESTS/DocumentTemplateDraftServiceTests.cs' \
    && grep -q 'ZB_DOC011_01_a_mutation_without_the_required_permission_is_refused' '$TESTS/DocumentTemplateDraftServiceTests.cs' \
    && grep -q 'ZB_DOC011_02_two_concurrent_CreateNextVersionAsync_calls_never_collide' '$TESTS/DocumentTemplateDraftServiceTests.cs'"

# --- Round 2 (Seyi F01 / Zainab ZB-DOC011-03, High): reads require an actual permission, not just provenance ---
check "F01/ZB-DOC011-03 a dedicated read permission exists, distinct from manage/publish" \
  bash -c "grep -q 'ReadPermission = \"document_template_editor.read\"' '$FEATURE/DocumentTemplateDraftService.cs'"
check "F01/ZB-DOC011-03 all three read methods require a permission before touching the repository" \
  bash -c "grep -q 'RequireReadPermissionAsync' '$FEATURE/DocumentTemplateDraftService.cs' \
    && test \$(grep -c 'await RequireReadPermissionAsync' '$FEATURE/DocumentTemplateDraftService.cs') -eq 3"
check "F01/ZB-DOC011-03 the read permission check accepts read OR either mutation permission (manage/publish implies view)" \
  bash -c "awk '/private async Task RequireReadPermissionAsync/,/^    }/' '$FEATURE/DocumentTemplateDraftService.cs' | grep -q 'ReadPermission' \
    && awk '/private async Task RequireReadPermissionAsync/,/^    }/' '$FEATURE/DocumentTemplateDraftService.cs' | grep -q 'ManagePermission' \
    && awk '/private async Task RequireReadPermissionAsync/,/^    }/' '$FEATURE/DocumentTemplateDraftService.cs' | grep -q 'PublishPermission'"
check "F01/ZB-DOC011-03 endpoints await the now-async, permission-checked read methods" \
  bash -c "grep -q 'await service.ListVersionsAsync' '$APID/DocumentTemplateDraftEndpoints.cs' \
    && grep -q 'await service.GetVersionAsync' '$APID/DocumentTemplateDraftEndpoints.cs' \
    && grep -q 'await service.GetPublishedAsync' '$APID/DocumentTemplateDraftEndpoints.cs'"
check "F01/ZB-DOC011-03 test coverage proves unprivileged reads are refused, manage implies read, and cross-tenant reads stay isolated" \
  bash -c "grep -q 'ZB_DOC011_03_an_actor_without_read_manage_or_publish_permission_cannot_read_any_draft_content' '$TESTS/DocumentTemplateDraftServiceTests.cs' \
    && grep -q 'ZB_DOC011_03_an_actor_who_only_holds_manage_permission_can_still_read_what_they_manage' '$TESTS/DocumentTemplateDraftServiceTests.cs' \
    && grep -q 'Two_tenants_drafts_are_fully_isolated_from_each_other' '$TESTS/DocumentTemplateDraftServiceTests.cs'"

echo
echo "RESULT: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
