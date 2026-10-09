#!/usr/bin/env python3
"""Independent adversarial probes for BIO-FND-010."""

from __future__ import annotations

import json
import subprocess
import tempfile
from pathlib import Path


REPO = Path(__file__).resolve().parents[3]
CHECKER = REPO / "scripts/check-traceability.py"


def run(root: Path, spec: dict, *, accept: bool = False) -> subprocess.CompletedProcess[str]:
    spec_path = root / "story.json"
    spec_path.write_text(json.dumps(spec), encoding="utf-8")
    command = ["python3", str(CHECKER), str(spec_path), "--root", str(root)]
    if accept:
        command += ["--record-acceptance", "decision.json", "--actor", "Independent"]
    return subprocess.run(command, text=True, capture_output=True, check=False)


def fixture(root: Path) -> dict:
    (root / "tests").mkdir()
    (root / "evidence").mkdir()
    (root / "tests/test.sh").write_text("# no AC declaration and never executed\n", encoding="utf-8")
    return {
        "schema_version": 1,
        "story_key": "BIO-QA-900",
        "owner": "Builder",
        "requirement_ids": ["BIO-QA-900-REQ-001"],
        "acceptance_criteria": [
            {
                "id": "BIO-QA-900-AC01",
                "requirement_ids": ["BIO-QA-900-REQ-001"],
                "text": "Synthetic criterion",
            }
        ],
        "tests": [{"path": "tests/test.sh", "ac_ids": ["BIO-QA-900-AC01"]}],
        "evidence": [{"path": "evidence/result.json", "ac_ids": ["BIO-QA-900-AC01"]}],
    }


failures = 0

with tempfile.TemporaryDirectory(prefix="bio-fnd-010-review-") as tmp:
    root = Path(tmp)
    spec = fixture(root)

    # AC03 baseline: genuinely absent evidence is reported and blocks recording.
    result = run(root, spec, accept=True)
    ok = result.returncode != 0 and not (root / "decision.json").exists()
    print(("PASS" if ok else "FAIL"), "missing evidence blocks acceptance")
    failures += not ok

    # An evidence manifest explicitly says FAIL, points to the wrong commit, and is
    # builder-only. It is nevertheless accepted solely because the file is non-empty.
    (root / "evidence/result.json").write_text(
        json.dumps(
            {
                "story_key": "BIO-OTHER-999",
                "commit_hash": "0" * 40,
                "test_command": "false",
                "result": "FAIL",
                "reviewer": "Builder",
            }
        ),
        encoding="utf-8",
    )
    result = run(root, spec, accept=True)
    unsafe = result.returncode == 0 and (root / "decision.json").exists()
    print(("FAIL" if unsafe else "PASS"), "failed/stale/builder-only evidence cannot authorize acceptance")
    failures += unsafe

implemented = sorted(path.stem.removesuffix(".test") for path in (REPO / "tests/foundation").glob("*.test.sh"))
declared = sorted(path.stem for path in (REPO / "governance/stories").glob("*.json"))
global_gap = len(implemented) > len(declared) and declared == ["BIO-FND-010"]
print(
    ("FAIL" if global_gap else "PASS"),
    f"every implemented story is inventoried (implemented={len(implemented)}, story_specs={len(declared)})",
)
failures += global_gap

