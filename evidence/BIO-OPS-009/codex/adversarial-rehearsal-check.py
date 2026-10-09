#!/usr/bin/env python3
"""Independent recovery-integrity and cleanup checks for BIO-OPS-009."""

from pathlib import Path

root = Path(__file__).resolve().parents[3]
script = (root / "scripts/disaster-recovery-rehearsal.sh").read_text()
restore_mysql = (root / "scripts/restore-mysql.sh").read_text()
restore_azurite = (root / "scripts/restore-azurite.sh").read_text()

checks = [
    (
        "control-plane restore verifies complete row contents",
        "SELECT id, name FROM biolog_control.rehearsal_tenants" in script,
        "verification selects only id; corrupted/restored name values would still PASS",
    ),
    (
        "tenant restore verifies complete row contents",
        "SELECT id, label FROM biolog_asu.rehearsal_persons" in script,
        "verification selects only id; corrupted/restored label values would still PASS",
    ),
    (
        "failure and signal exits always clean standalone restored resources",
        "trap " in script
        and "ops009-rehearsal-control-restored" in script.split("cleanup_rehearsal_stack()")[1].split("}", 1)[0],
        "no EXIT/INT/TERM trap; fail() cleans only the compose stack, not standalone restored containers/volumes",
    ),
    (
        "MySQL restore refuses a pre-existing volume instead of reusing it",
        "docker volume inspect" in restore_mysql and "already exists" in restore_mysql,
        "docker volume create succeeds for an existing name, so the script does not guarantee a fresh volume",
    ),
    (
        "Azurite restore refuses a pre-existing volume instead of reusing it",
        "docker volume inspect" in restore_azurite and "already exists" in restore_azurite,
        "docker volume create succeeds for an existing name, so the script does not guarantee a fresh volume",
    ),
]

failed = 0
for label, ok, detail in checks:
    print(("PASS" if ok else "FAIL"), label)
    if not ok:
        failed += 1
        print("     ", detail)

print(f"{len(checks) - failed} passed, {failed} failed")
raise SystemExit(1 if failed else 0)
