#!/usr/bin/env python3
"""Validate the BIOLOG W01 shared-contract freeze without network access."""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
from pathlib import Path


FROZEN_GLOBS = ("contracts/**/*", "docs/adr/ADR-000[1-6]-*.md")
EXPECTED_BASELINE_COMMIT = "2f6826a40db537753f3d91fae867a2b26a518308"
REQUIRED_READ_ONLY = (
    "contracts/**",
    "docs/adr/**",
    "governance/contract-freeze/**",
    "governance/feature-path-ownership.json",
    "scripts/check-contract-freeze.py",
    "docs/architecture/SHARED-CONTRACT-FREEZE.md",
)
SURFACES = ("api", "web", "mobile", "tests", "docs", "evidence")
EXPECTED_ROSTER_SHA256 = "f676fb2d8e98f0492566a990996949f4623103eb13e48fad65afba8f95dca933"


class FreezeError(RuntimeError):
    pass


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def frozen_files(root: Path) -> list[Path]:
    found: set[Path] = set()
    for pattern in FROZEN_GLOBS:
        found.update(path for path in root.glob(pattern) if path.is_file())
    return sorted(found)


def git(root: Path, *args: str) -> bytes:
    result = subprocess.run(
        ["git", *args], cwd=root, capture_output=True, check=False
    )
    if result.returncode:
        detail = result.stderr.decode(errors="replace").strip()
        raise FreezeError(f"git {' '.join(args)} failed: {detail}")
    return result.stdout


def validate_baseline(root: Path, baseline: object, entries: list[dict]) -> None:
    if baseline != EXPECTED_BASELINE_COMMIT:
        raise FreezeError("baseline_parent_commit differs from the accepted W01 baseline")
    git(root, "cat-file", "-e", f"{baseline}^{{commit}}")
    git(root, "merge-base", "--is-ancestor", str(baseline), "HEAD")
    for entry in entries:
        relative = entry.get("path")
        if not isinstance(relative, str):
            raise FreezeError("snapshot artifact path must be a string")
        baseline_bytes = git(root, "show", f"{baseline}:{relative}")
        baseline_hash = hashlib.sha256(baseline_bytes).hexdigest()
        if entry.get("sha256") != baseline_hash or entry.get("bytes") != len(baseline_bytes):
            raise FreezeError(f"snapshot differs from accepted baseline commit: {relative}")


def validate_snapshot(root: Path, manifest_path: Path) -> dict:
    manifest = json.loads(manifest_path.read_text())
    if manifest.get("snapshot_version") != "1.1.12":
        raise FreezeError("snapshot_version must be 1.1.12")
    entries = manifest.get("artifacts")
    if not isinstance(entries, list) or not entries:
        raise FreezeError("snapshot artifacts must be a non-empty list")
    validate_baseline(root, manifest.get("baseline_parent_commit"), entries)
    actual = {path.relative_to(root).as_posix(): path for path in frozen_files(root)}
    declared = {entry.get("path"): entry for entry in entries}
    if set(actual) != set(declared):
        missing = sorted(set(actual) - set(declared))
        stale = sorted(set(declared) - set(actual))
        raise FreezeError(f"snapshot file set differs: missing={missing}, stale={stale}")
    for relative, path in actual.items():
        entry = declared[relative]
        if entry.get("sha256") != sha256(path):
            raise FreezeError(f"snapshot hash mismatch: {relative}")
        if entry.get("bytes") != path.stat().st_size:
            raise FreezeError(f"snapshot size mismatch: {relative}")
    return manifest


def standard_owned_paths(story_key: str) -> list[str]:
    token = story_key.lower()
    return [f"{surface}/features/{token}/**" for surface in SURFACES]


def validate_ownership(path: Path, snapshot_version: str) -> dict:
    ownership = json.loads(path.read_text())
    if ownership.get("contract_snapshot_version") != snapshot_version:
        raise FreezeError("ownership declarations do not bind the current snapshot")
    expected = ownership.get("expected_story_keys")
    declarations = ownership.get("features")
    if not isinstance(expected, list) or not expected or len(expected) != len(set(expected)):
        raise FreezeError("expected_story_keys must be a non-empty unique list")
    if not isinstance(declarations, list):
        raise FreezeError("features must be a list")
    by_key = {row.get("story_key"): row for row in declarations}
    if set(by_key) != set(expected) or len(by_key) != len(declarations):
        raise FreezeError("every expected feature must have exactly one declaration")
    roster = "\n".join(
        "|".join((row["story_key"], row["wave"], row["owner"], row["conflict_group"]))
        for row in sorted(declarations, key=lambda item: item["story_key"])
    ) + "\n"
    if hashlib.sha256(roster.encode()).hexdigest() != EXPECTED_ROSTER_SHA256:
        raise FreezeError("future-story roster differs from the approved W02-W15 plan")
    claimed: dict[str, str] = {}
    for key in expected:
        row = by_key[key]
        if not row.get("wave") or not row.get("owner"):
            raise FreezeError(f"{key}: wave and owner are required")
        owned = row.get("owned_paths")
        if owned != standard_owned_paths(key):
            raise FreezeError(f"{key}: owned_paths must be the six standard story-scoped paths")
        read_only = row.get("read_only_shared_contracts")
        if read_only != list(REQUIRED_READ_ONLY):
            raise FreezeError(f"{key}: read-only shared contracts are incomplete or reordered")
        for owned_path in owned:
            if owned_path in claimed:
                raise FreezeError(f"owned path overlap: {owned_path} ({claimed[owned_path]}, {key})")
            if any(
                owned_path == protected.removesuffix("/**")
                or owned_path.startswith(protected.removesuffix("**"))
                for protected in REQUIRED_READ_ONLY
            ):
                raise FreezeError(f"{key}: frozen shared path declared writable: {owned_path}")
            claimed[owned_path] = key
    return ownership


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--snapshot", type=Path)
    parser.add_argument("--ownership", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    snapshot_path = args.snapshot or root / "governance/contract-freeze/v1.1.12/manifest.json"
    ownership_path = args.ownership or root / "governance/feature-path-ownership.json"
    snapshot = validate_snapshot(root, snapshot_path)
    ownership = validate_ownership(ownership_path, snapshot["snapshot_version"])
    print(f"contract-freeze: PASS ({len(snapshot['artifacts'])} artifacts, {len(ownership['features'])} future stories)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (FreezeError, OSError, json.JSONDecodeError) as error:
        print(f"contract-freeze: FAIL: {error}")
        raise SystemExit(1)
