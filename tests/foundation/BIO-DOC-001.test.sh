#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-001 (Document Verify session/evidence foundation).
# Behavioural assertions (session lifecycle, immutability, concurrency, manual review, the
# proposal-never-auto-applies invariant) live in the executable xunit suite under
# tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests (see
# docs/features/bio-doc-001/DOCUMENT-VERIFY-FOUNDATION.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-001/Biolog.Features.DocumentVerify
MODEL="$FEATURE/Model"

# --- AC01: DocumentSession supports person-linked or pre-person onboarding ---
check "AC01 DocumentSession model exists" test -f "$MODEL/DocumentSession.cs"
check "AC01 PersonId is nullable (pre-person onboarding representable)" \
  bash -c "grep -q 'string? PersonId' '$MODEL/DocumentSession.cs'"
check "AC01 Open accepts a null personId" \
  bash -c "grep -q 'static DocumentSession Open(string id, string tenantId, string? personId, string nowUtc)' '$MODEL/DocumentSession.cs'"
check "AC01 LinkPerson transitions a session to a person" \
  bash -c "grep -q 'public void LinkPerson(' '$MODEL/DocumentSession.cs'"
check "AC01 relinking to a different person is refused, not silently overwritten" \
  bash -c "grep -q 'already_linked_to_different_person' '$MODEL/DocumentSession.cs'"

# --- ADR-0001 Decision 3: every mutable aggregate carries a version, compare-and-set on write ---
check "ADR-0001-D3 DocumentSession starts at version 1" \
  bash -c "grep -q 'InitialVersion = 1' '$MODEL/DocumentSession.cs'"
check "ADR-0001-D3 a stale expected version raises a dedicated ConcurrencyException" \
  bash -c "grep -q 'class ConcurrencyException' '$MODEL/DocumentSession.cs'"

# --- AC02: source images are immutable evidence objects with hashes and page/side metadata ---
check "AC02 DocumentEvidence model exists" test -f "$MODEL/DocumentEvidence.cs"
check "AC02 DocumentEvidence is an immutable record (no mutator methods, init-only)" \
  bash -c "grep -q 'public sealed record DocumentEvidence' '$MODEL/DocumentEvidence.cs'"
check "AC02 carries a SHA-256 hash" bash -c "grep -q 'Sha256Hash' '$MODEL/DocumentEvidence.cs'"
check "AC02 carries page number and side metadata" \
  bash -c "grep -q 'PageNumber' '$MODEL/DocumentEvidence.cs' && grep -q 'DocumentSide Side' '$MODEL/DocumentEvidence.cs'"
check "AC02 DocumentSide enum exists with page/side values" test -f "$MODEL/DocumentSide.cs"
check "AC02 device-claim vs server-authoritative receipt time are both recorded (ADR-0001 S2.4)" \
  bash -c "grep -q 'CapturedAt' '$MODEL/DocumentEvidence.cs' && grep -q 'ReceivedAt' '$MODEL/DocumentEvidence.cs'"
check "AC02/ADR-0001 no bare version field on the append-only evidence record" \
  bash -c "! grep -qE '^\s*public required int Version' '$MODEL/DocumentEvidence.cs'"

# --- AC03: evidence records preserve raw, normalized, confidence, source and bounding-box metadata ---
check "AC03 DocumentFieldEvidence model exists" test -f "$MODEL/DocumentFieldEvidence.cs"
check "AC03 preserves RawValue and NormalizedValue separately" \
  bash -c "grep -q 'RawValue' '$MODEL/DocumentFieldEvidence.cs' && grep -q 'NormalizedValue' '$MODEL/DocumentFieldEvidence.cs'"
check "AC03 carries a Confidence score" bash -c "grep -q 'double Confidence' '$MODEL/DocumentFieldEvidence.cs'"
check "AC03 carries which engine sourced the value" bash -c "grep -q 'FieldEvidenceSource Source' '$MODEL/DocumentFieldEvidence.cs'"
check "AC03 carries a nullable bounding box" bash -c "grep -q 'BoundingBox? BoundingBox' '$MODEL/DocumentFieldEvidence.cs'"
check "AC03 BoundingBox value object exists" test -f "$MODEL/BoundingBox.cs"
check "AC03 confidence is validated into [0.0, 1.0]" \
  bash -c "grep -q 'confidence > 1.0' '$MODEL/DocumentFieldEvidence.cs'"

