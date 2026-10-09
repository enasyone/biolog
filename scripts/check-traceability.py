#!/usr/bin/env python3
"""Validate story requirement/AC/test/evidence traceability and gate acceptance."""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import re
import subprocess
import sys
from pathlib import Path

STORY_RE = re.compile(r"^[A-Z][A-Z0-9]*-[A-Z][A-Z0-9]*-[0-9]{3,}$")
HASH_RE = re.compile(r"^[0-9a-f]{40}$")
EVIDENCE_STAGES = {"builder", "adversarial", "security", "acceptance"}
MANDATORY_ACCEPTANCE_STAGES = {"builder", "adversarial"}


def safe_file(root: Path, value: object, label: str, gaps: list[str]) -> Path | None:
    if not isinstance(value, str) or not value or Path(value).is_absolute():
        gaps.append(f"{label} path must be a non-empty repository-relative path")
        return None
    candidate = (root / value).resolve()
    try:
        candidate.relative_to(root)
    except ValueError:
        gaps.append(f"{label} path escapes repository: {value}")
        return None
    if not candidate.is_file():
        gaps.append(f"missing {label}: {value}")
        return None
    if candidate.stat().st_size == 0:
        gaps.append(f"empty {label}: {value}")
        return None
    return candidate


def validate_evidence_metadata(
    root: Path, path: Path, story: str, reviewed_commit: object, owner: str, stage: object
) -> list[str]:
    gaps: list[str] = []
    if stage not in EVIDENCE_STAGES:
        gaps.append(f"evidence stage is missing or invalid: {path}")
    try:
        metadata = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        return [f"invalid evidence metadata JSON: {path} ({error})"]
    if not isinstance(metadata, dict):
        return [f"evidence metadata must be an object: {path}"]
    if metadata.get("stage") != stage:
        gaps.append(f"evidence metadata stage does not match declaration ({stage}): {path}")
    if metadata.get("story_key") != story:
        gaps.append(f"evidence story_key mismatch: {path}")
    commit = metadata.get("commit_hash")
    if not isinstance(reviewed_commit, str) or not HASH_RE.fullmatch(reviewed_commit) or reviewed_commit == "0" * 40:
        gaps.append("reviewed_commit must be 40 lowercase hex characters")
    elif commit != reviewed_commit:
        gaps.append(f"evidence commit does not match reviewed_commit ({reviewed_commit}): {path}")
    elif subprocess.run(
        ["git", "cat-file", "-e", f"{reviewed_commit}^{{commit}}"], cwd=root,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False,
    ).returncode != 0:
        gaps.append(f"reviewed_commit does not resolve locally: {reviewed_commit}")
    if metadata.get("result") != "PASS":
        gaps.append(f"evidence result must be PASS: {path}")
    if not isinstance(metadata.get("test_command"), str) or not metadata["test_command"].strip():
        gaps.append(f"evidence test_command is missing: {path}")
    reviewer = metadata.get("reviewer")
    if not isinstance(reviewer, str) or not reviewer.strip():
        gaps.append(f"evidence reviewer is missing: {path}")
    elif stage != "builder" and reviewer.strip().casefold() == owner.strip().casefold():
        gaps.append(f"independent evidence reviewer cannot be the story owner: {path}")
    return gaps


