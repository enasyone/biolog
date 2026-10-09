#!/usr/bin/env python3
import hashlib
import json
import os
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]


def run(root: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["python3", "scripts/check-contract-freeze.py", "--root", str(root)],
        cwd=root, text=True, capture_output=True, check=False,
    )


def copy_fixture() -> Path:
    parent = Path(tempfile.mkdtemp(prefix="bio-arc-006-"))
    target = parent / "repo"
    result = subprocess.run(
        ["git", "clone", "--quiet", "--no-hardlinks", str(ROOT), str(target)],
        text=True, capture_output=True, check=False,
    )
    assert result.returncode == 0, result.stderr
    return target


failures = 0


def probe(name, function):
    global failures
    try:
        function()
        print(f"PASS {name}")
    except AssertionError as error:
        failures += 1
        print(f"FAIL {name}: {error}")


probe("baseline freeze validates", lambda: (
    (_ for _ in ()).throw(AssertionError(run(ROOT).stdout + run(ROOT).stderr))
    if run(ROOT).returncode != 0 else None
))


def bogus_commit_must_fail():
    fixture = copy_fixture()
    manifest_path = fixture / "governance/contract-freeze/v1.0.0/manifest.json"
    manifest = json.loads(manifest_path.read_text())
    manifest["baseline_parent_commit"] = "0" * 40
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    result = run(fixture)
    assert result.returncode != 0, "checker accepted a nonexistent all-zero baseline commit"


probe("AC01 rejects a nonexistent baseline commit", bogus_commit_must_fail)


def rewritten_v1_must_fail():
    fixture = copy_fixture()
    contract = fixture / "contracts/README.md"
    contract.write_text(contract.read_text() + "\npost-freeze incompatible rewrite\n")
    manifest_path = fixture / "governance/contract-freeze/v1.0.0/manifest.json"
    manifest = json.loads(manifest_path.read_text())
    entry = next(item for item in manifest["artifacts"] if item["path"] == "contracts/README.md")
    entry["bytes"] = contract.stat().st_size
    entry["sha256"] = hashlib.sha256(contract.read_bytes()).hexdigest()
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    result = run(fixture)
    assert result.returncode != 0, "checker accepted a rewritten contract and regenerated v1.0.0 manifest"


probe("AC04 rejects in-place regeneration of an existing snapshot version", rewritten_v1_must_fail)


def governance_files_must_be_read_only():
    ownership = json.loads((ROOT / "governance/feature-path-ownership.json").read_text())
    required = {
        "governance/feature-path-ownership.json",
        "scripts/check-contract-freeze.py",
        "docs/architecture/SHARED-CONTRACT-FREEZE.md",
    }
    for feature in ownership["features"]:
        declared = set(feature["read_only_shared_contracts"])
        missing = required - declared
        assert not missing, f"{feature['story_key']} can write freeze governance files: {sorted(missing)}"


probe("AC03 makes freeze governance and ownership enforcement read-only", governance_files_must_be_read_only)


def tamper_probe_setup_failure_must_fail():
    shim_dir = Path(tempfile.mkdtemp(prefix="bio-arc-006-git-shim-"))
    real_git = subprocess.run(["which", "git"], text=True, capture_output=True, check=True).stdout.strip()
    shim = shim_dir / "git"
    shim.write_text(
        "#!/bin/sh\n"
        "if [ \"$1\" = clone ]; then exit 42; fi\n"
        f"exec {real_git} \"$@\"\n"
    )
    shim.chmod(0o755)
    environment = dict(os.environ)
    environment["PATH"] = f"{shim_dir}:{environment['PATH']}"
    result = subprocess.run(
        ["bash", "tests/foundation/BIO-ARC-006.test.sh"], cwd=ROOT,
        env=environment, text=True, capture_output=True, check=False,
    )
    false_success = result.returncode == 0 and "PASS  AC04 a rewritten contract and regenerated v1 manifest fail closed" in result.stdout
    assert not false_success, "foundation suite passed the AC04 tamper test even though git clone was forced to fail"


probe("AC04 tamper probe fails when its clone/setup fails", tamper_probe_setup_failure_must_fail)

raise SystemExit(1 if failures else 0)
