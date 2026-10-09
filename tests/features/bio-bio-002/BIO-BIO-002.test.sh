#!/usr/bin/env bash
# Offline-safe structural checks for BIO-BIO-002 (guided tenprint/single-finger fingerprint
# enrolment). Behavioural assertions live in the executable xunit suite under
# tests/features/bio-bio-002/ (see docs/features/bio-bio-002/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-bio-002/Biolog.Features.FingerprintEnrolment

# --- AC01: 10-print and single-finger modes exist ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.FingerprintEnrolment.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.FingerprintEnrolment.csproj'"
check "AC01 an EnrolmentMode enum with TenPrint and SingleFinger exists" \
  bash -c "grep -q 'TenPrint,' '$FEATURE/Domain/EnrolmentMode.cs' && grep -q 'SingleFinger,' '$FEATURE/Domain/EnrolmentMode.cs'"
check "AC01 TenPrint mode targets exactly the ten standard finger positions" \
  bash -c "grep -q 'AllTenPrintPositions' '$FEATURE/Domain/FingerprintEnrolmentSession.cs' && [ \"\$(grep -c '^    [A-Za-z]*,\$' '$FEATURE/Domain/FingerPosition.cs')\" = 10 ]"
check "AC01 SingleFinger mode requires exactly one target position, enforced in the aggregate" \
  bash -c "grep -q 'SingleFingerPositionRequired' '$FEATURE/Domain/FingerprintEnrolmentSession.cs'"
check "AC01 a structural test proves both modes exist and target the right positions" \
  bash -c "grep -q 'AC01_TenPrint_mode_targets_exactly_the_ten_standard_positions' tests/features/bio-bio-002/FingerprintEnrolmentSessionTests.cs && grep -q 'AC01_SingleFinger_mode_targets_exactly_the_one_requested_position' tests/features/bio-bio-002/FingerprintEnrolmentSessionTests.cs"

# --- AC02: finger identity and quality are recorded ---
check "AC02 FingerRecord carries a FingerPosition and a QualityScore" \
  bash -c "grep -q 'FingerPosition Position' '$FEATURE/Domain/FingerRecord.cs' && grep -q 'int? QualityScore' '$FEATURE/Domain/FingerRecord.cs'"
check "AC02 a quality scorer contract exists, separate from the service" test -f "$FEATURE/Quality/IFingerQualityScorer.cs"
check "AC02 a synthetic quality scorer implementation exists (AC05-precedent local-dev requirement)" \
  bash -c "test -f '$FEATURE/Quality/SyntheticFingerQualityScorer.cs' && grep -q 'IFingerQualityScorer' '$FEATURE/Quality/SyntheticFingerQualityScorer.cs'"
check "AC02 the service records identity and quality together on capture" \
  bash -c "grep -q 'QualityScore = quality' '$FEATURE/Service/FingerprintEnrolmentService.cs' && grep -q 'Position = position' '$FEATURE/Service/FingerprintEnrolmentService.cs'"
check "AC02 a structural test proves identity and quality are both recorded" \
  bash -c "grep -q 'AC02_CaptureFingerAsync_records_finger_identity_and_a_quality_score' tests/features/bio-bio-002/FingerprintEnrolmentServiceTests.cs"

# --- AC03: recapture and missing/amputated finger exceptions ---
check "AC03 a distinct MissingAmputated capture status exists" \
  bash -c "grep -q 'MissingAmputated' '$FEATURE/Domain/FingerCaptureStatus.cs'"
check "AC03 recapture increments a capture count, not a bare overwrite" \
  bash -c "grep -q 'CaptureCount = current.CaptureCount + 1' '$FEATURE/Domain/FingerprintEnrolmentSession.cs'"
check "AC03 a dedicated missing/amputated method with a reason exists" \
  bash -c "grep -q 'MarkMissingOrAmputated' '$FEATURE/Domain/FingerprintEnrolmentSession.cs' && grep -q 'MissingAmputatedReason' '$FEATURE/Domain/FingerRecord.cs'"
check "AC03 a resolved missing/amputated position cannot be captured over" \
  bash -c "grep -q 'PositionAlreadyMissingAmputated' '$FEATURE/Domain/FingerprintEnrolmentSession.cs'"
check "AC03 structural tests cover recapture and the exception path" \
  bash -c "grep -q 'AC03_recapturing_an_already_captured_position' tests/features/bio-bio-002/FingerprintEnrolmentSessionTests.cs && grep -q 'AC03_marking_a_finger_missing_amputated' tests/features/bio-bio-002/FingerprintEnrolmentSessionTests.cs"

# --- AC04: raw fingerprint images are discarded by default unless policy authorises retention ---
check "AC04 FingerRecord's raw image field is optional and clones on write/read (BIO-BIO-001-QA-01 pattern)" \
  bash -c "grep -q 'byte\[\]? RetainedRawImage' '$FEATURE/Domain/FingerRecord.cs' && grep -q '_retainedRawImage.Clone()' '$FEATURE/Domain/FingerRecord.cs'"
check "AC04 the session carries a RetentionAuthorised policy flag, fixed at start" \
  bash -c "grep -q 'bool RetentionAuthorised' '$FEATURE/Domain/FingerprintEnrolmentSession.cs'"
