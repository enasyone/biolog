#!/usr/bin/env bash
# Developer tests mapped to BIO-FND-004-AC01..AC04.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

total=0
failed=0
scratch="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd004.XXXXXX")"
trap 'rm -rf "$scratch"' EXIT

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

# BIO-FND-004-AC01: canonical four-stage folder convention.
ac01_template_stages() {
  for stage in builder adversarial security acceptance; do
    [ -f "evidence/_template/$stage/README.md" ] || return 1
  done
}
check "AC01: template contains builder/adversarial/security/acceptance stages" ac01_template_stages

# BIO-FND-004-AC02: metadata carries every required trace field.
ac02_metadata_contract() {
  python3 - <<'PY'
import json
from pathlib import Path
data = json.loads(Path("evidence/_template/metadata.json").read_text())
required = {"story_key", "commit_hash", "test_command", "result", "timestamp", "reviewer"}
raise SystemExit(0 if required <= data.keys() else 1)
PY
}
check "AC02: template records all mandated metadata fields" ac02_metadata_contract

# BIO-FND-004-AC03: checker identifies absent and malformed evidence.
ac03_missing_story_fails() {
  ! python3 scripts/check-evidence.py BIO-FND-999 --required-stages builder >"$scratch/missing.out" 2>&1 \
    && grep -q "missing story evidence directory" "$scratch/missing.out"
}
check "AC03: checker reports a missing story directory" ac03_missing_story_fails

ac03_missing_stage_fails() {
  ! python3 scripts/check-evidence.py BIO-FND-001 --required-stages security >"$scratch/stage.out" 2>&1 \
    && grep -q "missing stage directory" "$scratch/stage.out"
}
check "AC03: checker reports a missing required stage" ac03_missing_stage_fails

ac03_missing_metadata_fails() {
  ! python3 scripts/check-evidence.py BIO-FND-001 --required-stages builder >"$scratch/metadata.out" 2>&1 \
    && grep -q "missing metadata" "$scratch/metadata.out"
}
check "AC03: checker reports missing stage metadata" ac03_missing_metadata_fails

ac02_ac03_rejects_stale_commit_and_failed_results() {
  CHECKER="$PWD/scripts/check-evidence.py" python3 - <<'PY'
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

repo = Path(tempfile.mkdtemp(prefix="biolog-fnd004-regression-"))
try:
    subprocess.run(["git", "init", "-q", "-b", "main"], cwd=repo, check=True)
    subprocess.run(["git", "config", "user.name", "Synthetic Tester"], cwd=repo, check=True)
    subprocess.run(["git", "config", "user.email", "synthetic@example.invalid"], cwd=repo, check=True)
    (repo / "baseline.txt").write_text("synthetic baseline\n")
    subprocess.run(["git", "add", "baseline.txt"], cwd=repo, check=True)
    subprocess.run(["git", "commit", "-qm", "synthetic baseline"], cwd=repo, check=True)
    old_commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    (repo / "reviewed.txt").write_text("synthetic reviewed content\n")
    subprocess.run(["git", "add", "reviewed.txt"], cwd=repo, check=True)
    subprocess.run(["git", "commit", "-qm", "synthetic reviewed commit"], cwd=repo, check=True)
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    scripts = repo / "scripts"
    scripts.mkdir()
    shutil.copy2(os.environ["CHECKER"], scripts / "check-evidence.py")
    story = "BIO-QA-900"
    stages = ("builder", "adversarial", "security", "acceptance")

    def write(stage, result="PASS", commit=head, classification="SYNTHETIC"):
        target = repo / "evidence" / story / stage
        target.mkdir(parents=True, exist_ok=True)
        target.joinpath("metadata.json").write_text(json.dumps({
            "story_key": story,
            "commit_hash": commit,
            "test_command": f"synthetic-check --stage {stage}",
            "result": result,
            "timestamp": "2026-09-20T15:00:00Z",
            "reviewer": "Synthetic Tester",
            "data_classification": classification,
        }))

    def check():
        return subprocess.run(
            ["python3", "scripts/check-evidence.py", story], cwd=repo,
            text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        )

    for stage in stages:
        write(stage)
    assert check().returncode == 0

    write("security", commit=old_commit)
    stale = check()
    assert stale.returncode != 0 and "does not match commit under review" in stale.stdout
    write("security")

    for stage in stages:
        write(stage, result="FAIL")
    failed = check()
    assert failed.returncode != 0 and "result must be PASS" in failed.stdout

    for stage in stages:
        write(stage)
    (repo / "evidence" / story / "security" / "metadata.json").write_text("{ malformed")
    assert check().returncode != 0
    write("security", commit="0" * 40)
    assert check().returncode != 0
    write("security", classification="PRODUCTION")
    assert check().returncode != 0
finally:
    shutil.rmtree(repo)
PY
}
check "AC02/AC03 regressions: stale commits and FAIL results are rejected" ac02_ac03_rejects_stale_commit_and_failed_results

# BIO-FND-004-AC04: synthetic classification is explicit and enforced.
ac04_synthetic_marked() {
  python3 - <<'PY'
import json
from pathlib import Path
data = json.loads(Path("evidence/_template/metadata.json").read_text())
raise SystemExit(0 if data.get("data_classification") == "SYNTHETIC" else 1)
PY
}
check "AC04: template explicitly marks synthetic test data" ac04_synthetic_marked

ac04_checker_enforces_classification() {
  grep -q 'data_classification.*SYNTHETIC.*NO_TEST_DATA' scripts/check-evidence.py
}
check "AC04: checker enforces the permitted data classifications" ac04_checker_enforces_classification

echo "=============================="
echo "$((total - failed))/$total checks passed"
[ "$failed" -eq 0 ]
