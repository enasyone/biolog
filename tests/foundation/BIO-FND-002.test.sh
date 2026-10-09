#!/usr/bin/env bash
# Developer tests for BIO-FND-002, mapped to acceptance criteria.
# Run from anywhere: tests/foundation/BIO-FND-002.test.sh
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

manifest_categories() {
  python3 -c "
import json
d = json.load(open('toolchain/versions.json'))
t = d['toolchain']
required = ['dotnet', 'node', 'package_manager', 'flutter', 'docker', 'database_client', 'gitleaks']
missing = [k for k in required if k not in t]
exit(1 if missing else 0)
"
}

# --- BIO-FND-002-AC01: policy covers all six categories (plus gitleaks, a
# seventh category added via TD-FND002-PIN-GITLEAKS per Kemi's BIO-OPS-006
# round-3 ruling once the no-gitleaks fallback was removed) ---------------
check "AC01: toolchain/versions.json declares all required categories" manifest_categories

ac01_pinned_versions_nonempty() {
  python3 -c "
import json
d = json.load(open('toolchain/versions.json'))['toolchain']
for key in ('dotnet', 'node', 'flutter', 'gitleaks'):
    assert d[key].get('pinned_version'), key
assert d['package_manager'].get('pinned_version')
assert d['docker'].get('pinned_engine_version') and d['docker'].get('pinned_compose_version')
assert d['database_client'].get('pinned_version')
"
}
check "AC01: every category has a non-empty pinned version" ac01_pinned_versions_nonempty

# --- BIO-FND-002-AC02: exact versions recorded in native pin files --------
ac02_global_json_matches_manifest() {
  python3 -c "
import json
manifest = json.load(open('toolchain/versions.json'))['toolchain']['dotnet']
g = json.load(open('global.json'))
assert g['sdk']['version'] == manifest['pinned_version']
assert g['sdk']['rollForward'] == 'disable'
"
}
check "AC02: global.json sdk.version matches manifest and rollForward=disable" ac02_global_json_matches_manifest

ac02_nvmrc_matches_manifest() {
  local pinned
  pinned="$(python3 -c "import json; print(json.load(open('toolchain/versions.json'))['toolchain']['node']['pinned_version'])")"
  [ "$(tr -d '[:space:]' < .nvmrc)" = "$pinned" ]
}
check "AC02: .nvmrc matches manifest pinned Node version" ac02_nvmrc_matches_manifest

ac02_package_json_pins_pnpm() {
  python3 -c "
import json
manifest = json.load(open('toolchain/versions.json'))['toolchain']['package_manager']
pkg = json.load(open('package.json'))
assert pkg['packageManager'] == f\"pnpm@{manifest['pinned_version']}\"
"
}
check "AC02: package.json packageManager pins the manifest's pnpm version" ac02_package_json_pins_pnpm

# Regression tests for BIO-FND-002-QA-01 (independent review finding): the
# checker used to read installed binaries and the manifest but never read
# the native pin files back, so a native pin could disagree with
# toolchain/versions.json - the declared single source of truth - and the
# check would still report success. Each of these builds an isolated scratch
# clone, tampers exactly one native pin file, and asserts the checker now
# fails specifically on that mismatch.
qa01_scratch_repo() {
  local dir="$1"
  mkdir -p "$dir/scripts" "$dir/toolchain" "$dir/web" "$dir/mobile" "$dir/api"
  cp scripts/check-toolchain.sh "$dir/scripts/check-toolchain.sh"
  cp toolchain/versions.json "$dir/toolchain/versions.json"
  cp global.json "$dir/global.json"
  cp .nvmrc "$dir/.nvmrc"
  cp package.json "$dir/package.json"
  chmod +x "$dir/scripts/check-toolchain.sh"
  ( cd "$dir" && git init -q -b main )
}

qa01_nvmrc_drift_is_caught() {
  local scratch
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-qa01-nvmrc.XXXXXX")"
  qa01_scratch_repo "$scratch"
  printf '99.0.0\n' > "$scratch/.nvmrc"
  local rc=0 out
  out="$(cd "$scratch" && bash scripts/check-toolchain.sh 2>&1)" || rc=$?
  rm -rf "$scratch"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "\.nvmrc (Node pin): installed '99\.0\.0' != pinned"
}
check "AC02/QA-01: .nvmrc edited to disagree with the manifest fails the check" qa01_nvmrc_drift_is_caught

qa01_global_json_drift_is_caught() {
  local scratch
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-qa01-globaljson.XXXXXX")"
  qa01_scratch_repo "$scratch"
  python3 -c "
import json
p = '$scratch/global.json'
d = json.load(open(p))
d['sdk']['version'] = '9.9.999'
json.dump(d, open(p, 'w'))
"
  local rc=0 out
  out="$(cd "$scratch" && bash scripts/check-toolchain.sh 2>&1)" || rc=$?
  rm -rf "$scratch"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "global\.json sdk\.version (\.NET pin): installed '9\.9\.999' != pinned"
}
check "AC02/QA-01: global.json sdk.version edited to disagree with the manifest fails the check" qa01_global_json_drift_is_caught

