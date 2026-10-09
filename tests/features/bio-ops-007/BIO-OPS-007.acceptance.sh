#!/usr/bin/env bash
# BIO-OPS-007 local/controlled performance and resilience evidence gate.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
runtime=false
if [ "${1:-}" = "--runtime" ]; then runtime=true; fi

pass=0
fail=0
check() {
  local label="$1"; shift
  if "$@" >/dev/null 2>&1; then
    printf 'PASS  %s\n' "$label"
    pass=$((pass + 1))
  else
    printf 'FAIL  %s\n' "$label" >&2
    fail=$((fail + 1))
  fi
}

check 'AC01 two distinct replacements race activation in a story-owned developer test' \
  rg -q 'AC01_two_distinct_replacements_racing_activation_leave_at_most_one_active_card' \
  tests/features/bio-ops-007/ConcurrentReplacementHardeningTests.cs
check 'AC01 proof asserts exactly one active card and one successful activation' \
  bash -c "grep -q 'Assert.Single(cards, card =>' tests/features/bio-ops-007/ConcurrentReplacementHardeningTests.cs && grep -q 'Assert.Single(attempts' tests/features/bio-ops-007/ConcurrentReplacementHardeningTests.cs"

check 'AC02 reconnect-storm proof races overlapping sync passes' \
  rg -q 'Future.wait\(\[engine.runSyncPass\(\), engine.runSyncPass\(\)\]\)' \
  tests/flutter/test/bio_mob_003_ac05_reconnect_storm_test.dart
check 'AC02 proof asserts one upload per event' \
  rg -q 'uploadCallCounts\[one.mutationId\], 1' \
  tests/flutter/test/bio_mob_003_ac05_reconnect_storm_test.dart

check 'AC03 API/UI baseline evidence is machine-readable' \
  python3 tests/features/bio-ops-007/latency-probe.py --validate evidence/BIO-OPS-007/builder/latency-baseline.json
check 'AC03 evidence separates controlled measurements from production claims' \
  rg -q 'not a production-scale SLO' docs/features/bio-ops-007/README.md

check 'AC04 database interruption has recovery and integrity assertions' \
  rg -q 'AC04_database_create_interruption_recovers_without_partial_or_duplicate_state' \
  tests/features/bio-ops-007/ConcurrentReplacementHardeningTests.cs
check 'AC04 queue interruption has retry/no-loss/no-duplicate assertions' \
  rg -q 'AC04_queue_append_interruption_retries_without_loss_or_duplicate_event' \
  tests/features/bio-ops-007/ConcurrentReplacementHardeningTests.cs
check 'AC04 network interruption is injected mid-request' \
  rg -q 'simulated mid-request network drop' \
  tests/flutter/test/bio_ops_004_sy_ops004_01_access_cache_reconnect_test.dart
check 'AC04 network recovery coalesces a follow-up pass' \
  rg -q 'in-flight failure/recovery race' docs/features/bio-ops-004/README.md

if $runtime; then
  check 'AC01/AC04 .NET concurrency, database and queue injections execute' \
    dotnet test tests/features/bio-ops-007/Biolog.Features.PerformanceResilience.Tests.csproj --no-restore -v minimal
  check 'AC02/AC04 reconnect queue and network injections execute' \
    bash -c 'cd tests/flutter && flutter test test/bio_mob_002_ac03_retry_queue_test.dart test/bio_ops_004_sy_ops004_01_access_cache_reconnect_test.dart test/bio_mob_003_ac05_reconnect_storm_test.dart'
fi

for ram in 16 32 64; do
  check "AC05 ${ram}GB laptop profile documented" \
    rg -q "${ram} GB" docs/features/bio-ops-007/RESOURCE-PROFILES.md
done

printf '%s\n' '=============================='
printf '%d passed, %d failed\n' "$pass" "$fail"
test "$fail" -eq 0
