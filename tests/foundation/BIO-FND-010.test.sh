#!/usr/bin/env bash
# Developer tests mapped to BIO-FND-010-AC01..AC04.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

total=0
failed=0
scratch=$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd010.XXXXXX") || exit 1
[ -n "$scratch" ] && [ -d "$scratch" ] || exit 1
trap 'rm -rf "$scratch"' EXIT

check() {
  local label="$1"; shift
  total=$((total + 1))
  if "$@"; then echo "PASS  $label"; else echo "FAIL  $label" >&2; failed=$((failed + 1)); fi
}

SPEC=governance/stories/BIO-FND-010.json
CHECKER=scripts/check-traceability.py

check "BIO-FND-010-AC01 story spec exposes stable requirement and AC IDs" python3 - <<'PY'
import json
d=json.load(open("governance/stories/BIO-FND-010.json"))
assert d["requirement_ids"] == ["BIO-FND-010-REQ-001"]
assert [x["id"] for x in d["acceptance_criteria"]] == [f"BIO-FND-010-AC0{x}" for x in range(1,5)]
assert all(x["requirement_ids"] == d["requirement_ids"] for x in d["acceptance_criteria"])
PY

check "BIO-FND-010-AC02 tests declare the AC IDs they cover" python3 - <<'PY'
import json
d=json.load(open("governance/stories/BIO-FND-010.json"))
assert d["tests"][0]["path"] == "tests/foundation/BIO-FND-010.test.sh"
assert set(d["tests"][0]["ac_ids"]) == {x["id"] for x in d["acceptance_criteria"]}
PY

make_fixture() {
  local target="$1"
  mkdir -p "$target/scripts" "$target/tests" "$target/evidence/builder" "$target/evidence/adversarial"
  cp "$CHECKER" "$target/scripts/check-traceability.py"
  printf 'synthetic test\n' > "$target/tests/story.test"
  git -C "$target" init -q -b main
  git -C "$target" config user.name "Synthetic Tester"
  git -C "$target" config user.email "synthetic@example.invalid"
  git -C "$target" add scripts tests
  git -C "$target" commit -qm "synthetic reviewed commit"
  local commit
  commit=$(git -C "$target" rev-parse HEAD) || return 1
  cat > "$target/story.json" <<'JSON'
{
  "schema_version": 1,
  "story_key": "BIO-QA-900",
  "title": "Synthetic trace fixture",
  "owner": "Builder",
  "reviewed_commit": "REVIEWED_COMMIT",
  "required_acceptance_stages": ["builder", "adversarial"],
  "requirement_ids": ["BIO-QA-900-REQ-001"],
  "acceptance_criteria": [
    {"id":"BIO-QA-900-AC01","requirement_ids":["BIO-QA-900-REQ-001"],"text":"Synthetic criterion"}
  ],
  "tests": [{"path":"tests/story.test","ac_ids":["BIO-QA-900-AC01"]}],
  "evidence": [
    {"path":"evidence/builder/metadata.json","stage":"builder","ac_ids":["BIO-QA-900-AC01"]},
    {"path":"evidence/adversarial/metadata.json","stage":"adversarial","ac_ids":["BIO-QA-900-AC01"]}
  ]
}
JSON
  sed -i.bak "s/REVIEWED_COMMIT/$commit/" "$target/story.json" && rm "$target/story.json.bak"
  python3 - "$target" "$commit" <<'PY'
import json, pathlib, sys
root=pathlib.Path(sys.argv[1]); commit=sys.argv[2]
for stage, reviewer in (("builder","Builder"),("adversarial","Independent")):
    (root/"evidence"/stage/"metadata.json").write_text(json.dumps({
      "story_key":"BIO-QA-900","stage":stage,"commit_hash":commit,"test_command":"synthetic-check",
      "result":"PASS","timestamp":"2026-09-21T00:00:00Z","reviewer":reviewer,
      "data_classification":"SYNTHETIC"
    }))
PY
}