qa01_package_json_drift_is_caught() {
  local scratch
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-qa01-packagejson.XXXXXX")"
  qa01_scratch_repo "$scratch"
  python3 -c "
import json
p = '$scratch/package.json'
d = json.load(open(p))
d['packageManager'] = 'pnpm@8.0.0'
d['engines']['node'] = '18.0.0'
json.dump(d, open(p, 'w'))
"
  local rc=0 out
  out="$(cd "$scratch" && bash scripts/check-toolchain.sh 2>&1)" || rc=$?
  rm -rf "$scratch"
  [ "$rc" -ne 0 ] \
    && printf '%s\n' "$out" | grep -q "package\.json packageManager (pnpm pin): installed 'pnpm@8\.0\.0' != pinned" \
    && printf '%s\n' "$out" | grep -q "package\.json engines\.node: installed '18\.0\.0' != pinned"
}
check "AC02/QA-01: package.json pnpm/node pins edited to disagree with the manifest fail the check" qa01_package_json_drift_is_caught

# --- BIO-FND-002-AC03: lock file policy + no silent upgrades --------------
ac03_lockfile_violation_is_caught() {
  # Regression: a manifest without its lock file must fail the check.
  local scratch
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-ac03-lock.XXXXXX")"
  mkdir -p "$scratch/scripts" "$scratch/toolchain" "$scratch/web"
  cp scripts/check-toolchain.sh "$scratch/scripts/check-toolchain.sh"
  cp toolchain/versions.json "$scratch/toolchain/versions.json"
  chmod +x "$scratch/scripts/check-toolchain.sh"
  printf '{"name":"fixture"}\n' > "$scratch/web/package.json"
  ( cd "$scratch" && git init -q -b main )
  local rc=0
  local out
  out="$(cd "$scratch" && bash scripts/check-toolchain.sh 2>&1)" || rc=$?
  rm -rf "$scratch"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "web/package.json exists without a committed web/pnpm-lock.yaml"
}
check "AC03: web/package.json without pnpm-lock.yaml fails the check" ac03_lockfile_violation_is_caught

ac03_lockfile_present_passes() {
  # Boundary case: once the lock file exists alongside the manifest, that
  # specific check must pass (proves AC03 isn't just permanently failing).
  local scratch
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-ac03-lock-ok.XXXXXX")"
  mkdir -p "$scratch/scripts" "$scratch/toolchain" "$scratch/web"
  cp scripts/check-toolchain.sh "$scratch/scripts/check-toolchain.sh"
  cp toolchain/versions.json "$scratch/toolchain/versions.json"
  chmod +x "$scratch/scripts/check-toolchain.sh"
  printf '{"name":"fixture"}\n' > "$scratch/web/package.json"
  printf 'lockfileVersion: 9.0\n' > "$scratch/web/pnpm-lock.yaml"
  ( cd "$scratch" && git init -q -b main )
  local out
  out="$(cd "$scratch" && bash scripts/check-toolchain.sh 2>&1)"
  rm -rf "$scratch"
  printf '%s\n' "$out" | grep -q "PASS  web: web/pnpm-lock.yaml is committed alongside web/package.json"
}
check "AC03: web/package.json with pnpm-lock.yaml passes that check" ac03_lockfile_present_passes

ac03_version_drift_is_caught() {
  # Regression: if the manifest is edited to a version that does not match
  # what is actually installed (an unapproved "upgrade" or a typo), the
  # check must fail rather than silently accept it.
  local scratch
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-ac03-drift.XXXXXX")"
  mkdir -p "$scratch/scripts" "$scratch/toolchain"
  cp scripts/check-toolchain.sh "$scratch/scripts/check-toolchain.sh"
  chmod +x "$scratch/scripts/check-toolchain.sh"
  python3 -c "
import json
d = json.load(open('toolchain/versions.json'))
d['toolchain']['dotnet']['pinned_version'] = '1.0.0'
json.dump(d, open('$scratch/toolchain/versions.json', 'w'))
"
  ( cd "$scratch" && git init -q -b main )
  local rc=0
  local out
  out="$(cd "$scratch" && bash scripts/check-toolchain.sh 2>&1)" || rc=$?
  rm -rf "$scratch"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "\.NET SDK: installed .* != pinned '1.0.0'"
}
check "AC03/AC04: a manifest edited to an uninstalled version is reported as drift" ac03_version_drift_is_caught

gitleaks_installed_version_is_checked_exact() {
  # Regression for TD-FND002-PIN-GITLEAKS: gitleaks is pinned exact (not a
  # minimum) because its detection rules, not just its interface, can change
  # between versions. Fakes a `gitleaks` binary reporting a version that
  # disagrees with the pinned one and asserts the checker fails specifically
  # on that mismatch rather than accepting any installed version.
  local fakebin rc=0 out
  fakebin="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-gitleaks-bin.XXXXXX")"
  printf '#!/usr/bin/env bash\nprintf "1.0.0\\n"\n' > "$fakebin/gitleaks"
  chmod +x "$fakebin/gitleaks"
  out="$(PATH="$fakebin:$PATH" scripts/check-toolchain.sh 2>&1)" || rc=$?
  rm -rf "$fakebin"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "gitleaks: installed '1.0.0' != pinned"
}
check "AC01/AC04: gitleaks installed at a non-pinned version fails the check" gitleaks_installed_version_is_checked_exact

