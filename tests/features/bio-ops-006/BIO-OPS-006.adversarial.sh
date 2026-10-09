#!/usr/bin/env bash
set -u
cd "$(git rev-parse --show-toplevel)"

failures=0
pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1"; failures=$((failures + 1)); }

# AC03 says the negative suite passes. Searching source files for words does not execute a suite.
if rg -q 'dotnet test .*Biolog.IntegrationTests' tests/features/bio-ops-006/BIO-OPS-006.acceptance.sh; then
  pass 'AC03 hardening gate executes the cross-tenant integration suite'
else
  fail 'AC03 hardening gate only inspects cross-tenant test source'
fi

# Kemi ruled that no fallback is permitted. Verify both mandatory gates reject
# an environment without gitleaks and name the installation policy clearly.
for scanner in scripts/secret-scan.sh scripts/history-secret-scan.sh; do
  output="$(env PATH="/usr/bin:/bin" bash "$scanner" --all 2>&1)"
  status=$?
  if [ "$status" -ne 0 ] && grep -Fxq \
    'gitleaks is required and not installed - see docs/TOOLCHAIN-POLICY.md' <<<"$output"; then
    pass "AC04 $scanner fails closed when gitleaks is unavailable"
  else
    fail "AC04 $scanner did not fail closed with the required error"
  fi
done

if (( failures > 0 )); then
  printf '%d adversarial invariant(s) failed\n' "$failures"
  exit 1
fi

printf 'PASS: BIO-OPS-006 adversarial invariants satisfied\n'
