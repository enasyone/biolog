#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

pass=0
fail=0
check() {
  local label="$1"
  shift
  if "$@"; then
    echo "PASS  $label"
    pass=$((pass + 1))
  else
    echo "FAIL  $label" >&2
    fail=$((fail + 1))
  fi
}

check "AC04 repository has no configured remote dependency" bash -c 'test -z "$(git remote)"'
check "AC01-AC05 executable journey, ownership, and blocker suite passes" \
  pnpm --dir tests/frontend exec vitest run unit/bio-ops-001-enrolment-journey.test.ts
check "story documentation maps every AC to executable evidence" \
  bash -c 'for ac in AC01 AC02 AC03 AC04 AC05; do grep -q "BIO-OPS-001-$ac" docs/features/bio-ops-001/README.md || exit 1; done'

echo "=============================="
echo "$pass passed, $fail failed"
test "$fail" -eq 0