qa02_unsupported_mysql_major_is_rejected() {
  # Regression for BIO-FND-002-QA-02: the policy pins MySQL client 8.4.0 as
  # the supported family (it must match the infrastructure/ container image),
  # but check_min's plain "installed >= pinned" comparison let a numerically
  # larger major (e.g. 9.0.0) through. Fakes a `mysql` binary reporting 9.0.0
  # on PATH and asserts the checker now rejects it by major, not just passes
  # it because 9 > 8.
  local scratch fakebin
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-qa02-repo.XXXXXX")"
  fakebin="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-qa02-bin.XXXXXX")"
  qa01_scratch_repo "$scratch"
  printf '#!/usr/bin/env bash\nprintf "mysql  Ver 9.0.0 for synthetic\\n"\n' > "$fakebin/mysql"
  chmod +x "$fakebin/mysql"
  local rc=0 out
  out="$(cd "$scratch" && PATH="$fakebin:$PATH" bash scripts/check-toolchain.sh 2>&1)" || rc=$?
  rm -rf "$scratch" "$fakebin"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "MySQL client: installed 9\.0\.0 is major version 9, unsupported"
}
check "AC04/QA-02: MySQL client major 9.x is rejected even though 9 >= 8.4.0 numerically" qa02_unsupported_mysql_major_is_rejected

qa02_same_major_still_passes() {
  # Boundary case: a same-major, higher-patch MySQL client (8.5.0) must still
  # pass - QA-02's fix must not turn into "any major mismatch, including
  # none at all" or a same-major upgrade within policy would wrongly fail.
  local scratch fakebin
  scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-qa02-ok-repo.XXXXXX")"
  fakebin="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd002-qa02-ok-bin.XXXXXX")"
  qa01_scratch_repo "$scratch"
  printf '#!/usr/bin/env bash\nprintf "mysql  Ver 8.5.0 for synthetic\\n"\n' > "$fakebin/mysql"
  chmod +x "$fakebin/mysql"
  local out
  out="$(cd "$scratch" && PATH="$fakebin:$PATH" bash scripts/check-toolchain.sh 2>&1)"
  rm -rf "$scratch" "$fakebin"
  printf '%s\n' "$out" | grep -q "PASS  MySQL client: 8\.5\.0 (>= minimum supported 8\.4\.0, supported major 8)"
}
check "AC04/QA-02: MySQL client 8.5.0 (same major, higher patch) still passes" qa02_same_major_still_passes

# --- BIO-FND-002-AC04: version check command reports drift and fails ------
ac04_reports_known_missing_tools() {
  # pnpm and the MySQL client are documented as not installed on this
  # bootstrap machine (see docs/TOOLCHAIN-POLICY.md); the checker must say
  # so explicitly rather than silently skipping them. gitleaks is
  # deliberately NOT asserted missing here: unlike pnpm/MySQL it was
  # installed after TD-FND002-PIN-GITLEAKS's own correction commit (its
  # pinned version was reverified against that installed binary), so on
  # this machine it now PASSes - see ac04_reports_matching_tools_as_pass.
  local out
  out="$(scripts/check-toolchain.sh 2>&1 || true)"
  printf '%s\n' "$out" | grep -q "FAIL  pnpm: NOT INSTALLED" \
    && printf '%s\n' "$out" | grep -q "FAIL  MySQL client: NOT INSTALLED"
}
check "AC04: checker reports pnpm and MySQL client as not installed" ac04_reports_known_missing_tools

ac04_reports_matching_tools_as_pass() {
  local out
  out="$(scripts/check-toolchain.sh 2>&1 || true)"
  printf '%s\n' "$out" | grep -q "PASS  \.NET SDK" \
    && printf '%s\n' "$out" | grep -q "PASS  Node\.js" \
    && printf '%s\n' "$out" | grep -q "PASS  Docker Engine" \
    && printf '%s\n' "$out" | grep -q "PASS  gitleaks"
}
check "AC04: checker reports installed, matching tools as PASS" ac04_reports_matching_tools_as_pass

ac04_exits_nonzero_while_any_tool_missing_or_drifted() {
  # True today because pnpm and the MySQL client are documented as not
  # installed (see docs/TOOLCHAIN-POLICY.md "Known gaps"). Once both are
  # installed at their pinned versions this assertion should flip to
  # expecting exit 0 - update it in the same change that installs them.
  scripts/check-toolchain.sh >/dev/null 2>&1
  [ "$?" -ne 0 ]
}
check "AC04: overall check exits non-zero while a required tool is missing/drifted" ac04_exits_nonzero_while_any_tool_missing_or_drifted

echo "=============================="
echo "$((total - failed))/$total checks passed"
[ "$failed" -eq 0 ]
