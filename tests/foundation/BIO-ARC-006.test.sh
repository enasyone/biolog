#!/usr/bin/env bash
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

total=0
failed=0
check() {
  local label="$1"; shift
  total=$((total + 1))
  if "$@"; then echo "PASS  $label"; else echo "FAIL  $label" >&2; failed=$((failed + 1)); fi
}

FREEZE=docs/architecture/SHARED-CONTRACT-FREEZE.md
SNAPSHOT=governance/contract-freeze/v1.1.12/manifest.json
OWNERSHIP=governance/feature-path-ownership.json

check "AC01 versioned snapshot validates every frozen artifact" python3 scripts/check-contract-freeze.py
check "AC01 current snapshot version is semantic and baseline is commit-bound" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.12/manifest.json"))
assert m["snapshot_version"] == "1.1.12"
assert m["baseline_parent_commit"] == "2f6826a40db537753f3d91fae867a2b26a518308"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 prior current snapshot (v1.1.11) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.11/manifest.json"))
assert m["snapshot_version"] == "1.1.11"
assert m["baseline_parent_commit"] == "7435abf1f3540661adbe99ea695747c9979239ac"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 prior current snapshot (v1.1.10) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.10/manifest.json"))
assert m["snapshot_version"] == "1.1.10"
assert m["baseline_parent_commit"] == "c4e26b2431e1682ddb1ef113a8850723ebf0650d"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 prior current snapshot (v1.1.9) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.9/manifest.json"))
assert m["snapshot_version"] == "1.1.9"
assert m["baseline_parent_commit"] == "167186c9c32ccda053b6f19f466a434b0c874c2d"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 prior current snapshot (v1.1.8) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.8/manifest.json"))
assert m["snapshot_version"] == "1.1.8"
assert m["baseline_parent_commit"] == "627f84a8628d11a9bd3e8adb129887901e2a5e18"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 prior snapshot (v1.0.0) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.0.0/manifest.json"))
assert m["snapshot_version"] == "1.0.0"
assert m["baseline_parent_commit"] == "a28830e0cb8a0e21006b20942ab7e2d9c686b662"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 superseded snapshot (v1.1.0) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.0/manifest.json"))
assert m["snapshot_version"] == "1.1.0"
assert m["baseline_parent_commit"] == "290c6f531723bfccf7fd1e75ea4ce5326dc5712b"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 superseded snapshot (v1.1.1) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.1/manifest.json"))
assert m["snapshot_version"] == "1.1.1"
assert m["baseline_parent_commit"] == "b2d6682013e9587cefa40d7ce156a7eea6b37823"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 superseded snapshot (v1.1.2) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.2/manifest.json"))
assert m["snapshot_version"] == "1.1.2"
assert m["baseline_parent_commit"] == "49711a88b79aaddc91ec2359e9629b4ff477b616"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 prior current snapshot (v1.1.3) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.3/manifest.json"))
assert m["snapshot_version"] == "1.1.3"
assert m["baseline_parent_commit"] == "95e1b1cc5a3c60630e0f14711a78879e0751c018"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 prior current snapshot (v1.1.4) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.4/manifest.json"))
assert m["snapshot_version"] == "1.1.4"
assert m["baseline_parent_commit"] == "40c40d70099ff65b6ac55455dddc7daf9274db99"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 prior current snapshot (v1.1.5) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.5/manifest.json"))
assert m["snapshot_version"] == "1.1.5"
assert m["baseline_parent_commit"] == "d906407f3118d5d0d72fc0e06c010f3e34d97a12"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 prior current snapshot (v1.1.6) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.6/manifest.json"))
assert m["snapshot_version"] == "1.1.6"
assert m["baseline_parent_commit"] == "0f1298fb06a605a60d19d7ad3f019bad1a5697e4"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 prior current snapshot (v1.1.7) remains an immutable historical record" python3 -c '
import json
m=json.load(open("governance/contract-freeze/v1.1.7/manifest.json"))
assert m["snapshot_version"] == "1.1.7"
assert m["baseline_parent_commit"] == "671d69629e39086020ec69a06fbc74c1616b5c38"
assert all(len(x["sha256"]) == 64 and x["bytes"] > 0 for x in m["artifacts"])
'
check "AC01 a nonexistent baseline commit fails closed" bash -c '
tmp=$(mktemp); python3 -c "import json; p=json.load(open(\"'$SNAPSHOT'\")); p[\"baseline_parent_commit\"]=\"0\"*40; print(json.dumps(p))" > "$tmp"
! python3 scripts/check-contract-freeze.py --snapshot "$tmp" >/dev/null
rm -f "$tmp"
'
check "AC01 a tampered snapshot hash fails closed" bash -c '
tmp=$(mktemp); python3 -c "import json; p=json.load(open(\"'$SNAPSHOT'\")); p[\"artifacts\"][0][\"sha256\"]=\"0\"*64; print(json.dumps(p))" > "$tmp"
! python3 scripts/check-contract-freeze.py --snapshot "$tmp" >/dev/null
rm -f "$tmp"
'
check "AC04 a rewritten contract and regenerated manifest fail closed" bash -c '
root=$(mktemp -d) || exit 1
[ -n "$root" ] && [ -d "$root" ] || exit 1
trap '\''rm -rf "$root"'\'' EXIT
"${FREEZE_CLONE_BIN:-git}" clone -q --no-hardlinks . "$root" || exit 1
printf "\\npost-freeze incompatible rewrite\\n" >> "$root/contracts/README.md" || exit 1
python3 - "$root" <<'\''PY'\''
import hashlib, json, pathlib, sys
root=pathlib.Path(sys.argv[1]); manifest=root/"governance/contract-freeze/v1.1.12/manifest.json"
p=json.loads(manifest.read_text()); target=root/"contracts/README.md"
entry=next(x for x in p["artifacts"] if x["path"] == "contracts/README.md")
entry["bytes"]=target.stat().st_size; entry["sha256"]=hashlib.sha256(target.read_bytes()).hexdigest()
manifest.write_text(json.dumps(p, indent=2)+"\n")
PY
[ "$?" -eq 0 ] || exit 1
output=$(python3 "$root/scripts/check-contract-freeze.py" --root "$root" 2>&1)
checker_rc=$?
[ "$checker_rc" -ne 0 ] || exit 1
printf "%s\\n" "$output" | grep -q "snapshot differs from accepted baseline commit: contracts/README.md"
'
if [ "${FREEZE_SKIP_SETUP_FAILURE_SELFTEST:-0}" != 1 ]; then
check "AC04 clone/setup failure cannot masquerade as tamper rejection" bash -c '
output=$(FREEZE_CLONE_BIN=false FREEZE_SKIP_SETUP_FAILURE_SELFTEST=1 bash tests/foundation/BIO-ARC-006.test.sh 2>&1)
rc=$?
[ "$rc" -ne 0 ] && printf "%s\\n" "$output" | grep -q "FAIL  AC04 a rewritten contract and regenerated manifest fail closed"
'
fi
check "AC02 conflict definition includes contradictions, ownership, findings and overclaims" bash -c "grep -q 'Critical/High' '$FREEZE' && grep -q 'stronger guarantee' '$FREEZE' && grep -q 'same path' '$FREEZE'"
check "AC02 ADR-0005 runner fidelity boundary is preserved, not flattened" bash -c "grep -q 'if and only if' '$FREEZE' && grep -q 'No feature may cite sequence assignment as' '$FREEZE' && grep -q 'no-op runner remains' '$FREEZE'"
check "AC02 conflict ledger declares zero open only with resolutions" bash -c "grep -q 'zero-open' '$FREEZE' && ! grep -q '| Yes |' '$FREEZE'"
check "AC03 all 83 W02-W15 stories have unique declarations" python3 -c '
import json
d=json.load(open("governance/feature-path-ownership.json"))
assert len(d["expected_story_keys"]) == 83
assert len(d["features"]) == 83
assert len({x["story_key"] for x in d["features"]}) == 83
'
check "AC03 deleting a story from both self-declared lists still fails closed" bash -c '
tmp=$(mktemp); python3 -c "import json; p=json.load(open(\"'$OWNERSHIP'\")); key=p[\"expected_story_keys\"].pop(); p[\"features\"]=[x for x in p[\"features\"] if x[\"story_key\"] != key]; print(json.dumps(p))" > "$tmp"
! python3 scripts/check-contract-freeze.py --ownership "$tmp" >/dev/null
rm -f "$tmp"
'
check "AC03 every story declares owned paths and all frozen roots read-only" python3 scripts/check-contract-freeze.py
check "AC03 no future story owns a frozen shared path" python3 -c '
import json
d=json.load(open("governance/feature-path-ownership.json"))
for row in d["features"]:
 assert row["read_only_shared_contracts"] == ["contracts/**","docs/adr/**","governance/contract-freeze/**","governance/feature-path-ownership.json","scripts/check-contract-freeze.py","docs/architecture/SHARED-CONTRACT-FREEZE.md"]
 assert not any(p.startswith(("contracts/","docs/adr/","governance/contract-freeze/")) for p in row["owned_paths"])
'
check "AC04 freeze requires ADR, impact review, new version and independent acceptance" bash -c "grep -q 'superseding or amending ADR' '$FREEZE' && grep -q 'impact review' '$FREEZE' && grep -q 'new snapshot version' '$FREEZE' && grep -q 'Independent review' '$FREEZE'"
check "AC04 existing snapshots are immutable, never rewritten" bash -c "grep -q 'never edited in place' '$FREEZE'"
check "every published schema remains valid JSON" bash -c "python3 -c 'import json,glob;[json.load(open(f)) for f in glob.glob(\"contracts/schemas/*.json\")]'"
check "contract behavior suite passes" node --test contracts/js/
check "tracked secret scan stays clean" bash scripts/secret-scan.sh --all

echo "=============================="
echo "$((total - failed))/$total checks passed"
[ "$failed" -eq 0 ]
