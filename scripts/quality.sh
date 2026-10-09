#!/usr/bin/env bash
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

full=0
if [ "${1:-}" = "--full" ]; then
  full=1
elif [ "$#" -gt 0 ]; then
  echo "usage: scripts/quality.sh [--full]" >&2
  exit 2
fi

fail=0
run() {
  local label="$1"; shift
  echo "== $label =="
  if "$@"; then echo "PASS  $label"; else echo "FAIL  $label" >&2; fail=1; fi
}

run "BIO-FND-006 foundation harness" bash tests/foundation/BIO-FND-006.test.sh
run "BIO-ARC-001 contract harness" bash tests/foundation/BIO-ARC-001.test.sh
run "BIO-ARC-002 contract harness" bash tests/foundation/BIO-ARC-002.test.sh
run "BIO-ARC-003 contract harness" bash tests/foundation/BIO-ARC-003.test.sh
run "BIO-ARC-004 contract harness" bash tests/foundation/BIO-ARC-004.test.sh
run "BIO-ARC-005 contract harness" bash tests/foundation/BIO-ARC-005.test.sh
run "BIO-ARC-006 contract freeze harness" bash tests/foundation/BIO-ARC-006.test.sh
run "BIO-FND-007 web shell harness" bash tests/foundation/BIO-FND-007.test.sh
run "BIO-FND-008 api-shell harness" bash tests/foundation/BIO-FND-008.test.sh
run "BIO-FND-009 observability harness" bash tests/foundation/BIO-FND-009.test.sh
run "BIO-FND-010 traceability harness" bash tests/foundation/BIO-FND-010.test.sh
run "BIO-DEV-001 device registry harness" bash tests/foundation/BIO-DEV-001.test.sh
run "BIO-DEV-002 device certification harness" bash tests/foundation/BIO-DEV-002.test.sh
run "BIO-DOC-001 document-session harness" bash tests/foundation/BIO-DOC-001.test.sh
run "BIO-DOC-002 mrz-engine harness" bash tests/foundation/BIO-DOC-002.test.sh
run "BIO-DOC-003 barcode-engine harness" bash tests/foundation/BIO-DOC-003.test.sh
run "BIO-DOC-004 ocr-engine harness" bash tests/foundation/BIO-DOC-004.test.sh
run "BIO-DOC-005 parser-registry harness" bash tests/foundation/BIO-DOC-005.test.sh
run "BIO-DOC-006 document-classifier harness" bash tests/foundation/BIO-DOC-006.test.sh
run "BIO-DOC-007 nfc-reader harness" bash tests/foundation/BIO-DOC-007.test.sh
run "BIO-DOC-008 evidence-comparison harness" bash tests/foundation/BIO-DOC-008.test.sh
run "BIO-DOC-009 authenticity-assessment harness" bash tests/foundation/BIO-DOC-009.test.sh
run "BIO-DOC-010 document-template-pack harness" bash tests/foundation/BIO-DOC-010.test.sh
run "BIO-TEN-001 tenant registry harness" bash tests/features/bio-ten-001/BIO-TEN-001.test.sh
run "BIO-TEN-002 tenant domain harness" bash tests/features/bio-ten-002/BIO-TEN-002.test.sh
run "BIO-CRD-001 brand assets harness" bash tests/foundation/BIO-CRD-001.test.sh
run "BIO-IDN-001 universal PERSON harness" bash tests/features/bio-idn-001/BIO-IDN-001.test.sh
run "BIO-IDN-002 person profile harness" bash tests/features/bio-idn-002/BIO-IDN-002.test.sh
run "BIO-IDN-003 institutional identifier harness" bash tests/features/bio-idn-003/BIO-IDN-003.test.sh
run "BIO-IDN-004 identity lifecycle harness" bash tests/features/bio-idn-004/BIO-IDN-004.test.sh
run "BIO-IDN-005 duplicate review harness" bash tests/features/bio-idn-005/BIO-IDN-005.test.sh
run "BIO-IDN-006 RBAC harness" bash tests/features/bio-idn-006/BIO-IDN-006.test.sh
run "BIO-BIO-001 biometric provider abstraction harness" bash tests/features/bio-bio-001/BIO-BIO-001.test.sh
run "BIO-PHO-001 photography harness" bash tests/features/bio-pho-001/BIO-PHO-001.test.sh
run "BIO-PHO-002 photo booth harness" bash tests/features/bio-pho-002/BIO-PHO-002.test.sh
run "BIO-BIO-002 fingerprint enrolment harness" bash tests/features/bio-bio-002/BIO-BIO-002.test.sh
run "BIO-CRD-002 card designer harness" bash tests/features/bio-crd-002/BIO-CRD-002.test.sh
run "BIO-BIO-003 face enrolment harness" bash tests/features/bio-bio-003/BIO-BIO-003.test.sh
run "BIO-CRD-003 card typography harness" bash tests/features/bio-crd-003/BIO-CRD-003.test.sh
run "BIO-CRD-004 card objects harness" bash tests/features/bio-crd-004/BIO-CRD-004.test.sh
run "BIO-BIO-004 fingerprint verification harness" bash tests/features/bio-bio-004/BIO-BIO-004.test.sh
run "BIO-CRD-005 card issuance harness" bash tests/features/bio-crd-005/BIO-CRD-005.test.sh
run "BIO-MOB-001 mobile shell harness" bash tests/foundation/BIO-MOB-001.test.sh
run "BIO-MOB-002 offline queue harness" bash tests/foundation/BIO-MOB-002.test.sh
run "BIO-MOB-003 offline sync harness" bash tests/foundation/BIO-MOB-003.test.sh
run "BIO-BIO-005 face verification harness" bash tests/features/bio-bio-005/BIO-BIO-005.test.sh
run "BIO-BIO-006 fingerprint search harness" bash tests/features/bio-bio-006/BIO-BIO-006.test.sh
run "BIO-CRD-006 card lifecycle harness" bash tests/features/bio-crd-006/BIO-CRD-006.test.sh
run "BIO-BIO-007 face search harness" bash tests/features/bio-bio-007/BIO-BIO-007.test.sh
run "BIO-ACC-001 access policy harness" bash tests/features/bio-acc-001/BIO-ACC-001.test.sh
run "BIO-CRD-007 card production harness" bash tests/features/bio-crd-007/BIO-CRD-007.test.sh
run "BIO-MOB-003 offline sync central intake harness" bash tests/features/bio-mob-003/BIO-MOB-003.test.sh
run "BIO-DEV-003 device adapter framework harness" bash tests/features/bio-dev-003/BIO-DEV-003.test.sh
run "BIO-ATT-001 attendance event core harness" bash tests/features/bio-att-001/BIO-ATT-001.test.sh
run "BIO-RPT-001 notification event/rule foundation harness" bash tests/features/bio-rpt-001/BIO-RPT-001.test.sh
run "BIO-RPT-002 governed report builder harness" bash tests/features/bio-rpt-002/BIO-RPT-002.test.sh
run "BIO-CRD-008 card-template lifecycle harness" bash tests/features/bio-crd-008/BIO-CRD-008.test.sh
run "BIO-RPT-003 audit investigation view harness" bash tests/features/bio-rpt-003/BIO-RPT-003.test.sh
run "BIO-VIS-001 visitor preregistration and host approval harness" bash tests/features/bio-vis-001/BIO-VIS-001.test.sh
run "BIO-TRN-001 routes, buses and person-assignment model harness" bash tests/features/bio-trn-001/BIO-TRN-001.test.sh
run "BIO-TRN-002 boarding/alighting and bus-presence events harness" bash tests/features/bio-trn-002/BIO-TRN-002.test.sh
run "BIO-MOB-005 limited offline enrolment harness" bash tests/foundation/BIO-MOB-005.test.sh
run "BIO-ACC-002 online access decision harness" bash tests/features/bio-acc-002/BIO-ACC-002.test.sh
run "BIO-INT-001 authentication provider abstraction harness" bash tests/features/bio-int-001/BIO-INT-001.test.sh
run "BIO-MOB-004 mobile verification flows harness" bash tests/foundation/BIO-MOB-004.test.sh
run "BIO-DOC-011 document-template editor harness" bash tests/foundation/BIO-DOC-011.test.sh
run "BIO-OPS-001 local enrolment acceptance harness" bash tests/operations/BIO-OPS-001.test.sh
run "BIO-OPS-006 security and tenant-isolation hardening harness" bash tests/features/bio-ops-006/BIO-OPS-006.acceptance.sh
run "BIO-OPS-006 hardening mutation probes" bash tests/features/bio-ops-006/BIO-OPS-006.mutation.sh
run "implemented-story traceability inventory" python3 scripts/check-traceability.py --all
run "module project reference direction" bash scripts/check-module-project-references.sh
run "tracked secret scan" bash scripts/secret-scan.sh --all

