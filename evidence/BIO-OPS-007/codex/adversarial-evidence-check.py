#!/usr/bin/env python3
"""Independent semantic checks for BIO-OPS-007 evidence."""

import json
from pathlib import Path

root = Path(__file__).resolve().parents[3]
baseline = json.loads((root / "evidence/BIO-OPS-007/builder/latency-baseline.json").read_text())
gate = (root / "tests/features/bio-ops-007/BIO-OPS-007.acceptance.sh").read_text()

checks = []

api = baseline["api"]
checks.append((
    "AC03 API value is request latency",
    "runner overhead" not in api["limitations"].lower()
    and "testserver integration cases" not in api["metric"].lower(),
    f"metric={api['metric']!r}; limitations={api['limitations']!r}",
))

ui = baseline["ui"]
checks.append((
    "AC03 UI value is interaction/request latency",
    "build/render" not in ui["limitations"].lower()
    and "statically rendered" not in ui["metric"].lower(),
    f"metric={ui['metric']!r}; limitations={ui['limitations']!r}",
))

checks.append((
    "AC03 gate validates latency semantics, not only positive sample counts",
    'd["api"]["samples"] > 0 and d["ui"]["samples"] > 0' not in gate,
    "gate accepts any two positive sample counts without checking operation, units, or latency source",
))

checks.append((
    "AC04 gate executes database failure injection",
    "dotnet test" in gate and "database" in gate.lower(),
    "gate only rg-checks an audit-failure test name; it executes no failure-injection test",
))

checks.append((
    "AC04 gate executes queue failure injection",
    "flutter test" in gate and "queue" in gate.lower(),
    "gate only rg-checks recordUploadFailure; it executes no queue interruption",
))

failed = 0
for label, ok, detail in checks:
    print(("PASS" if ok else "FAIL"), label)
    if not ok:
        failed += 1
        print("     ", detail)

print(f"{len(checks) - failed} passed, {failed} failed")
raise SystemExit(1 if failed else 0)