def validate(
    spec: object, root: Path, inventory_only: bool = False
) -> tuple[list[str], str, str, set[str], dict[str, set[str]]]:
    gaps: list[str] = []
    if not isinstance(spec, dict):
        return ["story spec must be a JSON object"], "UNKNOWN", "UNKNOWN", set(), {}
    story = spec.get("story_key")
    owner = spec.get("owner")
    if not isinstance(story, str) or not STORY_RE.fullmatch(story):
        gaps.append("story_key is missing or invalid")
        story = "UNKNOWN"
    if not isinstance(owner, str) or not owner.strip():
        gaps.append("owner is missing")
        owner = "UNKNOWN"
    requirements = spec.get("requirement_ids")
    if not isinstance(requirements, list) or not requirements or any(not isinstance(x, str) or not x for x in requirements):
        gaps.append("requirement_ids must be a non-empty string list")
        requirements = []
    if len(requirements) != len(set(requirements)):
        gaps.append("requirement_ids contains duplicates")
    criteria = spec.get("acceptance_criteria")
    if not isinstance(criteria, list) or not criteria:
        gaps.append("acceptance_criteria must be a non-empty list")
        criteria = []
    ac_ids: set[str] = set()
    covered_requirements: set[str] = set()
    for row in criteria:
        if not isinstance(row, dict) or not isinstance(row.get("id"), str):
            gaps.append("acceptance criterion is missing an id")
            continue
        ac_id = row["id"]
        if ac_id in ac_ids:
            gaps.append(f"duplicate acceptance criterion: {ac_id}")
        ac_ids.add(ac_id)
        if story != "UNKNOWN" and not ac_id.startswith(f"{story}-AC"):
            gaps.append(f"acceptance criterion does not belong to story: {ac_id}")
        links = row.get("requirement_ids")
        if not isinstance(links, list) or not links:
            gaps.append(f"{ac_id}: missing requirement_ids")
            continue
        unknown = set(links) - set(requirements)
        if unknown:
            gaps.append(f"{ac_id}: unknown requirement IDs: {sorted(unknown)}")
        covered_requirements.update(links)
    for requirement in sorted(set(requirements) - covered_requirements):
        gaps.append(f"requirement has no acceptance criterion: {requirement}")

    reviewed_commit = spec.get("reviewed_commit")
    if not inventory_only and (
        not isinstance(reviewed_commit, str)
        or not HASH_RE.fullmatch(reviewed_commit)
        or reviewed_commit == "0" * 40
    ):
        gaps.append("reviewed_commit must be 40 lowercase hex characters")
    coverage = {ac_id: {"test": False, "evidence": False} for ac_id in ac_ids}
    validated_stages: set[str] = set()
    evidence_paths: set[Path] = set()
    stage_reviewers: dict[str, set[str]] = {}
    for field, label in (("tests", "test"), ("evidence", "evidence")):
        declarations = spec.get(field)
        if not isinstance(declarations, list) or not declarations:
            gaps.append(f"{field} must be a non-empty declaration list")
            continue
        for declaration in declarations:
            if not isinstance(declaration, dict):
                gaps.append(f"{label} declaration must be an object")
                continue
            path = safe_file(root, declaration.get("path"), label, gaps)
            links = declaration.get("ac_ids")
            if not isinstance(links, list) or not links:
                gaps.append(f"{label} declaration has no AC IDs: {declaration.get('path')}")
                continue
            unknown = set(links) - ac_ids
            if unknown:
                gaps.append(f"{label} declares unknown AC IDs: {sorted(unknown)}")
            for ac_id in set(links) & ac_ids:
                coverage[ac_id][label] = path is not None
            if label == "evidence" and path is not None and not inventory_only:
                stage = declaration.get("stage")
                if path in evidence_paths:
                    gaps.append(f"evidence file cannot satisfy multiple declarations: {path}")
                evidence_paths.add(path)
                metadata_gaps = validate_evidence_metadata(root, path, story, reviewed_commit, owner, stage)
                gaps.extend(metadata_gaps)
                if not metadata_gaps and isinstance(stage, str):
                    validated_stages.add(stage)
                    metadata = json.loads(path.read_text(encoding="utf-8"))
                    stage_reviewers.setdefault(stage, set()).add(metadata["reviewer"].strip().casefold())
    for ac_id, kinds in sorted(coverage.items()):
        for label, present in kinds.items():
            if not present:
                gaps.append(f"{ac_id}: missing {label} coverage")
    return gaps, story, owner, validated_stages, stage_reviewers


def implemented_stories(root: Path) -> set[str]:
    return {path.name.removesuffix(".test.sh") for path in (root / "tests/foundation").glob("*.test.sh")}


