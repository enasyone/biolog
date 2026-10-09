#!/usr/bin/env bash
set -u

repo="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d "${TMPDIR:-/tmp}/bio-fnd-005-codex.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT
fail=0

pass() { printf 'PASS %s\n' "$1"; }
fail_case() { printf 'FAIL %s\n' "$1"; fail=1; }

if python3 - governance/munder-agents.json <<'PY'
import json, sys
d=json.load(open(sys.argv[1])); agents=d["agents"]
expected={"Chinedu","Kemi","Tunde","Ngozi","Amina","Ifeanyi","Busola","Emeka","Seyi","Zainab","Femi","Chioma"}
assert len(agents)==12
assert {a["name"] for a in agents} == expected
assert len({a["name"] for a in agents}) == 12
for a in agents:
    assert all(a.get(k) for k in ("role","engine","model"))
    assert isinstance(a.get("permissions"), dict) and a["permissions"]
PY
then pass 'AC01 exact unique 12-agent roster and required attributes'; else fail_case 'AC01 exact unique 12-agent roster and required attributes'; fi

if python3 - governance/munder-agents.json <<'PY'
import json, sys
a=json.load(open(sys.argv[1]))["agents"]
assert [x["name"] for x in a if x["permissions"].get("approve_shared_contracts") is True] == ["Kemi"]
assert [x["name"] for x in a if "migrations" in x["permissions"].get("write_scope", [])] == ["Kemi"]
PY
then pass 'AC03 Kemi alone has contract approval and migration scope'; else fail_case 'AC03 Kemi alone has contract approval and migration scope'; fi

if python3 - governance/munder-agents.json <<'PY'
import json, sys
a=json.load(open(sys.argv[1]))["agents"]
assert all(x["permissions"].get("git_push") is False for x in a)
PY
then pass 'AC05 every agent explicitly has git_push false'; else fail_case 'AC05 every agent explicitly has git_push false'; fi

if [ "$(git config --get core.hooksPath)" = .githooks ] && [ -x .githooks/pre-push ] && ! sh .githooks/pre-push >/dev/null 2>&1; then
  pass 'AC05 active executable guard blocks while W15 flag is false'
else
  fail_case 'AC05 active executable guard blocks while W15 flag is false'
fi

git clone -q --no-local "$repo" "$tmp/repo"
client="$tmp/repo"
git -C "$client" remote remove origin
run_dev() { (cd "$client" && bash tests/foundation/BIO-FND-005.test.sh) >"$tmp/last.log" 2>&1; }
mutate() {
  local expression="$1"
  jq "$expression" "$client/governance/munder-agents.json" >"$tmp/agents.json"
  mv "$tmp/agents.json" "$client/governance/munder-agents.json"
}
restore() { git -C "$client" show HEAD:governance/munder-agents.json >"$client/governance/munder-agents.json"; }

mutate '.agents += [{"name":"Fake","role":"Extra","engine":"Other","model":"x","permissions":{"git_push":false}}]'
if run_dev; then fail_case 'AC01 supplied check rejects a 13th agent'; else pass 'AC01 supplied check rejects a 13th agent'; fi
restore

mutate '(.agents[] | select(.name=="Emeka").permissions.approve_shared_contracts) = true'
if run_dev; then fail_case 'AC03 supplied check rejects a second approver'; else pass 'AC03 supplied check rejects a second approver'; fi
restore

mutate '.rules.dispatch_eligibility = "Chinedu may dispatch whether dependencies are DONE or not; conflict locks are ignored."'
if run_dev; then fail_case 'AC02 contradictory dispatch rule is rejected'; else pass 'AC02 contradictory dispatch rule is rejected'; fi
restore

mutate '(.agents[] | select(.name=="Emeka").permissions.self_accept) = "allowed"'
if run_dev; then fail_case 'AC04 non-boolean self-accept grant is rejected'; else pass 'AC04 non-boolean self-accept grant is rejected'; fi
restore

mutate '(.agents[] | select(.name=="Emeka").permissions.git_push) = true'
if run_dev; then fail_case 'AC05 git_push true is rejected'; else pass 'AC05 git_push true is rejected'; fi
restore

mutate '(.agents[] | select(.name=="Emeka").permissions) |= del(.self_accept)'
if run_dev; then fail_case 'AC04 missing self_accept permission is rejected'; else pass 'AC04 missing self_accept permission is rejected'; fi
restore

mutate '(.agents[] | select(.name=="Emeka").permissions) |= del(.git_push)'
if run_dev; then fail_case 'AC05 missing git_push permission is rejected'; else pass 'AC05 missing git_push permission is rejected'; fi
restore

mutate '(.agents[] | select(.name=="Chinedu").name) = "Alice"'
if run_dev; then fail_case 'AC01 substitution of a required named agent is rejected'; else pass 'AC01 substitution of a required named agent is rejected'; fi
restore

mutate '(.agents[] | select(.name=="Kemi").permissions.write_scope) -= ["migrations"]'
if run_dev; then fail_case 'AC03 removal of Kemi migration authority is rejected'; else pass 'AC03 removal of Kemi migration authority is rejected'; fi
restore

git -C "$client" remote add synthetic "$tmp/nonexistent-remote"
if run_dev; then fail_case 'AC05 configured Git remote is rejected'; else pass 'AC05 configured Git remote is rejected'; fi

exit "$fail"