check "AC04 the service only attaches a raw image when the session authorised retention" \
  bash -c "grep -q 'current.RetentionAuthorised ? sample.Payload : null' '$FEATURE/Service/FingerprintEnrolmentService.cs'"
check "AC04 structural tests cover both the discard-by-default and the retain-with-authorisation paths" \
  bash -c "grep -q 'AC04_raw_image_is_discarded_by_default' tests/features/bio-bio-002/FingerprintEnrolmentServiceTests.cs && grep -q 'AC04_raw_image_is_retained_only_when_the_session_authorised_retention' tests/features/bio-bio-002/FingerprintEnrolmentServiceTests.cs"

# --- AC05: enrolment is audited ---
check "AC05 an audit sink contract and event type exist" \
  bash -c "test -f '$FEATURE/Audit/IFingerprintEnrolmentAuditSink.cs' && test -f '$FEATURE/Audit/FingerprintEnrolmentAuditEvent.cs'"
check "AC05 the audit event carries no byte[]/payload-shaped field" \
  bash -c "! grep -q 'byte\[\]' '$FEATURE/Audit/FingerprintEnrolmentAuditEvent.cs'"
check "AC05 every mutating service method audits before committing (ADR-0003 Decision 7)" \
  bash -c "[ \"\$(grep -c 'await AuditAsync(' '$FEATURE/Service/FingerprintEnrolmentService.cs')\" -ge 4 ]"
check "AC05 an ambiguous audit-write throw is resolved via ExistsAsync, not assumed to have failed" \
  bash -c "grep -q 'auditSink.ExistsAsync' '$FEATURE/Service/FingerprintEnrolmentService.cs'"
check "AC05 commits pass CancellationToken.None once the audit write is confirmed" \
  bash -c "[ \"\$(grep -c 'CancellationToken.None' '$FEATURE/Service/FingerprintEnrolmentService.cs')\" -ge 4 ]"
check "AC05 a structural test proves session-start/capture/complete are all audited" \
  bash -c "grep -q 'AC05_every_mutation_writes_an_audit_event' tests/features/bio-bio-002/FingerprintEnrolmentServiceTests.cs"

# --- Governance (BIO-IDN-001-QA-01 shape) ---
check "governance: the repository and its implementation are internal, not public" \
  bash -c "grep -q '^internal interface IFingerprintEnrolmentRepository' '$FEATURE/Domain/IFingerprintEnrolmentRepository.cs' && grep -q '^internal sealed class InMemoryFingerprintEnrolmentRepository' '$FEATURE/Domain/InMemoryFingerprintEnrolmentRepository.cs'"
check "governance: the service is internal too" \
  bash -c "grep -q '^internal sealed class FingerprintEnrolmentService' '$FEATURE/Service/FingerprintEnrolmentService.cs'"
check "governance: the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.FingerprintEnrolment.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.FingerprintEnrolment.Tests\" />' '$FEATURE/Biolog.Features.FingerprintEnrolment.csproj'"
check "governance: reflection tests prove the repository/service are inaccessible to ordinary production consumers" \
  bash -c "grep -q 'IFingerprintEnrolmentRepository_and_InMemoryFingerprintEnrolmentRepository_are_not_public' tests/features/bio-bio-002/FingerprintEnrolmentGovernanceTests.cs"
check "governance: the repository independently re-validates tenant/identity immutability and CAS at the persistence boundary" \
  bash -c "grep -q 'identity_or_tenant_immutable' '$FEATURE/Domain/InMemoryFingerprintEnrolmentRepository.cs'"
check "governance: cross-tenant lookup returns null, not an exception (no enumeration oracle)" \
  bash -c "grep -q 'Cross-tenant lookup returns null' '$FEATURE/Domain/InMemoryFingerprintEnrolmentRepository.cs'"

# --- Feature-to-feature dependency shape (BIO-DOC-002/003 precedent) ---
check "this project references BIO-BIO-001's provider-neutral biometric library" \
  bash -c "grep -q 'Biolog.Features.Biometrics.csproj' '$FEATURE/Biolog.Features.FingerprintEnrolment.csproj'"
check "module project references stay one-way (no ProjectReference back to Biolog.Api)" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\.Api\.csproj' '$FEATURE/Biolog.Features.FingerprintEnrolment.csproj'"

# --- Module wiring ---
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioBio002Module.cs && ! test -f '$FEATURE/BioBio002Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioBio002Module()' api/Biolog.Api/Program.cs"
check "no HTTP endpoints are mapped (AC01-AC05 are about the session model, matching BIO-BIO-001's deferred-integration call)" \
  bash -c "! grep -q 'endpoints\.Map' api/Biolog.Api/Modules/BioBio002Module.cs"

# --- Developer test wiring ---
check "unit tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.FingerprintEnrolment.csproj' tests/features/bio-bio-002/Biolog.Features.FingerprintEnrolment.Tests.csproj"
check "docs/features/bio-bio-002/README.md documents the feature" test -f docs/features/bio-bio-002/README.md
check "builder evidence recorded" test -d evidence/features/bio-bio-002/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