# AC04: the story being accepted must not get to weaken its own required-review
# policy. Removing the adversarial requirement and declaration must not let the
# owner's builder evidence authorize acceptance.
with tempfile.TemporaryDirectory(prefix="bio-fnd-010-policy-") as tmp:
    root = Path(tmp)
    (root / "tests").mkdir()
    (root / "evidence").mkdir()
    (root / "tests/test.sh").write_text("synthetic test\n", encoding="utf-8")
    subprocess.run(["git", "init", "-q", "-b", "main"], cwd=root, check=True)
    subprocess.run(["git", "config", "user.name", "Synthetic"], cwd=root, check=True)
    subprocess.run(["git", "config", "user.email", "synthetic@example.invalid"], cwd=root, check=True)
    subprocess.run(["git", "add", "tests/test.sh"], cwd=root, check=True)
    subprocess.run(["git", "commit", "-qm", "reviewed"], cwd=root, check=True)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    metadata = {
        "story_key": "BIO-QA-900",
        "commit_hash": commit,
        "test_command": "synthetic-check",
        "result": "PASS",
        "reviewer": "Builder",
    }
    (root / "evidence/builder.json").write_text(json.dumps(metadata), encoding="utf-8")
    spec = {
        "schema_version": 1,
        "story_key": "BIO-QA-900",
        "owner": "Builder",
        "reviewed_commit": commit,
        "required_acceptance_stages": ["builder"],
        "requirement_ids": ["BIO-QA-900-REQ-001"],
        "acceptance_criteria": [{
            "id": "BIO-QA-900-AC01",
            "requirement_ids": ["BIO-QA-900-REQ-001"],
            "text": "Synthetic criterion",
        }],
        "tests": [{"path": "tests/test.sh", "ac_ids": ["BIO-QA-900-AC01"]}],
        "evidence": [{
            "path": "evidence/builder.json",
            "stage": "builder",
            "ac_ids": ["BIO-QA-900-AC01"],
        }],
    }
    result = run(root, spec, accept=True)
    weakened = result.returncode == 0 and (root / "decision.json").exists()
    print(("FAIL" if weakened else "PASS"), "story cannot remove its own independent-review requirement")
    failures += weakened

# A single metadata file and reviewer must not be reusable as two allegedly
# independent stages merely by declaring the same path twice under new labels.
with tempfile.TemporaryDirectory(prefix="bio-fnd-010-stage-alias-") as tmp:
    root = Path(tmp)
    (root / "tests").mkdir()
    (root / "evidence").mkdir()
    (root / "tests/test.sh").write_text("synthetic test\n", encoding="utf-8")
    subprocess.run(["git", "init", "-q", "-b", "main"], cwd=root, check=True)
    subprocess.run(["git", "config", "user.name", "Synthetic"], cwd=root, check=True)
    subprocess.run(["git", "config", "user.email", "synthetic@example.invalid"], cwd=root, check=True)
    subprocess.run(["git", "add", "tests/test.sh"], cwd=root, check=True)
    subprocess.run(["git", "commit", "-qm", "reviewed"], cwd=root, check=True)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    metadata_path = root / "evidence/one.json"
    metadata_path.write_text(json.dumps({
        "story_key": "BIO-QA-900",
        "commit_hash": commit,
        "test_command": "one-check",
        "result": "PASS",
        "reviewer": "One Reviewer",
    }), encoding="utf-8")
    base = {
        "path": "evidence/one.json",
        "ac_ids": ["BIO-QA-900-AC01"],
    }
    spec = {
        "schema_version": 1,
        "story_key": "BIO-QA-900",
        "owner": "Builder",
        "reviewed_commit": commit,
        "required_acceptance_stages": ["builder", "adversarial"],
        "requirement_ids": ["BIO-QA-900-REQ-001"],
        "acceptance_criteria": [{
            "id": "BIO-QA-900-AC01",
            "requirement_ids": ["BIO-QA-900-REQ-001"],
            "text": "Synthetic criterion",
        }],
        "tests": [{"path": "tests/test.sh", "ac_ids": ["BIO-QA-900-AC01"]}],
        "evidence": [base | {"stage": "builder"}, base | {"stage": "adversarial"}],
    }
    result = run(root, spec, accept=True)
    aliased = result.returncode == 0 and (root / "decision.json").exists()
    print(("FAIL" if aliased else "PASS"), "one evidence artifact cannot impersonate multiple required stages")
    failures += aliased

raise SystemExit(1 if failures else 0)
