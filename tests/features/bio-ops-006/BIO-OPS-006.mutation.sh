#!/usr/bin/env bash
# Adversarial self-tests for the two controls Seyi found could falsely pass.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

pass=0
fail=0
check_rejected() {
  local label="$1"; shift
  if "$@" >/dev/null 2>&1; then
    echo "FAIL  $label" >&2
    fail=$((fail + 1))
  else
    echo "PASS  $label"
    pass=$((pass + 1))
  fi
}

# Mutant 1: the runtime exists but the selected negative suite fails. The gate
# must propagate the runner's nonzero result; matching source text is irrelevant.
dotnet_shim="$(mktemp -d "${TMPDIR:-/tmp}/bio-ops-006-dotnet.XXXXXX")"
gitleaks_probe="$(mktemp -d "${TMPDIR:-/tmp}/bio-ops-006-gitleaks.XXXXXX")"
trap 'rm -rf "$dotnet_shim" "$gitleaks_probe"' EXIT
printf '#!/usr/bin/env bash\nexit 17\n' >"$dotnet_shim/dotnet"
chmod +x "$dotnet_shim/dotnet"
check_rejected "failing cross-tenant test runner fails the gate" \
  env PATH="$dotnet_shim:$PATH" bash tests/features/bio-ops-006/BIO-OPS-006.acceptance.sh --cross-tenant-only

# Kemi's ruling removes the incomplete fallback. Both scanners must fail closed
# with the actionable toolchain-policy message when gitleaks is unavailable.
missing_gitleaks_is_clear_failure() {
  local script="$1"
  local output
  if output="$(env PATH="/usr/bin:/bin" bash "$script" --all 2>&1)"; then
    return 0
  fi
  grep -Fxq 'gitleaks is required and not installed - see docs/TOOLCHAIN-POLICY.md' <<<"$output" || return 0
  return 1
}

check_rejected "current-tree scan fails clearly when gitleaks is unavailable" \
  missing_gitleaks_is_clear_failure scripts/secret-scan.sh
check_rejected "history scan fails clearly when gitleaks is unavailable" \
  missing_gitleaks_is_clear_failure scripts/history-secret-scan.sh

# The native allowlist contains exact synthetic fixture values, never a path or
# broad credential class. A different real-shaped secret must remain detectable.
cp .gitleaks.toml "$gitleaks_probe/.gitleaks.toml"
probe_token='xoxb'
probe_token+='-555555555555-444444444444-zyxwvutsrqponmlkjihgfedc'
(
  cd "$gitleaks_probe"
  git init -q
  git config user.name mutation
  git config user.email mutation@example.invalid
  printf 'SLACK_TOKEN=%s\n' "$probe_token" >app.env
  git add .gitleaks.toml app.env
  git commit -qm 'commit unallowlisted credential mutant'
)
check_rejected "gitleaks allowlist does not hide an unlisted Slack token" \
  bash -c 'cd "$1" && gitleaks git --no-banner --redact .' _ "$gitleaks_probe"

echo "=============================="
echo "$pass passed, $fail failed"
test "$fail" -eq 0
