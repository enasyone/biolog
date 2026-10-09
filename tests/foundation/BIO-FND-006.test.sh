#!/usr/bin/env bash
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

check "AC01 .NET unit test project" test -f tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj
check "AC01 .NET integration test project" test -f tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
check "AC01 .NET projects marked as test projects" bash -c 'grep -q "<IsTestProject>true</IsTestProject>" tests/dotnet/Biolog.UnitTests/*.csproj && grep -q "<IsTestProject>true</IsTestProject>" tests/dotnet/Biolog.IntegrationTests/*.csproj'
check "AC02 frontend unit project and test" bash -c 'grep -q "test:unit" tests/frontend/package.json && test -f tests/frontend/vitest.config.mjs && test -f tests/frontend/unit/baseline.test.js'
check "AC02 Playwright project and test" bash -c 'grep -q "@playwright/test" tests/frontend/package.json && test -f tests/frontend/playwright.config.mjs && test -f tests/frontend/playwright/baseline.spec.js'
check "AC03 Flutter scaffold" bash -c 'grep -q "flutter_test" tests/flutter/pubspec.yaml && test -f tests/flutter/test/baseline_test.dart'
check "AC04 security commands documented" bash -c 'grep -q "secret-scan.sh --all" docs/TESTING.md && grep -q "package --vulnerable" docs/TESTING.md && grep -q "pnpm --dir tests/frontend audit" docs/TESTING.md && grep -q "flutter pub outdated" docs/TESTING.md'
check "AC04 all fixtures marked synthetic" bash -c 'grep -Rq "SYNTHETIC" tests/dotnet tests/frontend tests/flutter'
check "AC05 single quality command executable" test -x scripts/quality.sh
check "AC05 quality command orchestrates every ecosystem" bash -c 'grep -q "dotnet test" scripts/quality.sh && grep -q "test:unit" scripts/quality.sh && grep -q "test:e2e" scripts/quality.sh && grep -q "flutter test" scripts/quality.sh'

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
