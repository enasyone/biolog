# BIO-OPS-009 — Builder evidence

**Story:** Run local backup/restore and disaster-recovery rehearsal
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-OPS-009`, branch `feature/BIO-OPS-009` off `develop`
**Dependency:** BIO-OPS-007 (done, merged to develop)
**Round 1 commit:** `2ad57cd7db55b0502f556823b5966e2e7ba05068` (+ evidence `5b1e3b0`)
**Round 2 commit:** `2e3497570ec9fd0a50a2c2a543b8cfad7eee769c` — fixes all four High findings from Seyi/Femi round 1
**Round 3 commit:** `a91b0c429b5b32352aabc95b37dd543f0c80f583` — fixes Femi's residual FEMI-OPS009-01 finding (round 2 re-review); Seyi PASSed round 2 outright

## Summary

- **AC01** — `scripts/backup-mysql.sh` dumps `mysql-control` and `mysql-tenant-asu`
  as two separate `mysqldump --single-transaction` files, one per service/database,
  never a combined dump. The rehearsal proves each is independently restorable:
  `mysql-control`'s restore never touches `mysql-tenant-asu`'s dump or container,
  and vice versa.
- **AC02** — `docs/BACKUP-RESTORE-REHEARSAL.md` documents the restore procedure.
  `scripts/restore-mysql.sh` / `scripts/restore-azurite.sh` always create a
  brand-new named volume and a brand-new standalone container — never the
  original compose-managed one — proven by three consecutive real local runs
  (see below), not merely asserted.
- **AC03** — `scripts/backup-azurite.sh` / `scripts/restore-azurite.sh` round-trip
  the Azurite (Blob Storage emulator) volume holding synthetic "photo/object
  evidence" blobs. `scripts/azurite-rest.py` downloads every restored blob and
  confirms its sha256 matches exactly what was recorded at backup time, and that
  a blob written *after* the backup is correctly absent from the restore.
- **AC04** — `scripts/disaster-recovery-rehearsal.sh` times its own run and writes
  a JSON result recording observed RPO (exactly which rows/blobs were lost, by
  id/name, because they were deliberately written after the backup) and observed
  RTO (wall-clock seconds from declaring the disaster to verified restore), with
  an explicit "NOT a production SLO" disclaimer — the same disclosure pattern
  BIO-OPS-007 uses for its own local performance numbers.

## Why synthetic seed data

No BIOLOG feature yet persists real data to `mysql-control`, `mysql-tenant-asu`
or Azurite — every repository implementation inspected is `InMemory*Repository`,
and nothing in `api/` references a MySQL client library or `Azure.Storage.Blobs`.
The rehearsal seeds its own clearly-synthetic rows (`rehearsal_tenants`,
`rehearsal_persons`) and blobs (`evidence-N.bin`, containing a literal "synthetic
photo evidence fixture" string), proving the backup/restore mechanism itself
independently of that wiring. Disclosed in full in
`docs/BACKUP-RESTORE-REHEARSAL.md`, not silently assumed.

## Two real bugs found and fixed while building this, both confirmed empirically
against a real running Azurite instance before being declared fixed:

1. **Azurite's LokiJS metadata store only flushes to disk on a graceful stop**,
   not on every write — a volume-level tar taken while Azurite is still running
   can silently back up a stale/empty metadata snapshot even though the blob
   bytes are already on disk. `backup-azurite.sh --container NAME` now stops the
   container, snapshots, and restarts it automatically.
2. **Azurite's `--location` flag is not optional on restore** — omitted, Azurite
   defaults to its process's own current working directory rather than the
   mounted volume, so a freshly started container opens a brand-new empty
   database and every blob lookup fails with `ContainerNotFound` even though the
   restored files are sitting right there on disk.

Also had to reverse-engineer Shared Key REST authentication for Azurite directly
(no `azure-storage-blob` SDK or `azcopy` is pinned in `docs/TOOLCHAIN-POLICY.md`,
and adding either just for this would cut against BIO-FND-003's "zero required
configuration" policy) — `scripts/azurite-rest.py`'s signing was verified against
Azurite's own `--debug` log (`[STRING TO SIGN]` / `Signature 1 matched`) until it
worked, not assumed correct from memory of the spec.

## Round 1 verification

- `tests/foundation/BIO-OPS-009.test.sh`: **11/11 PASS** (10 structural/regression
  checks + 1 live end-to-end rehearsal run, gated on `docker info` being
  reachable). See `developer-test-output.txt`.
- `scripts/disaster-recovery-rehearsal.sh --keep-artifacts`: run **three
  consecutive times**, all PASS, observed RTO 31s/32s/32s. See
  `rehearsal-run1-output.txt` / `rehearsal-run2-output.txt` /
  `rehearsal-run3-output.txt` for full real output including each run's result
  JSON.
- `scripts/check-module-project-references.sh`: PASS (no `api/` changes). See
  `module-reference-output.txt`.
- `scripts/secret-scan.sh --staged`: clean (gitleaks, 0 leaks). See
  `secret-scan-output.txt`.
- `python3 scripts/check-traceability.py --all`: PASS. See
  `traceability-output.txt`.

## Round 2 (Seyi + Femi round 1 — 4 High findings, all fixed)

Both required reviewers FAILed round 1 with four real, concrete assurance
gaps — see `codex/REVIEW-Seyi-round1.md`, `codex/REVIEW-Femi-round1.md`, and
Seyi's own reproduction script `codex/adversarial-rehearsal-check.py`. Full
fix writeup is in `docs/BACKUP-RESTORE-REHEARSAL.md`'s "Round 2 review
findings" section; summary:

- **SY-OPS009-01** (restore verification checked only primary keys) — fixed
  by capturing a full-row pre-backup snapshot and comparing restored rows
  against it exactly, not an ids-only count.
- **SY-OPS009-02** (no failure/interrupt cleanup; volumes could be silently
  reused) — fixed with an `EXIT`/`INT`/`TERM` trap covering the standalone
  restore resources, plus an explicit pre-existing-volume refusal in both
  restore scripts. Verified live via a deterministic injected-failure test
  hook: zero stray resources afterward.
- **FEMI-OPS009-01** (a failed Azurite archive could leave it stopped) —
  fixed by always attempting the restart regardless of archive outcome, and
  treating a failed restart as fatal. Verified live by forcing an archive
  failure.
- **FEMI-OPS009-02** (RPO was a hard-coded fixture count, not a measured
  duration) — fixed by computing RPO per store from that store's own
  backup-completion manifest timestamp through the disaster.

Verified again after fixing:

- `evidence/BIO-OPS-009/codex/adversarial-rehearsal-check.py` (Seyi's own
  reproduction): **5/5 PASS**. See `round2-seyi-reproduction-output.txt`.
- `tests/foundation/BIO-OPS-009.test.sh`: **19/19 PASS** (8 new checks: 6
  structural + 2 live Docker-gated tests that actually exercise the
  failure-injection/restart-on-failure scenarios). See
  `round2-developer-test-output.txt`.
- `scripts/disaster-recovery-rehearsal.sh --keep-artifacts`: run three more
  consecutive times, all PASS, observed RTO 31s/32s/32s, RPO now genuinely
  computed per store (11-13s for MySQL, 1s for Azurite in these runs). See
  `round2-rehearsal-run1-output.txt` / `round2-rehearsal-run2-output.txt` /
  `round2-rehearsal-run3-output.txt`.
- `scripts/check-module-project-references.sh`: PASS. See
  `round2-module-reference-output.txt`.
- `scripts/secret-scan.sh --staged`: clean. See
  `round2-secret-scan-output.txt`.
- `python3 scripts/check-traceability.py --all`: PASS. See
  `round2-traceability-output.txt`.

## Round 3 (Femi round 2 re-review — residual High finding, fixed; Seyi PASSed round 2 outright)

Seyi's round-2 re-review PASSed outright: SY-OPS009-01 and SY-OPS009-02 both
fully closed, confirmed independently. Femi's round-2 re-review found
FEMI-OPS009-01 only partially closed — see `codex/REVIEW-Femi-round2.md`.
The round-2 fix correctly covered the archive command returning a nonzero
exit code, but `backup-azurite.sh` installed no `EXIT`/`INT`/`TERM` trap:
a SIGINT/SIGTERM delivered between `docker stop` and the restart left
Azurite stopped with no restart ever attempted — the round-2 live test only
forced a nonzero archive exit, never an actual signal, so it didn't catch
this.

Fix: `trap restart_on_exit_if_not_already_confirmed EXIT` (+
`trap 'exit N' INT/TERM`) installed before the container is ever stopped,
disarmed only once the normal restart path has confirmed readiness. Full
writeup in `docs/BACKUP-RESTORE-REHEARSAL.md`'s "Round 3 review finding"
section.

Verified again after fixing:

- Live signal-injection test: sent a real `SIGTERM` to the backup process
  immediately after its `docker stop` log line (well before the script's
  own restart code could run) — confirmed the trap caught it, restarted
  Azurite, and confirmed it healthy before the process exited.
- `tests/foundation/BIO-OPS-009.test.sh`: **21/21 PASS** (2 new checks: 1
  structural + 1 live Docker-gated signal-injection test). See
  `round3-developer-test-output.txt`.
- `scripts/disaster-recovery-rehearsal.sh --keep-artifacts`: run three more
  consecutive times, all PASS. See `round3-rehearsal-run1-output.txt` /
  `round3-rehearsal-run2-output.txt` / `round3-rehearsal-run3-output.txt`.
- `evidence/BIO-OPS-009/codex/adversarial-rehearsal-check.py` (Seyi's
  reproduction, untouched by this file): still 5/5 PASS. See
  `round3-seyi-reproduction-output.txt`.
- `scripts/check-module-project-references.sh`: PASS. See
  `round3-module-reference-output.txt`.
- `scripts/secret-scan.sh --staged`: clean. See
  `round3-secret-scan-output.txt`.
- `python3 scripts/check-traceability.py --all`: PASS. See
  `round3-traceability-output.txt`.

No application code, shared contract, migration or UI route is touched. No
GitHub remote added, no push performed.