ac03_reports_missing_test_and_evidence() {
  local fixture="$scratch/missing"
  make_fixture "$fixture"
  rm "$fixture/tests/story.test" "$fixture/evidence/builder/metadata.json" "$fixture/evidence/adversarial/metadata.json"
  local output
  output=$(python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" 2>&1)
  local rc=$?
  [ "$rc" -ne 0 ] && grep -q "missing test" <<<"$output" && grep -q "missing evidence" <<<"$output" \
    && grep -q "ACCEPTANCE: BLOCKED" <<<"$output"
}
check "BIO-FND-010-AC03 reports missing tests and evidence as blocking gaps" ac03_reports_missing_test_and_evidence

ac03_reports_uncovered_and_unknown_ids() {
  local fixture="$scratch/links"
  make_fixture "$fixture"
  python3 - "$fixture/story.json" <<'PY'
import json, pathlib, sys
p=pathlib.Path(sys.argv[1]); d=json.loads(p.read_text())
d["tests"][0]["ac_ids"]=["BIO-QA-900-AC99"]
p.write_text(json.dumps(d))
PY
  local output
  output=$(python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" 2>&1)
  [ "$?" -ne 0 ] && grep -q "unknown AC IDs" <<<"$output" && grep -q "missing test coverage" <<<"$output"
}
check "BIO-FND-010-AC03 reports unknown and uncovered AC links" ac03_reports_uncovered_and_unknown_ids

qa01_invalid_evidence_cannot_authorize_acceptance() {
  local fixture="$scratch/invalid-evidence"
  make_fixture "$fixture"
  python3 - "$fixture/evidence/adversarial/metadata.json" <<'PY'
import json, pathlib, sys
p=pathlib.Path(sys.argv[1]); d=json.loads(p.read_text())
d.update(story_key="BIO-OTHER-999", commit_hash="0"*40, result="FAIL", reviewer="Builder")
p.write_text(json.dumps(d))
PY
  local output
  output=$(python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" \
    --record-acceptance decision.json --actor Acceptor 2>&1)
  [ "$?" -ne 0 ] && [ ! -e "$fixture/decision.json" ] \
    && grep -q "evidence story_key mismatch" <<<"$output" \
    && grep -q "evidence result must be PASS" <<<"$output"
}
check "BIO-FND-010-AC03/AC04 failed stale builder-only evidence blocks acceptance" qa01_invalid_evidence_cannot_authorize_acceptance

qa02_inventory_reports_omitted_implemented_story() {
  local fixture="$scratch/inventory"
  mkdir -p "$fixture/scripts" "$fixture/tests/foundation" "$fixture/governance/stories"
  cp "$CHECKER" "$fixture/scripts/check-traceability.py"
  printf '# synthetic implemented story\n' > "$fixture/tests/foundation/BIO-QA-901.test.sh"
  local output
  output=$(python3 "$fixture/scripts/check-traceability.py" --root "$fixture" --all 2>&1)
  [ "$?" -ne 0 ] && grep -q "implemented story has no story spec: BIO-QA-901" <<<"$output"
}
check "BIO-FND-010-AC01/AC03 inventory reports an omitted implemented story" qa02_inventory_reports_omitted_implemented_story

ac04_gaps_cannot_write_acceptance() {
  local fixture="$scratch/blocked"
  make_fixture "$fixture"
  rm "$fixture/evidence/adversarial/metadata.json"
  ! python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" \
      --record-acceptance decision.json --actor Acceptor >/dev/null \
    && [ ! -e "$fixture/decision.json" ]
}
check "BIO-FND-010-AC04 unresolved gaps cannot create an acceptance record" ac04_gaps_cannot_write_acceptance

ac04_owner_cannot_self_accept() {
  local fixture="$scratch/self"
  make_fixture "$fixture"
  ! python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" \
      --record-acceptance decision.json --actor Builder >/dev/null \
    && [ ! -e "$fixture/decision.json" ] \
    && ! python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" \
      --record-acceptance decision.json --actor " Builder " >/dev/null \
    && [ ! -e "$fixture/decision.json" ]
}
check "BIO-FND-010-AC04 story owner cannot self-accept" ac04_owner_cannot_self_accept

ac04_story_cannot_remove_independent_review() {
  local fixture="$scratch/weakened-policy"
  make_fixture "$fixture"
  python3 - "$fixture/story.json" <<'PY'
import json, pathlib, sys
p=pathlib.Path(sys.argv[1]); d=json.loads(p.read_text())
d["required_acceptance_stages"]=["builder"]
d["evidence"]=[x for x in d["evidence"] if x["stage"] == "builder"]
p.write_text(json.dumps(d))
PY
  local output
  output=$(python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" \
    --record-acceptance decision.json --actor Acceptor 2>&1)
  [ "$?" -ne 0 ] && [ ! -e "$fixture/decision.json" ] \
    && grep -q "cannot remove mandatory acceptance stages" <<<"$output" \
    && grep -q "missing valid required evidence stage: adversarial" <<<"$output"
}
check "BIO-FND-010-AC04 story spec cannot remove independent review" ac04_story_cannot_remove_independent_review

ac04_one_evidence_identity_cannot_satisfy_two_stages() {
  local fixture="$scratch/duplicate-stage"
  make_fixture "$fixture"
  python3 - "$fixture/story.json" <<'PY'
import json, pathlib, sys
p=pathlib.Path(sys.argv[1]); d=json.loads(p.read_text())
d["evidence"][1]["path"]=d["evidence"][0]["path"]
p.write_text(json.dumps(d))
PY
  local output
  output=$(python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" \
    --record-acceptance decision.json --actor Acceptor 2>&1)
  [ "$?" -ne 0 ] && [ ! -e "$fixture/decision.json" ] \
    && grep -q "evidence file cannot satisfy multiple declarations" <<<"$output" \
    && grep -q "metadata stage does not match declaration" <<<"$output"
}
check "BIO-FND-010-AC04 one evidence identity cannot satisfy two stages" ac04_one_evidence_identity_cannot_satisfy_two_stages

ac04_clean_independent_acceptance_is_exclusive() {
  local fixture="$scratch/pass"
  make_fixture "$fixture"
  python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" \
      --record-acceptance decision.json --actor Acceptor >/dev/null \
    && python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); assert d["decision"]=="PASS" and d["actor"]=="Acceptor"' "$fixture/decision.json" \
    && ! python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" \
      --record-acceptance decision.json --actor Other >/dev/null
}
check "BIO-FND-010-AC04 clean independent acceptance records once without overwrite" ac04_clean_independent_acceptance_is_exclusive

ac03_path_escape_fails_closed() {
  local fixture="$scratch/escape"
  make_fixture "$fixture"
  python3 - "$fixture/story.json" <<'PY'
import json, pathlib, sys
p=pathlib.Path(sys.argv[1]); d=json.loads(p.read_text()); d["tests"][0]["path"]="../outside"
p.write_text(json.dumps(d))
PY
  local output
  output=$(python3 "$fixture/scripts/check-traceability.py" "$fixture/story.json" --root "$fixture" 2>&1)
  [ "$?" -ne 0 ] && grep -q "path escapes repository" <<<"$output"
}
check "BIO-FND-010-AC03 untrusted paths cannot escape the repository" ac03_path_escape_fails_closed

check "BIO-FND-010-AC01-AC04 committed story graph validates" python3 "$CHECKER" "$SPEC"
check "BIO-FND-010-AC01 repository-wide implemented-story inventory validates" python3 "$CHECKER" --all
check "regression: tracked secret scan stays clean" bash scripts/secret-scan.sh --all

echo "=============================="
echo "$((total - failed))/$total checks passed"
[ "$failed" -eq 0 ]
