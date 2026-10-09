#!/usr/bin/env bash
# BIO-FND-005 developer tests — machine-enforced governance. AC IDs + Seyi QA-01..03 regressions.
set -u
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
V="$ROOT/scripts/validate-governance.py"
J="$ROOT/governance/munder-agents.json"
TMP="$ROOT/.govtest"; rm -rf "$TMP"; mkdir -p "$TMP/bin"
trap 'rm -rf "$TMP"' EXIT
pass=0; fail=0; n=0
ok(){ echo "PASS $1"; pass=$((pass+1)); }
no(){ echo "FAIL $1"; fail=$((fail+1)); }
# mutate governance into a plain dir (no git needed) and expect validator to FAIL
expect_fail(){
  n=$((n+1)); local d="$TMP/c$n"; mkdir -p "$d/governance"
  python3 - "$J" "$d/governance/munder-agents.json" <<PY || { no "$1 (mutation setup error)"; return; }
import json,sys
d=json.load(open(sys.argv[1]))
$2
json.dump(d,open(sys.argv[2],"w"),indent=2)
PY
  if python3 "$V" "$d" >/dev/null 2>&1; then no "$1 (validator wrongly PASSED)"; else ok "$1"; fi
}

# Positive: real config passes (real repo, no remote)
python3 "$V" "$ROOT" >/dev/null 2>&1 && ok "AC01-AC05 real-config PASS" || no "AC01-AC05 real-config PASS"
# Sanity: an unmutated copy in a plain dir also passes (proves harness runs the validator, not just erroring)
mkdir -p "$TMP/base/governance"; cp "$J" "$TMP/base/governance/"
python3 "$V" "$TMP/base" >/dev/null 2>&1 && ok "harness sanity: clean copy PASS" || no "harness sanity: clean copy PASS"

expect_fail "QA-01 eligibility allow-incomplete-deps" 'd["rules"]["dispatch_eligibility"]["allow_incomplete_dependencies"]=True; d["rules"]["dispatch_eligibility"]["require_dependencies_done"]=False'
expect_fail "QA-02 self_accept=allowed string" 'd["agents"][7]["permissions"]["self_accept"]="allowed"'
expect_fail "QA-03 git_push true" 'd["agents"][7]["permissions"]["git_push"]=True'
expect_fail "AC01 thirteenth agent" 'd["agents"].append({"name":"Intruder","role":"x","engine":"y","model":"z","permissions":{}})'
expect_fail "AC03 second approver" 'd["agents"][2]["permissions"]["approve_shared_contracts"]=True'

expect_fail "QA-04 missing self_accept key" 'del d["agents"][0]["permissions"]["self_accept"]'
expect_fail "QA-04 missing git_push key" 'del d["agents"][0]["permissions"]["git_push"]'
expect_fail "QA-05 required agent replaced (Chinedu->Alice)" 'd["agents"][0]["name"]="Alice"'
expect_fail "QA-06 Kemi loses migrations scope" '[a["permissions"].__setitem__("write_scope",[x for x in a["permissions"].get("write_scope",[]) if x!="migrations"]) for a in d["agents"] if a["name"]=="Kemi"]'

# QA-03 configured remote: shim `git` to report a remote; validator must FAIL on AC05
printf '#!/usr/bin/env bash\nfor a in "$@"; do [ "$a" = "remote" ] && { echo origin; exit 0; }; done\nexit 0\n' > "$TMP/bin/git"
chmod +x "$TMP/bin/git"
if PATH="$TMP/bin:$PATH" python3 "$V" "$TMP/base" >/dev/null 2>&1; then no "QA-03 configured remote (validator wrongly PASSED)"; else ok "QA-03 configured remote"; fi

echo "---- $pass passed, $fail failed ----"
[ "$fail" -eq 0 ]