def validate_inventory(root: Path) -> list[str]:
    gaps: list[str] = []
    implemented = implemented_stories(root)
    specs = {path.stem: path for path in (root / "governance/stories").glob("*.json")}
    for story in sorted(implemented - set(specs)):
        gaps.append(f"implemented story has no story spec: {story}")
    for story in sorted(set(specs) - implemented):
        gaps.append(f"story spec has no implemented foundation test: {story}")
    for story in sorted(implemented & set(specs)):
        try:
            spec = json.loads(specs[story].read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            gaps.append(f"invalid story spec {story}: {error}")
            continue
        story_gaps, declared_story, _, _, _ = validate(spec, root, inventory_only=True)
        if declared_story != story:
            story_gaps.append(f"filename/story_key mismatch: {story}")
        gaps.extend(f"{story}: {gap}" for gap in story_gaps)
    return gaps


def write_acceptance(path: Path, story: str, actor: str, spec_path: Path) -> None:
    if path.exists():
        raise RuntimeError(f"acceptance record already exists: {path}")
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "story_key": story,
        "decision": "PASS",
        "actor": actor,
        "recorded_at": dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z"),
        "story_spec": spec_path.as_posix(),
    }
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    descriptor = os.open(path, flags, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)
        handle.write("\n")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("story_spec", type=Path, nargs="?")
    parser.add_argument("--all", action="store_true", help="validate the complete implemented-story inventory")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--record-acceptance", type=Path)
    parser.add_argument("--actor")
    args = parser.parse_args()
    root = args.root.resolve()
    if args.all:
        if args.story_spec or args.record_acceptance or args.actor:
            parser.error("--all cannot be combined with a story spec or acceptance recording")
        gaps = validate_inventory(root)
        if gaps:
            print(f"TRACEABILITY INVENTORY: FAIL ({len(gaps)} blocking gap(s))")
            for gap in gaps:
                print(f"- {gap}")
            return 1
        print(f"TRACEABILITY INVENTORY: PASS ({len(implemented_stories(root))} implemented stories)")
        return 0
    if not args.story_spec:
        parser.error("story_spec is required unless --all is used")
    try:
        spec_path = args.story_spec if args.story_spec.is_absolute() else root / args.story_spec
        spec = json.loads(spec_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"TRACEABILITY: FAIL (invalid story spec: {error})")
        return 1
    gaps, story, owner, validated_stages, stage_reviewers = validate(spec, root)
    if args.record_acceptance:
        if not args.actor or not args.actor.strip():
            gaps.append("acceptance recording requires --actor")
        elif args.actor.strip().casefold() == owner.strip().casefold():
            gaps.append("story owner cannot record acceptance")
        required_stages = spec.get("required_acceptance_stages")
        if not isinstance(required_stages, list) or not required_stages:
            gaps.append("required_acceptance_stages must be a non-empty list")
            required_stages = []
        else:
            invalid_stages = set(required_stages) - EVIDENCE_STAGES
            if invalid_stages:
                gaps.append(f"unknown required acceptance stages: {sorted(invalid_stages)}")
        weakened = MANDATORY_ACCEPTANCE_STAGES - set(required_stages)
        if weakened:
            gaps.append(f"story spec cannot remove mandatory acceptance stages: {sorted(weakened)}")
        effective_stages = set(required_stages) | MANDATORY_ACCEPTANCE_STAGES
        for stage in sorted(effective_stages - validated_stages):
            gaps.append(f"missing valid required evidence stage: {stage}")
        overlap = stage_reviewers.get("builder", set()) & stage_reviewers.get("adversarial", set())
        if overlap:
            gaps.append("builder and adversarial evidence require distinct reviewers")
    if gaps:
        print(f"TRACEABILITY: FAIL ({len(gaps)} blocking gap(s))")
        for gap in gaps:
            print(f"- {gap}")
        print("ACCEPTANCE: BLOCKED")
        return 1
    if args.record_acceptance:
        record = args.record_acceptance if args.record_acceptance.is_absolute() else root / args.record_acceptance
        try:
            record.resolve().relative_to(root)
            write_acceptance(record, story, args.actor.strip(), args.story_spec)
        except (OSError, RuntimeError, ValueError) as error:
            print(f"TRACEABILITY: FAIL (acceptance record not written: {error})")
            print("ACCEPTANCE: BLOCKED")
            return 1
        print(f"ACCEPTANCE: RECORDED ({record.relative_to(root)})")
    print(f"TRACEABILITY: PASS ({story}; {len(spec['acceptance_criteria'])} ACs)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
