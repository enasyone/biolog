#!/usr/bin/env bash
set -u

repo="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d "${TMPDIR:-/tmp}/bio-fnd-002-codex.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT
fail=0

pass() { printf 'PASS %s\n' "$1"; }
fail_case() { printf 'FAIL %s\n' "$1"; fail=1; }

git clone -q --no-local "$repo" "$tmp/repo"
client="$tmp/repo"
fake="$tmp/bin"
mkdir -p "$fake"

make_tool() {
  local name="$1" body="$2"
  printf '#!/usr/bin/env bash\n%s\n' "$body" >"$fake/$name"
  chmod +x "$fake/$name"
}

make_tool dotnet "printf '%s\\n' '10.0.201'"
make_tool node "printf '%s\\n' 'v20.20.2'"
make_tool pnpm "printf '%s\\n' '9.12.0'"
make_tool flutter "printf '%s\\n' '{\"frameworkVersion\":\"3.44.8\"}'"
make_tool docker "if [ \"\${1:-}\" = compose ]; then printf '%s\\n' '5.1.1'; else printf '%s\\n' 'Docker version 29.3.1, build synthetic'; fi"
make_tool mysql "printf '%s\\n' 'mysql  Ver 8.4.0 for synthetic'"

run_check() {
  (cd "$client" && PATH="$fake:/usr/bin:/bin:/usr/sbin:/sbin" bash scripts/check-toolchain.sh) >"$tmp/last.log" 2>&1
}

if run_check; then pass 'AC01/02/04 exact synthetic baseline succeeds'; else fail_case 'AC01/02/04 exact synthetic baseline succeeds'; fi

# The manifest must drive drift checks.
cp "$client/toolchain/versions.json" "$tmp/versions.json"
python3 - "$client/toolchain/versions.json" <<'PY'
import json, sys
p=sys.argv[1]
d=json.load(open(p))
d["toolchain"]["node"]["pinned_version"]="20.20.1"
open(p,"w").write(json.dumps(d, indent=2)+"\n")
PY
if run_check; then fail_case 'AC04 tampered manifest pin is detected'; else pass 'AC04 tampered manifest pin is detected'; fi
cp "$tmp/versions.json" "$client/toolchain/versions.json"

# Missing workspace lock files must fail.
printf '{"private":true}\n' >"$client/web/package.json"
if run_check; then fail_case 'AC03 missing web lock is rejected'; else pass 'AC03 missing web lock is rejected'; fi
rm "$client/web/package.json"
printf 'name: synthetic\n' >"$client/mobile/pubspec.yaml"
if run_check; then fail_case 'AC03 missing mobile lock is rejected'; else pass 'AC03 missing mobile lock is rejected'; fi
rm "$client/mobile/pubspec.yaml"
printf '<Project Sdk="Microsoft.NET.Sdk"/>\n' >"$client/api/Synthetic.csproj"
if run_check; then fail_case 'AC03 missing API lock is rejected'; else pass 'AC03 missing API lock is rejected'; fi
rm "$client/api/Synthetic.csproj"

# Malformed or incomplete policy input must fail closed.
printf '{ malformed\n' >"$client/toolchain/versions.json"
if run_check; then fail_case 'AC04 malformed versions.json fails closed'; else pass 'AC04 malformed versions.json fails closed'; fi
printf '{"toolchain":{"node":{"pinned_version":"20.20.2"}}}\n' >"$client/toolchain/versions.json"
if run_check; then fail_case 'AC04 partial versions.json fails closed'; else pass 'AC04 partial versions.json fails closed'; fi
cp "$tmp/versions.json" "$client/toolchain/versions.json"

# Native pins are repository-managed enforcement files and must not drift from
# the single source of truth. Each mutation should make the checker fail.
cp "$client/.nvmrc" "$tmp/nvmrc"
printf '99.0.0\n' >"$client/.nvmrc"
if run_check; then fail_case 'AC02/03/04 tampered .nvmrc is detected'; else pass 'AC02/03/04 tampered .nvmrc is detected'; fi
cp "$tmp/nvmrc" "$client/.nvmrc"

cp "$client/global.json" "$tmp/global.json"
python3 - "$client/global.json" <<'PY'
import json, sys
p=sys.argv[1]
d=json.load(open(p)); d["sdk"]["version"]="9.9.999"
open(p,"w").write(json.dumps(d, indent=2)+"\n")
PY
if run_check; then fail_case 'AC02/03/04 tampered global.json is detected'; else pass 'AC02/03/04 tampered global.json is detected'; fi
cp "$tmp/global.json" "$client/global.json"

cp "$client/package.json" "$tmp/package.json"
python3 - "$client/package.json" <<'PY'
import json, sys
p=sys.argv[1]
d=json.load(open(p)); d["packageManager"]="pnpm@8.0.0"; d["engines"]["node"]="18.0.0"
open(p,"w").write(json.dumps(d, indent=2)+"\n")
PY
if run_check; then fail_case 'AC02/03/04 tampered package.json pins are detected'; else pass 'AC02/03/04 tampered package.json pins are detected'; fi
cp "$tmp/package.json" "$client/package.json"

# The policy explicitly says a different MySQL major (for example 9.x) is
# unsupported; a simple numeric >= comparison must not accept it.
make_tool mysql "printf '%s\\n' 'mysql  Ver 9.0.0 for synthetic'"
if run_check; then fail_case 'AC04 unsupported MySQL 9.x is rejected'; else pass 'AC04 unsupported MySQL 9.x is rejected'; fi

exit "$fail"
