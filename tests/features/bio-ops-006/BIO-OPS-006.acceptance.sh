#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

mode="${1:-all}"
if [ "$mode" != "all" ] && [ "$mode" != "--cross-tenant-only" ] && [ "$mode" != "--controls-only" ]; then
  echo "usage: $0 [--cross-tenant-only|--controls-only]" >&2
  exit 2
fi

pass=0
fail=0
check() {
  local label="$1"; shift
  if "$@"; then
    echo "PASS  $label"
    pass=$((pass + 1))
  else
    echo "FAIL  $label" >&2
    fail=$((fail + 1))
  fi
}

run_cross_tenant_suite() {
  local filter
  local results_dir
  local trx
  filter='FullyQualifiedName=Biolog.IntegrationTests.TenantContextHostChannelTests.An_unknown_host_fails_closed_as_host_not_recognised_through_the_real_packaged_pipeline'
  filter+='|FullyQualifiedName=Biolog.IntegrationTests.PersonHostChannelTests.A_person_registered_under_one_tenant_s_verified_host_is_not_reachable_through_another_tenant_s'
  filter+='|FullyQualifiedName=Biolog.IntegrationTests.PhotographyHostChannelTests.BIOPHO001_a_tenant_B_caller_cannot_append_a_master_for_a_real_tenant_A_person'
  filter+='|FullyQualifiedName=Biolog.IntegrationTests.CardLifecycleHostChannelTests.TDCRD006_a_device_genuinely_registered_in_tenant_A_cannot_authenticate_tenant_Bs_deny_list'
  filter+='|FullyQualifiedName=Biolog.IntegrationTests.CardLifecycleHostChannelTests.A_tenant_B_caller_cannot_report_a_real_tenant_A_persons_card'
  filter+='|FullyQualifiedName=Biolog.IntegrationTests.WebhookEndpointsTests.Create_with_no_resolved_tenant_is_rejected'
  filter+='|FullyQualifiedName=Biolog.IntegrationTests.OfflineSyncHostChannelTests.A_payload_tenant_id_claim_that_disagrees_with_the_devices_real_registered_tenant_is_rejected'

  # No source-presence fallback is allowed. Build/test discovery failure, an
  # unavailable runner, zero selected tests, or any failing body is nonzero.
  results_dir="$(mktemp -d "${TMPDIR:-/tmp}/bio-ops-006-trx.XXXXXX")"
  trx="$results_dir/cross-tenant.trx"
  if ! DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj \
    --disable-build-servers --nologo --verbosity minimal -m:1 --filter "$filter" \
    --logger "trx;LogFileName=cross-tenant.trx" --results-directory "$results_dir"; then
    rm -rf "$results_dir"
    return 1
  fi

  # `dotnet test` can exit zero when a filter matches no tests. Require all
  # seven named negatives to have executed and passed in the machine result.
  if ! grep -Eq '<Counters total="7" executed="7" passed="7" failed="0"' "$trx"; then
    echo "BIO-OPS-006: expected seven executed/passing cross-tenant tests" >&2
    rm -rf "$results_dir"
    return 1
  fi
  rm -rf "$results_dir"
}

if [ "$mode" = "--cross-tenant-only" ]; then
  run_cross_tenant_suite
  exit $?
fi

threat_model="docs/features/bio-ops-006/THREAT-MODEL.md"

for area in authentication authorization tenant-routing biometrics card-lifecycle document-parsing webhooks offline-caches; do
  check "AC01 threat model covers $area" grep -q "BIO-OPS-006-THREAT:$area" "$threat_model"
done

check "AC02 ledger has no explicitly open Critical/High defect" bash -c \
  "! grep -Ei 'Severity/status:.*(Critical|High).*(—|-).*Open[.]?$' docs/SECURITY-FIXES.md"

if [ "$mode" = "all" ]; then
  check "AC03 executed cross-tenant negative suite passes" run_cross_tenant_suite
fi

check "AC04 current-tree secret scan passes" bash scripts/secret-scan.sh --all
check "AC04 Git-history secret scan passes" bash scripts/history-secret-scan.sh

# AC05: production logging is intentionally tiny. Every direct application log
# call must pass values through BIO-FND-009's explicit SafeLogValue boundary;
# raw secret/biometric/document payload vocabulary must never occur at a sink.
check "AC05 all application log arguments use SafeLogValue" bash -c '
  files=$(rg -l "\.Log(Trace|Debug|Information|Warning|Error|Critical)\(" api --glob "*.cs" --glob "!**/obj/**" --glob "!**/bin/**")
  test -n "$files"
  ! rg -n "\.Log(Trace|Debug|Information|Warning|Error|Critical)\(" $files | grep -v "InMemoryAuditSink.cs\|InMemoryCertificationAuditSink.cs"
  test "$(rg -c "SafeLogValue\.Of" api/Biolog.Api/Audit/InMemoryAuditSink.cs api/Biolog.Api/Features/DeviceCertification/Audit/InMemoryCertificationAuditSink.cs | awk -F: "{s+=\$2} END {print s}")" -ge 12
'
check "AC05 no production console/debug writes" bash -c \
  '! rg -n "(Console|Debug)\.(Write|WriteLine)" api mobile web --glob "*.cs" --glob "*.dart" --glob "*.ts" --glob "*.tsx" --glob "!**/obj/**" --glob "!**/bin/**"'
check "AC05 no sensitive vocabulary in direct log templates" bash -c \
  '! rg -n -i "\.Log(Trace|Debug|Information|Warning|Error|Critical)\([^\n]*(password|secret|token|cookie|biometric|fingerprint|face|photo|raw.?value|raw.?payload|document.?content)" api --glob "*.cs" --glob "!**/obj/**" --glob "!**/bin/**"'

echo "=============================="
echo "$pass passed, $fail failed"
test "$fail" -eq 0