# --- AC04: document results never silently overwrite PERSON fields ---
check "AC04 PersonFieldChangeProposal model exists" test -f "$MODEL/PersonFieldChangeProposal.cs"
check "AC04 a freshly proposed change has no applied timestamp/actor" \
  bash -c "grep -q 'AppliedAt = null,' '$MODEL/PersonFieldChangeProposal.cs' && grep -q 'AppliedBy = null,' '$MODEL/PersonFieldChangeProposal.cs'"
check "AC04 Accept requires a non-empty actor, no anonymous acceptance" \
  bash -c "grep -q 'RequireNonEmpty(acceptedBy' '$MODEL/PersonFieldChangeProposal.cs'"
check "AC04 Accept returns a new record rather than mutating in place ('this with')" \
  bash -c "grep -q 'return this with' '$MODEL/PersonFieldChangeProposal.cs'"
check "AC04 no method in this feature writes to a Person aggregate (no such type exists here)" \
  bash -c "! grep -rq 'class Person\b' '$FEATURE'"

# --- AC05: manual review state and conflict evidence are supported ---
check "AC05 ManualReviewState enum exists" test -f "$MODEL/ManualReviewState.cs"
check "AC05 DocumentConflict model exists" test -f "$MODEL/DocumentConflict.cs"
check "AC05 a conflict requires at least two disagreeing evidence records" \
  bash -c "grep -q 'Count < 2' '$MODEL/DocumentConflict.cs'"
check "AC05 DocumentSession has a ManualReviewPending status" \
  bash -c "grep -q 'ManualReviewPending' api/features/bio-doc-001/Biolog.Features.DocumentVerify/Model/DocumentSessionStatus.cs"
check "AC05 the in-memory store moves a session into manual review when a conflict is raised" \
  bash -c "grep -q 'EnterManualReview' '$FEATURE/Storage/InMemoryDocumentSessionStore.cs'"
check "AC04/AC05 a proposal for a conflicted field is flagged as requiring manual review" \
  bash -c "grep -q 'requiresManualReview' '$FEATURE/Storage/InMemoryDocumentSessionStore.cs'"

# --- ADR-0002 S4.3: cross-tenant access fails closed as not-found ---
check "ADR-0002-S4.3 a dedicated not-found exception exists (never a 403-shaped one)" \
  test -f "$FEATURE/Storage/DocumentSessionNotFoundException.cs"
check "ADR-0002-S4.3 the store's resolver checks tenant ownership before returning a session" \
  bash -c "grep -q 'record.Session.TenantId != tenantId' '$FEATURE/Storage/InMemoryDocumentSessionStore.cs'"

# --- No new canonical identifier prefix invented locally (ADR-0001 S1.2) ---
check "ADR-0001-S1.2 the frozen identifier registry was not touched by this story" \
  bash -c "! git diff --cached --name-only HEAD -- contracts/ 2>/dev/null | grep -q . && ! git diff --name-only HEAD -- contracts/ 2>/dev/null | grep -q ."

# --- Wiring: registered into the module host, not built as an island ---
check "DocumentModule adapter exists in the API shell" test -f api/Biolog.Api/Modules/DocumentModule.cs
check "DocumentModule implements IBiologModule" bash -c "grep -q ': IBiologModule' api/Biolog.Api/Modules/DocumentModule.cs"
check "Program.cs registers DocumentModule through the host" bash -c "grep -q 'new DocumentModule()' api/Biolog.Api/Program.cs"
check "Biolog.Api references the feature project (one-way; feature has no ASP.NET Core dependency)" \
  bash -c "grep -q 'Biolog.Features.DocumentVerify.csproj' api/Biolog.Api/Biolog.Api.csproj"
check "the feature project itself takes no ProjectReference back to Biolog.Api (no cycle)" \
  bash -c "! grep -q '<ProjectReference' '$FEATURE/Biolog.Features.DocumentVerify.csproj'"

# --- Test/evidence/traceability scaffolding exists ---
check "feature test project exists" test -f tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/Biolog.Features.DocumentVerify.Tests.csproj
check "story traceability spec exists" test -f governance/stories/BIO-DOC-001.json
check "feature foundation doc exists" test -f docs/features/bio-doc-001/DOCUMENT-VERIFY-FOUNDATION.md

echo "----"
echo "BIO-DOC-001 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