if [ "$full" -eq 1 ]; then
  run ".NET unit tests" dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj --nologo
  run ".NET integration tests" dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj --nologo
  run ".NET document-verify unit tests" dotnet test tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/Biolog.Features.DocumentVerify.Tests.csproj --nologo
  run ".NET biometric-provider unit tests" dotnet test tests/features/bio-bio-001/Biolog.Features.Biometrics.Tests.csproj --nologo
  run ".NET mrz-engine unit tests" dotnet test tests/features/bio-doc-002/Biolog.Features.MrzEngine.Tests/Biolog.Features.MrzEngine.Tests.csproj --nologo
  run ".NET photography unit tests" dotnet test tests/features/bio-pho-001/Biolog.Features.Photography.Tests.csproj --nologo
  run ".NET photo booth unit tests" dotnet test tests/features/bio-pho-002/Biolog.Features.PhotoBooth.Tests.csproj --nologo
  run ".NET barcode-engine unit tests" dotnet test tests/features/bio-doc-003/Biolog.Features.BarcodeEngine.Tests/Biolog.Features.BarcodeEngine.Tests.csproj --nologo
  run ".NET fingerprint-enrolment unit tests" dotnet test tests/features/bio-bio-002/Biolog.Features.FingerprintEnrolment.Tests.csproj --nologo
  run ".NET ocr-engine unit tests" dotnet test tests/features/bio-doc-004/Biolog.Features.OcrEngine.Tests/Biolog.Features.OcrEngine.Tests.csproj --nologo
  run ".NET card designer unit tests" dotnet test tests/features/bio-crd-002/Biolog.Features.CardDesigner.Tests.csproj --nologo
  run ".NET face-enrolment unit tests" dotnet test tests/features/bio-bio-003/Biolog.Features.FaceEnrolment.Tests.csproj --nologo
  run ".NET parser-registry unit tests" dotnet test tests/features/bio-doc-005/Biolog.Features.ParserRegistry.Tests/Biolog.Features.ParserRegistry.Tests.csproj --nologo
  run ".NET document-classifier unit tests" dotnet test tests/features/bio-doc-006/Biolog.Features.DocumentClassifier.Tests/Biolog.Features.DocumentClassifier.Tests.csproj --nologo
  run ".NET nfc-reader unit tests" dotnet test tests/features/bio-doc-007/Biolog.Features.NfcReader.Tests/Biolog.Features.NfcReader.Tests.csproj --nologo
  run ".NET evidence-comparison unit tests" dotnet test tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/Biolog.Features.EvidenceComparison.Tests.csproj --nologo
  run ".NET authenticity-assessment unit tests" dotnet test tests/features/bio-doc-009/Biolog.Features.AuthenticityAssessment.Tests/Biolog.Features.AuthenticityAssessment.Tests.csproj --nologo
  run ".NET document-template-pack unit tests" dotnet test tests/features/bio-doc-010/Biolog.Features.DocumentTemplatePack.Tests/Biolog.Features.DocumentTemplatePack.Tests.csproj --nologo
  run ".NET card typography unit tests" dotnet test tests/features/bio-crd-003/Biolog.Features.CardTypography.Tests.csproj --nologo
  run ".NET card objects unit tests" dotnet test tests/features/bio-crd-004/Biolog.Features.CardObjects.Tests.csproj --nologo
  run ".NET fingerprint-verification unit tests" dotnet test tests/features/bio-bio-004/Biolog.Features.FingerprintVerification.Tests.csproj --nologo
  run ".NET card issuance unit tests" dotnet test tests/features/bio-crd-005/Biolog.Features.CardIssuance.Tests.csproj --nologo
  run ".NET face-verification unit tests" dotnet test tests/features/bio-bio-005/Biolog.Features.FaceVerification.Tests.csproj --nologo
  run ".NET fingerprint-search unit tests" dotnet test tests/features/bio-bio-006/Biolog.Features.FingerprintSearch.Tests.csproj --nologo
  run ".NET card lifecycle unit tests" dotnet test tests/features/bio-crd-006/Biolog.Features.CardLifecycle.Tests.csproj --nologo
  run ".NET face-search unit tests" dotnet test tests/features/bio-bio-007/Biolog.Features.FaceSearch.Tests.csproj --nologo
  run ".NET access-policy unit tests" dotnet test tests/features/bio-acc-001/Biolog.Features.AccessPolicy.Tests.csproj --nologo
  run ".NET card production unit tests" dotnet test tests/features/bio-crd-007/Biolog.Features.CardProduction.Tests.csproj --nologo
  run ".NET offline sync unit tests" dotnet test tests/features/bio-mob-003/Biolog.Features.OfflineSync.Tests.csproj --nologo
  run ".NET device adapter framework unit tests" dotnet test tests/features/bio-dev-003/Biolog.Features.DeviceAdapters.Tests.csproj --nologo
  run ".NET attendance unit tests" dotnet test tests/features/bio-att-001/Biolog.Features.Attendance.Tests.csproj --nologo
  run ".NET notification foundation unit tests" dotnet test tests/features/bio-rpt-001/Biolog.Features.Notifications.Tests.csproj --nologo
  run ".NET report builder unit tests" dotnet test tests/features/bio-rpt-002/Biolog.Features.ReportBuilder.Tests.csproj --nologo
  run ".NET card-template lifecycle unit tests" dotnet test tests/features/bio-crd-008/Biolog.Features.CardTemplates.Tests.csproj --nologo
  run ".NET audit investigation unit tests" dotnet test tests/features/bio-rpt-003/Biolog.Features.AuditInvestigation.Tests.csproj --nologo
  run ".NET visitor preregistration unit tests" dotnet test tests/features/bio-vis-001/Biolog.Features.Visitors.Tests.csproj --nologo
  run ".NET access cache unit tests" dotnet test tests/features/bio-acc-003/Biolog.Features.AccessCache.Tests.csproj --nologo
  run ".NET transport unit tests" dotnet test tests/features/bio-trn-001/Biolog.Features.Transport.Tests.csproj --nologo
  run ".NET transport events unit tests" dotnet test tests/features/bio-trn-002/Biolog.Features.TransportEvents.Tests.csproj --nologo
  run ".NET offline enrolment unit tests" dotnet test tests/features/bio-mob-005/Biolog.Features.OfflineEnrolment.Tests.csproj --nologo
  run ".NET access-decision unit tests" dotnet test tests/features/bio-acc-002/Biolog.Features.AccessDecisions.Tests.csproj --nologo
  run ".NET authentication provider abstraction unit tests" dotnet test tests/features/bio-int-001/Biolog.Features.Authentication.Tests.csproj --nologo
  run ".NET mobile verification unit tests" dotnet test tests/features/bio-mob-004/Biolog.Features.MobileVerification.Tests.csproj --nologo
  run ".NET document-template editor unit tests" dotnet test tests/features/bio-doc-011/Biolog.Features.DocumentTemplateEditor.Tests/Biolog.Features.DocumentTemplateEditor.Tests.csproj --nologo
  run ".NET document-verify coordinator unit tests" dotnet test tests/features/bio-ops-005/Biolog.Features.DocumentVerifyCoordination.Tests/Biolog.Features.DocumentVerifyCoordination.Tests.csproj --nologo
  run ".NET vulnerability audit" bash scripts/check-dotnet-vulnerabilities.sh \
    api/Biolog.Api/Biolog.Api.csproj \
    tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj \
    tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj \
    api/features/bio-doc-001/Biolog.Features.DocumentVerify/Biolog.Features.DocumentVerify.csproj \
    tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/Biolog.Features.DocumentVerify.Tests.csproj \
    api/features/bio-bio-001/Biolog.Features.Biometrics/Biolog.Features.Biometrics.csproj \
    tests/features/bio-bio-001/Biolog.Features.Biometrics.Tests.csproj \
    api/features/bio-doc-002/Biolog.Features.MrzEngine/Biolog.Features.MrzEngine.csproj \
    tests/features/bio-doc-002/Biolog.Features.MrzEngine.Tests/Biolog.Features.MrzEngine.Tests.csproj \
    api/features/bio-pho-001/Biolog.Features.Photography.csproj \
    tests/features/bio-pho-001/Biolog.Features.Photography.Tests.csproj \
    api/features/bio-pho-002/Biolog.Features.PhotoBooth.csproj \
    tests/features/bio-pho-002/Biolog.Features.PhotoBooth.Tests.csproj \
    api/features/bio-doc-003/Biolog.Features.BarcodeEngine/Biolog.Features.BarcodeEngine.csproj \
    tests/features/bio-doc-003/Biolog.Features.BarcodeEngine.Tests/Biolog.Features.BarcodeEngine.Tests.csproj \
    api/features/bio-bio-002/Biolog.Features.FingerprintEnrolment/Biolog.Features.FingerprintEnrolment.csproj \
    tests/features/bio-bio-002/Biolog.Features.FingerprintEnrolment.Tests.csproj \
    api/features/bio-doc-004/Biolog.Features.OcrEngine/Biolog.Features.OcrEngine.csproj \
    tests/features/bio-doc-004/Biolog.Features.OcrEngine.Tests/Biolog.Features.OcrEngine.Tests.csproj \
    api/features/bio-crd-002/Biolog.Features.CardDesigner.csproj \
    tests/features/bio-crd-002/Biolog.Features.CardDesigner.Tests.csproj \
    api/features/bio-bio-003/Biolog.Features.FaceEnrolment/Biolog.Features.FaceEnrolment.csproj \
    tests/features/bio-bio-003/Biolog.Features.FaceEnrolment.Tests.csproj \
    api/features/bio-doc-005/Biolog.Features.ParserRegistry/Biolog.Features.ParserRegistry.csproj \
    tests/features/bio-doc-005/Biolog.Features.ParserRegistry.Tests/Biolog.Features.ParserRegistry.Tests.csproj \
    api/features/bio-crd-003/Biolog.Features.CardTypography.csproj \
    tests/features/bio-crd-003/Biolog.Features.CardTypography.Tests.csproj \
    api/features/bio-crd-004/Biolog.Features.CardObjects.csproj \
    tests/features/bio-crd-004/Biolog.Features.CardObjects.Tests.csproj \
    api/features/bio-bio-004/Biolog.Features.FingerprintVerification/Biolog.Features.FingerprintVerification.csproj \
    tests/features/bio-bio-004/Biolog.Features.FingerprintVerification.Tests.csproj \
    api/features/bio-crd-005/Biolog.Features.CardIssuance.csproj \
    tests/features/bio-crd-005/Biolog.Features.CardIssuance.Tests.csproj \
    api/features/bio-doc-006/Biolog.Features.DocumentClassifier/Biolog.Features.DocumentClassifier.csproj \
    tests/features/bio-doc-006/Biolog.Features.DocumentClassifier.Tests/Biolog.Features.DocumentClassifier.Tests.csproj \
    api/features/bio-bio-005/Biolog.Features.FaceVerification/Biolog.Features.FaceVerification.csproj \
    tests/features/bio-bio-005/Biolog.Features.FaceVerification.Tests.csproj \
    api/features/bio-bio-006/Biolog.Features.FingerprintSearch/Biolog.Features.FingerprintSearch.csproj \
    tests/features/bio-bio-006/Biolog.Features.FingerprintSearch.Tests.csproj \
    api/features/bio-doc-007/Biolog.Features.NfcReader/Biolog.Features.NfcReader.csproj \
    tests/features/bio-doc-007/Biolog.Features.NfcReader.Tests/Biolog.Features.NfcReader.Tests.csproj \
    api/features/bio-crd-006/Biolog.Features.CardLifecycle.csproj \
    tests/features/bio-crd-006/Biolog.Features.CardLifecycle.Tests.csproj \
    api/features/bio-bio-007/Biolog.Features.FaceSearch/Biolog.Features.FaceSearch.csproj \
    tests/features/bio-bio-007/Biolog.Features.FaceSearch.Tests.csproj \
    api/features/bio-acc-001/Biolog.Features.AccessPolicy.csproj \
    tests/features/bio-acc-001/Biolog.Features.AccessPolicy.Tests.csproj \
    api/features/bio-doc-008/Biolog.Features.EvidenceComparison/Biolog.Features.EvidenceComparison.csproj \
    tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/Biolog.Features.EvidenceComparison.Tests.csproj \
    api/features/bio-crd-007/Biolog.Features.CardProduction.csproj \
    tests/features/bio-crd-007/Biolog.Features.CardProduction.Tests.csproj \
    api/features/bio-mob-003/Biolog.Features.OfflineSync.csproj \
    tests/features/bio-mob-003/Biolog.Features.OfflineSync.Tests.csproj \
    api/features/bio-doc-009/Biolog.Features.AuthenticityAssessment/Biolog.Features.AuthenticityAssessment.csproj \
    tests/features/bio-doc-009/Biolog.Features.AuthenticityAssessment.Tests/Biolog.Features.AuthenticityAssessment.Tests.csproj \
    api/features/bio-doc-010/Biolog.Features.DocumentTemplatePack/Biolog.Features.DocumentTemplatePack.csproj \
    tests/features/bio-doc-010/Biolog.Features.DocumentTemplatePack.Tests/Biolog.Features.DocumentTemplatePack.Tests.csproj \
    api/features/bio-dev-003/Biolog.Features.DeviceAdapters.csproj \
    tests/features/bio-dev-003/Biolog.Features.DeviceAdapters.Tests.csproj \
    api/features/bio-att-001/Biolog.Features.Attendance.csproj \
    tests/features/bio-att-001/Biolog.Features.Attendance.Tests.csproj \
    api/features/bio-rpt-001/Biolog.Features.Notifications.csproj \
    tests/features/bio-rpt-001/Biolog.Features.Notifications.Tests.csproj \
    api/features/bio-vis-001/Biolog.Features.Visitors.csproj \
    tests/features/bio-vis-001/Biolog.Features.Visitors.Tests.csproj \
    api/features/bio-acc-003/Biolog.Features.AccessCache.csproj \
    tests/features/bio-acc-003/Biolog.Features.AccessCache.Tests.csproj \
    api/features/bio-trn-001/Biolog.Features.Transport.csproj \
    tests/features/bio-trn-001/Biolog.Features.Transport.Tests.csproj \
    api/features/bio-trn-002/Biolog.Features.TransportEvents.csproj \
    tests/features/bio-trn-002/Biolog.Features.TransportEvents.Tests.csproj \
    api/features/bio-mob-005/Biolog.Features.OfflineEnrolment.csproj \
    tests/features/bio-mob-005/Biolog.Features.OfflineEnrolment.Tests.csproj \
    api/features/bio-acc-002/Biolog.Features.AccessDecisions.csproj \
    tests/features/bio-acc-002/Biolog.Features.AccessDecisions.Tests.csproj \
    api/features/bio-int-001/Biolog.Features.Authentication.csproj \
    tests/features/bio-int-001/Biolog.Features.Authentication.Tests.csproj \
    api/features/bio-mob-004/Biolog.Features.MobileVerification.csproj \
    tests/features/bio-mob-004/Biolog.Features.MobileVerification.Tests.csproj
  run "frontend unit tests" pnpm --dir tests/frontend test:unit
  run "Playwright tests" pnpm --dir tests/frontend test:e2e
  run "Flutter tests" bash -c 'cd tests/flutter && flutter test'
fi

if [ "$fail" -eq 0 ]; then
  echo "QUALITY: PASS"
  exit 0
fi
echo "QUALITY: FAIL" >&2
exit 1
