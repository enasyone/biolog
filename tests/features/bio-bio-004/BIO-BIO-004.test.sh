#!/usr/bin/env bash
# Offline-safe structural checks for BIO-BIO-004 (1:1 fingerprint verification). Behavioural
# assertions live in the executable xunit suite under tests/features/bio-bio-004/ (see
# docs/features/bio-bio-004/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-bio-004/Biolog.Features.FingerprintVerification

# --- AC01: claimed identity resolves one authorised reference set ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.FingerprintVerification.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.FingerprintVerification.csproj'"
check "AC01 verification resolves the claimed identity's templates through BIO-BIO-001's own template store" \
  bash -c "grep -q 'templateStore.GetTemplatesAsync' '$FEATURE/Service/FingerprintVerificationService.cs'"
check "AC01 a missing reference is a precondition failure, not a routine outcome" \
  bash -c "grep -q 'FingerprintVerificationException.NoAuthorisedReference' '$FEATURE/Service/FingerprintVerificationService.cs'"
check "AC01 a structural test proves the newest enrolled template is resolved" \
  bash -c "grep -q 'AC01_verification_compares_against_the_single_newest_enrolled_template' tests/features/bio-bio-004/FingerprintVerificationServiceTests.cs"
check "AC01 a structural test proves an unenrolled claimed identity throws" \
  bash -c "grep -q 'AC01_no_enrolled_template_throws_a_precondition_error_not_a_routine_outcome' tests/features/bio-bio-004/FingerprintVerificationServiceTests.cs"

# --- AC02: provider score/decision/model version are recorded ---
check "AC02 a persisted verification record type exists" test -f "$FEATURE/Domain/FingerprintVerificationRecord.cs"
check "AC02 the record carries score, matcher, provider id and provider (model) version" \
  bash -c "grep -q 'double? Score' '$FEATURE/Domain/FingerprintVerificationRecord.cs' && grep -q 'string? MatcherId' '$FEATURE/Domain/FingerprintVerificationRecord.cs' && grep -q 'string ProviderId' '$FEATURE/Domain/FingerprintVerificationRecord.cs' && grep -q 'string ProviderVersion' '$FEATURE/Domain/FingerprintVerificationRecord.cs'"
check "AC02 a record store persistence seam exists" test -f "$FEATURE/Domain/IFingerprintVerificationRecordStore.cs"
check "AC02 the service persists exactly one record per verification call" \
  bash -c "grep -q 'recordStore.SaveAsync' '$FEATURE/Service/FingerprintVerificationService.cs'"
check "AC02 a structural test proves score/matcher/provider/model-version are recorded on a match" \
  bash -c "grep -q 'AC02_a_matched_verification_records_score_matcher_provider_and_model_version' tests/features/bio-bio-004/FingerprintVerificationServiceTests.cs"

# --- AC03: no-match and acquisition failure are distinct outcomes ---
check "AC03 a closed comparison-outcome enum distinguishes matched/not-matched/acquisition-failed" \
  bash -c "grep -q 'Matched,' '$FEATURE/Domain/BiometricComparisonOutcome.cs' && grep -q 'NotMatched,' '$FEATURE/Domain/BiometricComparisonOutcome.cs' && grep -q 'AcquisitionFailed,' '$FEATURE/Domain/BiometricComparisonOutcome.cs'"
check "AC03 an extraction failure is caught and recorded, never left to propagate" \
  bash -c "grep -q 'BiometricComparisonOutcome.AcquisitionFailed' '$FEATURE/Service/FingerprintVerificationService.cs'"
check "AC03 a structural test proves acquisition failure and no-match are distinct" \
  bash -c "grep -q 'AC03_an_empty_live_sample_records_acquisition_failed_not_not_matched' tests/features/bio-bio-004/FingerprintVerificationServiceTests.cs && grep -q 'AC03_a_non_matching_live_sample_records_not_matched_and_denies_access' tests/features/bio-bio-004/FingerprintVerificationServiceTests.cs"
check "BIO-BIO-004-F01 fix: provider extraction failures are caught broadly, not only ArgumentException" \
  bash -c "! grep -q '^        catch (ArgumentException)$' '$FEATURE/Service/FingerprintVerificationService.cs' && grep -q 'catch (Exception)' '$FEATURE/Service/FingerprintVerificationService.cs'"
check "BIO-BIO-004-F01 fix: a genuine caller cancellation is distinguished and propagated, not recorded" \
  bash -c "grep -q 'catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)' '$FEATURE/Service/FingerprintVerificationService.cs'"
check "BIO-BIO-004-F01 regression tests exist for non-ArgumentException provider failures and the cancellation carve-out" \
  bash -c "test -f tests/features/bio-bio-004/FingerprintVerificationAcquisitionFailureTests.cs && grep -q 'A_non_ArgumentException_provider_failure_is_recorded_as_AcquisitionFailed_not_propagated' tests/features/bio-bio-004/FingerprintVerificationAcquisitionFailureTests.cs && grep -q 'A_genuine_caller_cancellation_propagates_and_writes_no_record' tests/features/bio-bio-004/FingerprintVerificationAcquisitionFailureTests.cs"

