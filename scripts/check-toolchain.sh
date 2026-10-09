#!/usr/bin/env bash
# BIOLOG toolchain drift check (BIO-FND-002-AC04).
#
# Compares installed local tool versions against toolchain/versions.json,
# the single source of truth for the pinned toolchain (BIO-FND-002-AC01/02),
# and enforces the lock-file commitment policy (BIO-FND-002-AC03). Exits 0
# only when every pinned category and every existing manifest's lock file
# check passes; otherwise reports each drift/missing item and exits 1.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

MANIFEST="toolchain/versions.json"
fail=0

pass() { echo "PASS  $1"; }
bad()  { echo "FAIL  $1"; fail=1; }

if ! command -v python3 >/dev/null 2>&1; then
  echo "check-toolchain: python3 is required to parse $MANIFEST" >&2
  exit 2
fi

if [ ! -f "$MANIFEST" ]; then
  echo "check-toolchain: $MANIFEST is missing" >&2
  exit 2
fi

manifest_get() {
  python3 - "$MANIFEST" "$1" <<'PY'
import json, sys
manifest, path = sys.argv[1], sys.argv[2]
node = json.load(open(manifest))["toolchain"]
for key in path.split("."):
    node = node[key]
print(node)
PY
}

# Returns 0 if dotted numeric version $1 >= $2 (major.minor[.patch...]).
version_ge() {
  [ "$1" = "$2" ] && return 0
  local IFS=.
  local -a a=($1) b=($2)
  local i len=${#a[@]}
  [ ${#b[@]} -gt "$len" ] && len=${#b[@]}
  for ((i = 0; i < len; i++)); do
    local x=${a[i]:-0} y=${b[i]:-0}
    if ((10#$x > 10#$y)); then return 0; fi
    if ((10#$x < 10#$y)); then return 1; fi
  done
  return 0
}

check_exact() {
  local name="$1" installed="$2" pinned="$3"
  if [ -z "$installed" ]; then
    bad "$name: NOT INSTALLED (pinned exact version: $pinned)"
  elif [ "$installed" = "$pinned" ]; then
    pass "$name: $installed (matches pinned exact version)"
  else
    bad "$name: installed '$installed' != pinned '$pinned' (exact match required; do not change this inside a feature story - see docs/TOOLCHAIN-POLICY.md)"
  fi
}

check_min() {
  local name="$1" installed="$2" pinned="$3"
  if [ -z "$installed" ]; then
    bad "$name: NOT INSTALLED (minimum supported version: $pinned)"
  elif version_ge "$installed" "$pinned"; then
    pass "$name: $installed (>= minimum supported $pinned)"
  else
    bad "$name: installed $installed < minimum supported $pinned"
  fi
}

# Like check_min, but also rejects a different major version outright, even
# if it numerically satisfies version_ge (e.g. 9.0.0 >= 8.4.0). Used for
# categories where the policy names a specific supported major (currently
# only the MySQL client - see docs/TOOLCHAIN-POLICY.md).
check_min_same_major() {
  local name="$1" installed="$2" pinned="$3"
  if [ -z "$installed" ]; then
    bad "$name: NOT INSTALLED (minimum supported version: $pinned)"
    return
  fi
  local installed_major="${installed%%.*}" pinned_major="${pinned%%.*}"
  if [ "$installed_major" != "$pinned_major" ]; then
    bad "$name: installed $installed is major version $installed_major, unsupported (supported major: $pinned_major, minimum $pinned)"
  elif version_ge "$installed" "$pinned"; then
    pass "$name: $installed (>= minimum supported $pinned, supported major $pinned_major)"
  else
    bad "$name: installed $installed < minimum supported $pinned"
  fi
}

dotnet_pinned="$(manifest_get dotnet.pinned_version)"
node_pinned="$(manifest_get node.pinned_version)"
pnpm_pinned="$(manifest_get package_manager.pinned_version)"

echo "== BIO-FND-002-AC01/AC02/AC04: pinned toolchain versions =="

dotnet_installed=""
command -v dotnet >/dev/null 2>&1 && dotnet_installed="$(dotnet --version 2>/dev/null)"
check_exact ".NET SDK" "$dotnet_installed" "$dotnet_pinned"

node_installed=""
command -v node >/dev/null 2>&1 && node_installed="$(node --version 2>/dev/null | sed 's/^v//')"
check_exact "Node.js" "$node_installed" "$node_pinned"

pnpm_installed=""
command -v pnpm >/dev/null 2>&1 && pnpm_installed="$(pnpm --version 2>/dev/null)"
check_exact "pnpm" "$pnpm_installed" "$pnpm_pinned"

flutter_installed=""
if command -v flutter >/dev/null 2>&1; then
  flutter_installed="$(flutter --version --machine 2>/dev/null \
    | python3 -c 'import json,sys; print(json.load(sys.stdin).get("frameworkVersion",""))' 2>/dev/null || true)"
fi
check_exact "Flutter" "$flutter_installed" "$(manifest_get flutter.pinned_version)"

docker_installed=""
command -v docker >/dev/null 2>&1 && docker_installed="$(docker --version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)"
check_min "Docker Engine" "$docker_installed" "$(manifest_get docker.pinned_engine_version)"

compose_installed=""
command -v docker >/dev/null 2>&1 && compose_installed="$(docker compose version --short 2>/dev/null)"
check_min "Docker Compose" "$compose_installed" "$(manifest_get docker.pinned_compose_version)"

mysql_installed=""
if command -v mysql >/dev/null 2>&1; then
  mysql_installed="$(mysql --version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)"
fi
# Same-major enforcement (BIO-FND-002-QA-02): the policy names MySQL 8.4 as
# the supported family, so a numerically-larger major (e.g. 9.0.0) must not
# pass a plain >= comparison.
check_min_same_major "MySQL client" "$mysql_installed" "$(manifest_get database_client.pinned_version)"

# Seventh category, added via TD-FND002-PIN-GITLEAKS (Kemi's BIO-OPS-006
# round-3 ruling): scripts/secret-scan.sh and scripts/history-secret-scan.sh
# now require gitleaks and fail closed when it is absent, so this check
# surfaces the same "NOT INSTALLED"/drift signal for it as every other
# mandatory category rather than letting it hide behind a separate gate.
gitleaks_installed=""
if command -v gitleaks >/dev/null 2>&1; then
  gitleaks_installed="$(gitleaks version 2>/dev/null | tr -d '[:space:]' | sed 's/^v//')"
fi
check_exact "gitleaks" "$gitleaks_installed" "$(manifest_get gitleaks.pinned_version)"

echo "== BIO-FND-002-AC02: native pin files vs toolchain/versions.json =="

# BIO-FND-002-QA-01: toolchain/versions.json is the declared single source
# of truth and the native pin files below are supposed to mirror it exactly,
# but nothing previously checked that they actually do. A native file can be
# read/committed and still silently disagree with the manifest (or be
# missing outright); either case must fail here even if the installed
# binaries above happen to match the manifest.

if [ -f .nvmrc ]; then
  check_exact ".nvmrc (Node pin)" "$(tr -d '[:space:]' < .nvmrc)" "$node_pinned"
else
  bad ".nvmrc: missing (required native pin for Node, see docs/TOOLCHAIN-POLICY.md)"
fi

if [ -f global.json ]; then
  gj_version="$(python3 -c "import json; print(json.load(open('global.json'))['sdk'].get('version',''))" 2>/dev/null)"
  check_exact "global.json sdk.version (.NET pin)" "$gj_version" "$dotnet_pinned"
  gj_rollforward="$(python3 -c "import json; print(json.load(open('global.json'))['sdk'].get('rollForward',''))" 2>/dev/null)"
  if [ "$gj_rollforward" = "disable" ]; then
    pass "global.json sdk.rollForward: disable (exact pin enforced natively by the dotnet CLI)"
  else
    bad "global.json sdk.rollForward: '$gj_rollforward', expected 'disable' (anything else lets the dotnet CLI silently roll forward to an unpinned SDK)"
  fi
else
  bad "global.json: missing (required native pin for the .NET SDK, see docs/TOOLCHAIN-POLICY.md)"
fi

if [ -f package.json ]; then
  pkg_pm="$(python3 -c "import json; print(json.load(open('package.json')).get('packageManager',''))" 2>/dev/null)"
  check_exact "package.json packageManager (pnpm pin)" "$pkg_pm" "pnpm@${pnpm_pinned}"
  pkg_node_engine="$(python3 -c "import json; print(json.load(open('package.json')).get('engines',{}).get('node',''))" 2>/dev/null)"
  check_exact "package.json engines.node" "$pkg_node_engine" "$node_pinned"
else
  bad "package.json: missing (required native pin for pnpm/Node, see docs/TOOLCHAIN-POLICY.md)"
fi

echo "== BIO-FND-002-AC03: lock file policy =="

lockfile_check() {
  local manifest_file="$1" lock_file="$2" ecosystem="$3"
  if [ ! -f "$manifest_file" ]; then
    pass "$ecosystem: no $manifest_file yet (nothing to enforce)"
    return
  fi
  if [ -f "$lock_file" ]; then
    pass "$ecosystem: $lock_file is committed alongside $manifest_file"
  else
    bad "$ecosystem: $manifest_file exists without a committed $lock_file"
  fi
}

lockfile_check "web/package.json" "web/pnpm-lock.yaml" "web"
lockfile_check "mobile/pubspec.yaml" "mobile/pubspec.lock" "mobile"

api_csproj_count=0
if [ -d api ]; then
  while IFS= read -r -d '' proj; do
    api_csproj_count=$((api_csproj_count + 1))
    lock_path="$(dirname "$proj")/packages.lock.json"
    if [ -f "$lock_path" ]; then
      pass "api: $lock_path is committed alongside $proj"
    else
      bad "api: $proj exists without a committed $lock_path"
    fi
  done < <(find api -name '*.csproj' -print0 2>/dev/null)
fi
if [ "$api_csproj_count" -eq 0 ]; then
  pass "api: no *.csproj yet (nothing to enforce)"
fi

echo "=============================="
if [ "$fail" -eq 0 ]; then
  echo "TOOLCHAIN CHECK: SUCCESS"
  exit 0
else
  echo "TOOLCHAIN CHECK: DRIFT DETECTED"
  exit 1
fi
