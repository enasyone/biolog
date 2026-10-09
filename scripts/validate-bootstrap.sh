#!/usr/bin/env bash
# BIOLOG bootstrap validation (BIO-FND-001-AC05).
# Verifies AC01-AC04 for this repository and exits 0 only when every check
# passes. Run from anywhere inside the repository:
#   scripts/validate-bootstrap.sh
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

fail=0
pass() { echo "PASS  $1"; }
bad()  { echo "FAIL  $1"; fail=1; }

echo "== BIO-FND-001-AC01: documented directory structure =="
required_dirs=(web api mobile tests infrastructure docs evidence scripts .githooks governance)
for d in "${required_dirs[@]}"; do
  if [ -d "$d" ]; then
    pass "directory '$d' exists"
  else
    bad "directory '$d' is missing"
  fi
done

echo "== BIO-FND-001-AC02: git initialised, main + develop, no remote =="
if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  pass "git repository initialised"
else
  bad "not a git repository"
fi
for b in main develop; do
  if git show-ref --verify --quiet "refs/heads/$b"; then
    pass "branch '$b' exists"
  else
    bad "branch '$b' is missing"
  fi
done
remote_count="$(git remote | wc -l | tr -d ' ')"
if [ "$remote_count" -eq 0 ]; then
  pass "no git remote configured"
else
  bad "unexpected git remote(s) configured: $(git remote)"
fi

echo "== BIO-FND-001-AC03: pre-push guard blocks push while disabled =="
if [ -x .githooks/pre-push ]; then
  pass ".githooks/pre-push is present and executable"
else
  bad ".githooks/pre-push is missing or not executable"
fi
hooks_path="$(git config --get core.hooksPath || true)"
if [ "$hooks_path" = ".githooks" ]; then
  pass "core.hooksPath is set to .githooks"
else
  bad "core.hooksPath is '$hooks_path', expected '.githooks'"
fi
if grep -Eq '^github_push_enabled:[[:space:]]*true[[:space:]]*$' governance/release-state.yaml 2>/dev/null; then
  bad "governance/release-state.yaml has github_push_enabled: true (must stay false before W15)"
else
  prepush_log="$(mktemp "${TMPDIR:-/tmp}/biolog-prepush-check.XXXXXX")"
  if sh .githooks/pre-push >"$prepush_log" 2>&1; then
    bad "pre-push hook exited 0 while push should be blocked"
  else
    pass "pre-push hook blocks push while github_push_enabled is false"
  fi
  rm -f "$prepush_log"
fi

echo "== BIO-FND-001-AC04: no secrets or machine-specific absolute paths =="
if scripts/secret-scan.sh --all; then
  pass "secret scan clean"
else
  bad "secret scan reported findings"
fi
# Matches home-directory-style absolute paths (e.g. /Users/<name>/... or
# /home/<name>/...) in tracked file contents. The pattern is split so this
# check does not flag its own source line.
abs_path_hits="$(git ls-files -z | xargs -0 grep -InE "/(Users|home)/[A-Za-z0-9._-]+/" -- 2>/dev/null | grep -v '^scripts/validate-bootstrap.sh:' || true)"
if [ -z "$abs_path_hits" ]; then
  pass "no machine-specific absolute paths found in tracked files"
else
  bad "machine-specific absolute path(s) found:"
  echo "$abs_path_hits" >&2
fi

echo "=============================="
if [ "$fail" -eq 0 ]; then
  echo "BOOTSTRAP VALIDATION: SUCCESS"
  exit 0
else
  echo "BOOTSTRAP VALIDATION: FAILED"
  exit 1
fi
