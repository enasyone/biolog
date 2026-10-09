#!/usr/bin/env bash
# Developer tests for BIO-FND-001, mapped to acceptance criteria.
# Run from anywhere: tests/foundation/BIO-FND-001.test.sh
#
# NOTE: a formal shared test harness/framework is delivered by BIO-FND-006
# (owner: Seyi). Until then this is a self-contained bash suite so BIO-FND-001
# has developer tests per the story constraints.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

total=0
failed=0

check() {
  local name="$1"; shift
  total=$((total + 1))
  if "$@"; then
    echo "PASS  $name"
  else
    echo "FAIL  $name"
    failed=$((failed + 1))
  fi
}

# --- BIO-FND-001-AC01: documented structure -------------------------------
ac01_structure() {
  for d in web api mobile tests infrastructure docs evidence; do
    [ -d "$d" ] || return 1
  done
}
check "AC01: web/api/mobile/tests/infrastructure/docs/evidence all exist" ac01_structure

# --- BIO-FND-001-AC02: git init, main+develop, no remote ------------------
ac02_branches() {
  git show-ref --verify --quiet refs/heads/main && git show-ref --verify --quiet refs/heads/develop
}
check "AC02: main and develop branches exist" ac02_branches

ac02_no_remote() {
  [ -z "$(git remote)" ]
}
check "AC02: no remote configured" ac02_no_remote

# --- BIO-FND-001-AC03: pre-push guard --------------------------------------
ac03_hooks_path() {
  [ "$(git config --get core.hooksPath)" = ".githooks" ]
}
check "AC03: core.hooksPath is .githooks" ac03_hooks_path

ac03_blocks_when_disabled() {
  # Real repo state: github_push_enabled is false, hook must exit non-zero.
  ! sh .githooks/pre-push >/dev/null 2>&1
}
check "AC03: pre-push hook blocks push while github_push_enabled=false" ac03_blocks_when_disabled

ac03_allows_when_enabled() {
  # Boundary case: hook must permit push once the flag flips true. Exercised
  # against a scratch copy of the state file, never the real one.
  local scratch
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd001-ac03.XXXXXX")"
  mkdir -p "$scratch/governance"
  printf 'github_push_enabled: true\n' > "$scratch/governance/release-state.yaml"
  ( cd "$scratch" && sh "$OLDPWD/.githooks/pre-push" >/dev/null 2>&1 )
  local rc=$?
  rm -rf "$scratch"
  return $rc
}
check "AC03: pre-push hook allows push once github_push_enabled=true" ac03_allows_when_enabled

# --- BIO-FND-001-AC04: no secrets / machine-specific absolute paths --------
ac04_current_tree_clean() {
  scripts/secret-scan.sh --all >/dev/null 2>&1
}
check "AC04: secret scan of tracked files is clean" ac04_current_tree_clean

ac04_scanner_detects_fixture() {
  # Verify the scanner actually catches something, not just that it no-ops.
  # The fabricated key is assembled from two halves so this source line
  # itself never contains the contiguous look-alike pattern.
  local scratch half1 half2
  half1='AKIAABCDE'
  half2='FGHIJKLMNOP'
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd001-ac04.XXXXXX")"
  ( cd "$scratch" \
      && git init -q -b main \
      && printf 'aws_key=%s%s\n' "$half1" "$half2" > secret.txt \
      && git add secret.txt )
  local rc=0
  ( cd "$scratch" && bash "$OLDPWD/scripts/secret-scan.sh" --staged >/dev/null 2>&1 ) && rc=1
  rm -rf "$scratch"
  [ "$rc" -eq 0 ]
}
check "AC04: secret-scan fixture with a fabricated AWS key is rejected" ac04_scanner_detects_fixture

ac04_qa01_secret_in_whitespace_path_is_caught() {
  # Regression test for BIO-FND-001-QA-01 (independent review finding): the
  # fallback scanner used to enumerate tracked files with a whitespace-split
  # `for f in $(git ls-files)`, so a path containing a space (e.g.
  # "docs/leaked secret.txt") was silently never scanned, and
  # validate-bootstrap.sh then returned a false success. The fabricated key
  # is assembled from two halves so this source line itself never contains
  # the contiguous look-alike pattern.
  local scratch half1 half2
  half1='AKIA11112222'
  half2='33334455'
  # TD-001: a fresh clone only creates a local branch for whatever the
  # source's HEAD was, so cloning from a feature worktree (HEAD on
  # feature/BIO-XXX, not main) used to leave `main` missing entirely and
  # break the old `git branch -f develop main`. Create whichever of
  # main/develop isn't already a local branch from its origin counterpart
  # instead of assuming main is checked out.
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd001-qa01.XXXXXX")"
  if ! git clone -q --no-local "$(git rev-parse --show-toplevel)" "$scratch" >/dev/null 2>&1; then
    rm -rf "$scratch"; return 1
  fi
  ( cd "$scratch" \
      && git config user.name QA \
      && git config user.email qa@example.invalid \
      && { git show-ref --verify --quiet refs/heads/main || git branch main origin/main; } \
      && { git show-ref --verify --quiet refs/heads/develop || git branch develop origin/develop; } \
      && git remote remove origin \
      && git config core.hooksPath .githooks \
      && printf 'aws_access_key_id=%s%s\n' "$half1" "$half2" > "docs/leaked secret.txt" \
      && git add "docs/leaked secret.txt" \
      && git commit -qm 'adversarial secret fixture (whitespace path)' ) >/dev/null 2>&1
  local scan_rc=0 validate_rc=0
  ( cd "$scratch" && bash scripts/secret-scan.sh --all >/dev/null 2>&1 ) || scan_rc=1
  ( cd "$scratch" && bash scripts/validate-bootstrap.sh >/dev/null 2>&1 ) || validate_rc=1
  rm -rf "$scratch"
  [ "$scan_rc" -eq 1 ] && [ "$validate_rc" -eq 1 ]
}
check "AC04/AC05 regression BIO-FND-001-QA-01: secret in a path with a space is caught by scan and validator" ac04_qa01_secret_in_whitespace_path_is_caught

ac04_no_absolute_paths_committed() {
  ! git ls-files -z | xargs -0 grep -InE "/(Users|home)/[A-Za-z0-9._-]+/" -- 2>/dev/null \
      | grep -v '^scripts/validate-bootstrap.sh:' \
      | grep -v '^tests/foundation/BIO-FND-001.test.sh:' >/dev/null
}
check "AC04: no machine-specific absolute paths in tracked files" ac04_no_absolute_paths_committed

# --- BIO-FND-001-AC05: bootstrap validation script -------------------------
ac05_validate_script_succeeds() {
  scripts/validate-bootstrap.sh >/dev/null 2>&1
}
check "AC05: scripts/validate-bootstrap.sh returns success" ac05_validate_script_succeeds

echo "=============================="
echo "$((total - failed))/$total checks passed"
[ "$failed" -eq 0 ]
