#!/usr/bin/env python3
"""Validate BIOLOG story evidence metadata and stage structure."""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import subprocess
import sys
from pathlib import Path

STAGES = ("builder", "adversarial", "security", "acceptance")
REQUIRED_FIELDS = (
    "story_key",
    "commit_hash",
    "test_command",
    "result",
    "timestamp",
    "reviewer",
    "data_classification",
)
STORY_RE = re.compile(r"^[A-Z][A-Z0-9]*-[A-Z][A-Z0-9]*-[0-9]{3,}$")
HASH_RE = re.compile(r"^[0-9a-f]{40}$")


def commit_exists(repo: Path, commit_hash: str) -> bool:
    return subprocess.run(
        ["git", "cat-file", "-e", f"{commit_hash}^{{commit}}"],
        cwd=repo,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        check=False,
    ).returncode == 0


def valid_timestamp(value: object) -> bool:
    if not isinstance(value, str) or not value.endswith("Z"):
        return False
    try:
        dt.datetime.fromisoformat(value[:-1] + "+00:00")
    except ValueError:
        return False
    return True


def resolve_commit(repo: Path, commitish: str) -> str:
    return subprocess.check_output(
        ["git", "rev-parse", "--verify", f"{commitish}^{{commit}}"],
        cwd=repo,
        text=True,
        stderr=subprocess.DEVNULL,
    ).strip()


def validate(story: str, stages: list[str], repo: Path, expected_commit: str) -> list[str]:
    gaps: list[str] = []
    if not STORY_RE.fullmatch(story):
        return [f"invalid story key: {story}"]

    story_dir = repo / "evidence" / story
    if not story_dir.is_dir():
        return [f"missing story evidence directory: evidence/{story}"]

    for stage in stages:
        stage_dir = story_dir / stage
        relative = stage_dir.relative_to(repo)
        if not stage_dir.is_dir():
            gaps.append(f"missing stage directory: {relative}")
            continue
        metadata_path = stage_dir / "metadata.json"
        if not metadata_path.is_file():
            gaps.append(f"missing metadata: {metadata_path.relative_to(repo)}")
            continue
        try:
            metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            gaps.append(f"invalid metadata JSON: {metadata_path.relative_to(repo)} ({error})")
            continue
        if not isinstance(metadata, dict):
            gaps.append(f"metadata must be a JSON object: {metadata_path.relative_to(repo)}")
            continue
        for field in REQUIRED_FIELDS:
            if field not in metadata or not isinstance(metadata[field], str) or not metadata[field].strip():
                gaps.append(f"missing metadata field {field}: {metadata_path.relative_to(repo)}")
        if metadata.get("story_key") != story:
            gaps.append(f"story_key mismatch in {metadata_path.relative_to(repo)}")
        commit_hash = metadata.get("commit_hash")
        if isinstance(commit_hash, str):
            if not HASH_RE.fullmatch(commit_hash):
                gaps.append(f"commit_hash must be 40 lowercase hex characters: {metadata_path.relative_to(repo)}")
            elif not commit_exists(repo, commit_hash):
                gaps.append(f"commit_hash does not resolve locally: {metadata_path.relative_to(repo)}")
            elif commit_hash != expected_commit:
                gaps.append(
                    f"commit_hash does not match commit under review ({expected_commit}): "
                    f"{metadata_path.relative_to(repo)}"
                )
        if metadata.get("result") != "PASS":
            gaps.append(f"result must be PASS for required evidence: {metadata_path.relative_to(repo)}")
        if not valid_timestamp(metadata.get("timestamp")):
            gaps.append(f"timestamp must be UTC RFC 3339 ending Z: {metadata_path.relative_to(repo)}")
        if metadata.get("data_classification") not in {"SYNTHETIC", "NO_TEST_DATA"}:
            gaps.append(f"data_classification must mark synthetic usage: {metadata_path.relative_to(repo)}")
    return gaps


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("story_key")
    parser.add_argument(
        "--commit",
        default="HEAD",
        help="commit under review (default: HEAD)",
    )
    parser.add_argument(
        "--required-stages",
        nargs="+",
        choices=STAGES,
        default=list(STAGES),
        metavar="STAGE",
    )
    args = parser.parse_args()
    repo = Path(subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip())
    try:
        expected_commit = resolve_commit(repo, args.commit)
    except subprocess.CalledProcessError:
        print(f"EVIDENCE CHECK: FAIL (invalid commit under review: {args.commit})")
        return 1
    gaps = validate(args.story_key, args.required_stages, repo, expected_commit)
    if gaps:
        print(f"EVIDENCE CHECK: FAIL ({len(gaps)} gap(s))")
        for gap in gaps:
            print(f"- {gap}")
        return 1
    print(f"EVIDENCE CHECK: PASS ({args.story_key}; stages: {', '.join(args.required_stages)})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