# --- AC04: suspended/revoked identities cannot be converted into access allow by match alone ---
check "AC04 an identity status gateway contract exists, separate from the service" test -f "$FEATURE/Domain/IIdentityStatusGateway.cs"
check "AC04 the service gates access allow on suspended/revoked status even after a match" \
  bash -c "grep -q 'IdentityStatus.Suspended => \"identity_suspended\"' '$FEATURE/Service/FingerprintVerificationService.cs' && grep -q 'IdentityStatus.Revoked => \"identity_revoked\"' '$FEATURE/Service/FingerprintVerificationService.cs'"
check "AC04 access decision is a type distinct from the raw biometric comparison outcome" \
  bash -c "test -f '$FEATURE/Domain/AccessDecision.cs' && grep -q 'public enum AccessDecision' '$FEATURE/Domain/AccessDecision.cs' && ! grep -q 'public enum AccessDecision' '$FEATURE/Domain/BiometricComparisonOutcome.cs'"
check "AC04 a structural test proves a matched suspended identity is denied while the match is recorded" \
  bash -c "grep -q 'AC04_a_matched_suspended_identity_is_denied_access_but_the_match_is_still_recorded' tests/features/bio-bio-004/FingerprintVerificationServiceTests.cs"
check "AC04 a structural test proves a matched revoked identity is denied while the match is recorded" \
  bash -c "grep -q 'AC04_a_matched_revoked_identity_is_denied_access_but_the_match_is_still_recorded' tests/features/bio-bio-004/FingerprintVerificationServiceTests.cs"
check "AC04 a structural test proves a matched active identity is allowed" \
  bash -c "grep -q 'AC04_a_matched_active_identity_is_allowed_access' tests/features/bio-bio-004/FingerprintVerificationServiceTests.cs"

# --- Governance (BIO-IDN-001-QA-01 shape) ---
check "governance: the record store and its implementation are internal, not public" \
  bash -c "grep -q '^internal interface IFingerprintVerificationRecordStore' '$FEATURE/Domain/IFingerprintVerificationRecordStore.cs' && grep -q '^internal sealed class InMemoryFingerprintVerificationRecordStore' '$FEATURE/Domain/InMemoryFingerprintVerificationRecordStore.cs'"
check "governance: the service is internal too" \
  bash -c "grep -q '^internal sealed class FingerprintVerificationService' '$FEATURE/Service/FingerprintVerificationService.cs'"
check "governance: the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.FingerprintVerification.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.FingerprintVerification.Tests\" />' '$FEATURE/Biolog.Features.FingerprintVerification.csproj'"
check "governance: reflection tests prove the record store/service are inaccessible to ordinary production consumers" \
  bash -c "grep -q 'IFingerprintVerificationRecordStore_and_InMemory_implementation_are_not_public' tests/features/bio-bio-004/FingerprintVerificationGovernanceTests.cs"
check "governance: cross-tenant lookup returns null, not an exception (no enumeration oracle)" \
  bash -c "grep -q 'Cross-tenant lookup returns null' '$FEATURE/Domain/InMemoryFingerprintVerificationRecordStore.cs'"

# --- Feature-to-feature dependency shape ---
check "this project references BIO-BIO-001's provider-neutral biometric library" \
  bash -c "grep -q 'Biolog.Features.Biometrics.csproj' '$FEATURE/Biolog.Features.FingerprintVerification.csproj'"
check "no ProjectReference to BIO-BIO-002 or BIO-IDN-001 (both reached via this story's own gateway)" \
  bash -c "! grep -q '<ProjectReference[^>]*bio-bio-002' '$FEATURE/Biolog.Features.FingerprintVerification.csproj' && ! grep -q '<ProjectReference[^>]*bio-idn-001' '$FEATURE/Biolog.Features.FingerprintVerification.csproj'"
check "module project references stay one-way (no ProjectReference back to Biolog.Api)" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\.Api\.csproj' '$FEATURE/Biolog.Features.FingerprintVerification.csproj'"

# --- Module wiring ---
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioBio004Module.cs && ! test -f '$FEATURE/BioBio004Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioBio004Module()' api/Biolog.Api/Program.cs"
check "no HTTP endpoints are mapped (AC01-AC04 are about the verification/decision model, matching BIO-BIO-001/002/003's deferred-integration call)" \
  bash -c "! grep -q 'endpoints\.Map' api/Biolog.Api/Modules/BioBio004Module.cs"
check "the identity status gateway adapter wraps BIO-IDN-001's own already-registered PersonService" \
  bash -c "grep -q 'PersonService.GetAsync' api/Biolog.Api/Modules/BioBio004Module.cs"

# --- Developer test wiring ---
check "unit tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.FingerprintVerification.csproj' tests/features/bio-bio-004/Biolog.Features.FingerprintVerification.Tests.csproj"
check "docs/features/bio-bio-004/README.md documents the feature" test -f docs/features/bio-bio-004/README.md
check "builder evidence recorded" test -d evidence/features/bio-bio-004/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
