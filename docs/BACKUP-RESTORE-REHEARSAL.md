# BIOLOG local backup/restore and disaster-recovery rehearsal (BIO-OPS-009)

Operations needs proof - run locally, not merely designed on paper - that
the control-plane database, each tenant database, and object (photo)
evidence storage can be backed up and genuinely restored before any cloud
design is considered production-ready. This document is that rehearsal's
runbook; `scripts/disaster-recovery-rehearsal.sh` is the rehearsal itself.

All data used is synthetic. Nothing here is a production SLO or capacity
claim - every number below is what one local laptop run observed, the same
disclosure BIO-OPS-007 makes for its own local performance numbers.

## What gets rehearsed

| AC | How |
|---|---|
| **AC01** - control-plane and tenant backups are independently restorable | `scripts/backup-mysql.sh` dumps `mysql-control` and `mysql-tenant-asu` as two **separate** `mysqldump` files, one per service/database. Either can be restored alone, without touching the other. |
| **AC02** - restore to new local containers is documented | This document, plus `scripts/restore-mysql.sh` / `scripts/restore-azurite.sh`, both of which always create a **brand-new** named volume and a **brand-new** standalone container - never the original compose-managed container. |
| **AC03** - photo/object evidence restore is verified | `scripts/backup-azurite.sh` / `scripts/restore-azurite.sh` round-trip the Azurite (Blob Storage emulator) volume; `scripts/azurite-rest.py` verifies every restored blob's sha256 against the hash recorded at backup time. |
| **AC04** - recovery evidence records achieved RPO/RTO locally without claiming production SLOs | `scripts/disaster-recovery-rehearsal.sh` times its own run and writes a JSON result (see below) carrying an explicit non-SLO disclaimer. |

## Why synthetic seed data, not real application data

No BIOLOG feature yet writes real persisted data into `mysql-control`,
`mysql-tenant-asu` or Azurite: every feature's repository implementation
today is `InMemory*Repository` (confirmed by inspection; e.g.
`api/features/bio-pho-001/Domain/InMemoryPhotoMasterRepository.cs`), and
nothing in `api/` references `Azure.Storage.Blobs` or a MySQL client
library. BIO-FND-003 provisions these containers; no later story has wired
real persistence to them yet. A backup/restore rehearsal is still
meaningful today - it proves the *mechanism*, independently of which
feature eventually writes through it - so the rehearsal seeds its own
synthetic rows and blobs rather than waiting on that wiring. Every
row/blob the rehearsal creates is clearly synthetic
(`rehearsal_tenants`/`rehearsal_persons` tables, `evidence-N.bin` blobs
containing a literal "synthetic photo evidence fixture" string) and is
destroyed at the end of the run.

## Running it

```sh
scripts/disaster-recovery-rehearsal.sh                 # full rehearsal, cleans up its own scratch artifacts
scripts/disaster-recovery-rehearsal.sh --keep-artifacts # also keeps backups/ops009-rehearsal/<run>/ for inspection
```

This needs Docker; if invoked from an agent sandbox, run it with the
sandbox disabled (same class of restriction `docs/LOCAL-INFRASTRUCTURE.md`
and `docs/TOOLCHAIN-POLICY.md` already document for `dotnet`/Docker socket
access).

It is **fully self-contained and isolated**: it runs under its own Docker
Compose project name (`biolog-ops009-rehearsal`, via `docker compose -p`,
which overrides `infrastructure/docker-compose.local.yml`'s own pinned
`name: biolog`) and its own dedicated, uncommon ports, so it never touches,
recreates, or collides with a developer's already-running default stack
(the same technique the live smoke test in
`tests/foundation/BIO-FND-003.test.sh` uses). Every container, volume and
network it creates is removed in its final phase, pass or fail.

### What it actually does, phase by phase

1. **Bring up an isolated stack** - `mysql-control`, `mysql-tenant-asu`,
   `azurite`, via `docker compose -p biolog-ops009-rehearsal`, waits for
   all three to report `healthy`.
2. **Seed synthetic pre-backup data** - 2 rows into `mysql-control`, 3 rows
   into `mysql-tenant-asu`, 2 blobs into a `ops009-evidence` Azurite
   container.
3. **Back up each independently** (AC01) - `scripts/backup-mysql.sh` dumps
   `mysql-control` and `mysql-tenant-asu` to two separate `.sql.gz` files.
   `scripts/backup-azurite.sh` archives the Azurite volume. This moment is
   recorded as the RPO reference point.
4. **Simulate ongoing production writes AFTER the backup** - one more row
   into each MySQL database, one more blob into Azurite. This is the data
   the rehearsal expects to genuinely, boundedly lose - the honest RPO
   window, not a zero claimed by never writing anything after the backup.
5. **Declare a disaster** - `docker compose down -v`: every original
   container and volume is destroyed. A real simulated total loss, not a
   restart.
6. **Restore into brand-new containers/volumes** (AC02) -
   `scripts/restore-mysql.sh` (twice, once per database) and
   `scripts/restore-azurite.sh`, each against a container/volume name that
   never existed before this run.
7. **Verify** (AC01/AC03) - queries the restored MySQL rows and confirms
   they are *exactly* the pre-backup set (the post-backup row is correctly
   absent, not merely "some rows exist"); downloads both pre-backup blobs
   from the restored Azurite and confirms their sha256 matches exactly
   what was recorded at backup time; confirms the post-backup blob is
   correctly absent from the restore.
8. **Record RPO/RTO** (AC04) - writes a JSON result (next section).
9. **Tear down** every container/volume/network this run created.

### Result JSON

Written to `backups/ops009-rehearsal/<run-id>/result.json` (only kept with
`--keep-artifacts`; otherwise also copied to
`/tmp/biolog-ops009-last-result.json`). Example from a real local run:

```json
{
  "story": "BIO-OPS-009",
  "run_id": "20261008T060949Z",
  "disclaimer": "Observed on a single local laptop run. NOT a production SLO or capacity claim - see BIO-OPS-007 for the same disclosure pattern applied to its own local performance numbers.",
  "post_backup_write_at": "2026-10-08T06:09:54Z",
  "disaster_declared_at": "2026-10-08T06:09:55Z",
  "restore_verified_at": "2026-10-08T06:10:27Z",
  "observed_rto_seconds": 32,
  "observed_rpo": {
    "mysql_control": {"backup_completed_at": "2026-10-08T06:09:42Z", "rpo_seconds": 13, "lost_rows": 1, "lost_row_ids": [99]},
    "mysql_tenant_asu": {"backup_completed_at": "2026-10-08T06:09:42Z", "rpo_seconds": 13, "lost_rows": 1, "lost_row_ids": [99]},
    "azurite": {"backup_completed_at": "2026-10-08T06:09:54Z", "rpo_seconds": 1, "lost_blobs": 1, "lost_blob_names": ["evidence-3.bin"]}
  },
  "verification_failures": 0,
  "verification_result": "PASS"
}
```

**Observed RTO** (recovery time objective) is wall-clock seconds from the
moment the original stack's containers/volumes are destroyed
(`disaster_declared_at`) to the moment every restored row/blob is verified
correct (`restore_verified_at`) - three consecutive local runs measured
31s, 32s and 32s. **Observed RPO** (recovery point objective), per store, is
`rpo_seconds`: the wall-clock gap between *that store's own*
`backup_completed_at` (read from the manifest its own backup script writes -
not a single timestamp sampled loosely across all three stores) and the
disaster being declared. `lost_rows`/`lost_blob_names` are supporting impact
evidence, not the RPO measurement itself: the rehearsal deliberately writes
known data *after* the backup completes, then proves precisely that data
(and only that data) is absent after restore, attributable to an exact
id/name - but the RPO duration is derived from real timestamps, not from
counting how many rows this one rehearsal happened to write
(FEMI-OPS009-02, round 2).

Neither number is a production SLO. They describe one laptop, one small
synthetic dataset, and a cold/local Docker daemon - not production hardware,
network topology, or data volume.

## Round 2 review findings (Seyi + Femi) — fixed

Round 1 passed the happy path genuinely but had four real assurance gaps,
all confirmed and fixed:

- **SY-OPS009-01 (High)** — Phase 7 originally compared only `SELECT id`,
  so a restore that preserved primary keys but corrupted every other
  column would still report PASS. Fixed: Phase 2 now captures a full-row
  snapshot (`SELECT id, name` / `SELECT id, label`) right after seeding,
  and Phase 7 compares the restored rows against that exact snapshot
  string, not a hard-coded literal or an ids-only count.
- **SY-OPS009-02 (High)** — there was no `EXIT`/`INT`/`TERM` trap at all;
  `fail()` only tore down the compose stack, never the standalone restore
  containers/volumes from Phase 6, and both restore scripts let
  `docker volume create` silently reuse a pre-existing name. Fixed:
  `cleanup_rehearsal_stack()` now removes the standalone restore
  containers/volumes too and is bound via `trap cleanup_rehearsal_stack
  EXIT` (plus `trap 'exit N' INT/TERM`, so a signal actually terminates the
  script - a trap bound directly to INT/TERM does NOT stop the script on
  its own unless the handler itself calls `exit`); `restore-mysql.sh`/
  `restore-azurite.sh` now `docker volume inspect` first and refuse
  outright if the name already exists. Verified live: a deterministic
  failure injected right after Phase 6 (`BIOLOG_OPS009_INJECT_FAILURE_AFTER_PHASE6=1`,
  a test-only hook) leaves zero stray containers/volumes.
- **FEMI-OPS009-01 (High)** — `backup-azurite.sh` stopped the Azurite
  container, then ran the archive step under `set -e`; a failure there
  (disk full, runtime failure) exited before the restart ever ran, leaving
  Azurite stopped - a failed backup becoming an outage. Fixed: the
  archive's exit status is captured without letting `set -e` act on it,
  the restart is always attempted regardless, and a failed restart is now
  fatal rather than a warning. Verified live: forcing the archive to fail
  (an unwritable `--out` path) while the container is running, the
  container is confirmed restarted and running afterward.
- **FEMI-OPS009-02 (High)** — the result labeled a timestamp taken
  *before* the sequential backups as `rpo_reference_at`, then reported a
  hard-coded count of one lost row/blob per store - that measured the
  rehearsal's own fixture, not an actual RPO duration, and ignored that the
  three stores' backups complete at different times. Fixed: each store's
  own `backup_completed_at` is read from its manifest, and
  `observed_rpo.<store>.rpo_seconds` is the real elapsed time from that
  timestamp through `disaster_declared_at`; `lost_rows`/`lost_blob_names`
  remain as supporting impact evidence, not the RPO measurement itself.

See `tests/foundation/BIO-OPS-009.test.sh` for the regression tests added
for each of these (structural checks plus two live, Docker-gated tests
that actually exercise the failure-injection and restart-after-failure
scenarios above).

## Round 3 review finding (Femi) — fixed

Seyi PASSed round 2 outright (SY-OPS009-01/02 fully closed). Femi's round-2
re-review found FEMI-OPS009-01 only partially closed:

- **FEMI-OPS009-01 residual (High)** — round 2's fix correctly covered the
  archive command returning a nonzero exit code, but `backup-azurite.sh`
  still installed no `EXIT`/`INT`/`TERM` trap. If the shell process itself
  received SIGINT/SIGTERM (or was otherwise killed) between `docker stop`
  and the restart later in the script, it exited immediately with Azurite
  left stopped and no restart ever attempted - the same outage class,
  triggered a different way, that the round-2 live test (which only forced
  a nonzero `mkdir`/archive exit, never an actual signal) didn't catch.
  Fixed: installed `trap restart_on_exit_if_not_already_confirmed EXIT`
  (plus `trap 'exit N' INT/TERM`, same reasoning as SY-OPS009-02) **before**
  the container is ever stopped, so a signal arriving at any point from
  then on - mid-stop, mid-archive, or between the stop and the normal
  restart call - is covered. A `container_restart_confirmed` flag disarms
  the handler once the script's own normal restart path has confirmed
  readiness, so the happy path never does redundant work. Verified live: a
  real `SIGTERM` sent to the backup process immediately after its
  `docker stop` log line, well before the script's own restart code could
  possibly run - the trap caught it, restarted Azurite, and confirmed it
  healthy before the process exited.

## Three independent backup mechanisms, one principle

- **`scripts/backup-mysql.sh`** - `mysqldump --single-transaction` per
  service/database, independently.
- **`scripts/backup-azurite.sh`** - a volume-level tar of Azurite's data
  directory. **Must stop the Azurite container first** if it is running:
  confirmed directly against a live instance that Azurite's on-disk
  `__azurite_db_blob__.json`/`__azurite_db_queue__.json` metadata files only
  reflect newly created containers/blobs after a *graceful* stop (its
  LokiJS persistence layer flushes to disk on clean shutdown, not on every
  write) - a tar taken while it is still running can silently capture a
  stale or outright empty metadata snapshot even though the blob bytes
  themselves are already on disk, because the index pointing to them was
  never flushed. `--container NAME` makes `backup-azurite.sh` do this
  stop/snapshot/restart automatically; passing no container name is only
  safe when the volume's owning container is already stopped. The restart
  is guaranteed even if the archive step fails or the backup process itself
  is killed between the stop and the restart (an `EXIT`/`INT`/`TERM` trap
  covers the latter - see "Round 3 review finding" below).
- **`scripts/restore-mysql.sh` / `scripts/restore-azurite.sh`** - both
  create a brand-new named volume and a brand-new, uniquely-named
  container; neither ever touches an existing volume or container name.
  `restore-azurite.sh` must pass `--location /data` explicitly when
  starting the fresh Azurite container: Azurite's own default location is
  its process's current working directory, not the mounted volume path, so
  omitting it silently opens a brand-new empty metadata database instead of
  the one just restored (confirmed via `azurite-blob --help` and by
  reproducing the resulting `ContainerNotFound` directly).

## Azurite REST authentication (`scripts/azurite-rest.py`)

No `azure-storage-blob` SDK or `azcopy` is pinned in
`docs/TOOLCHAIN-POLICY.md`, and adding either just to seed/verify a handful
of rehearsal blobs would work against BIO-FND-003's own "zero required
configuration" policy. `scripts/azurite-rest.py` instead implements the
small handful of Azure Blob Storage REST operations the rehearsal needs
(`put-container`, `put-blob`, `get-blob`, `list-blobs`) directly, using only
the Python standard library and Azurite's publicly documented, fixed
emulator account name/key (`devstoreaccount1` - not a secret; see the
module's own docstring and `.gitleaks.toml`'s allowlist entry for why).

Two non-obvious things had to be confirmed empirically against a real
running Azurite instance (via its own `--debug` log, which prints the exact
`[STRING TO SIGN]` it computed) before Shared Key signing worked at all:

1. Azurite's path-style addressing means the account name must appear
   **twice** in `CanonicalizedResource`
   (`/devstoreaccount1/devstoreaccount1/<container>`), not once - this
   differs from production Azure's virtual-hosted-style URLs, where the
   account name never appears in the URI path at all.
2. `urllib.request` silently adds `Content-Type:
   application/x-www-form-urlencoded` to any request carrying a body
   unless one is set explicitly - that header must be signed with whatever
   value is *actually sent on the wire*, so it is now set explicitly
   (`application/octet-stream`) rather than left out of the signature.

## Repeatable commands

```sh
scripts/disaster-recovery-rehearsal.sh --keep-artifacts
bash tests/foundation/BIO-OPS-009.test.sh
```

See `evidence/BIO-OPS-009/builder/` for real output from three consecutive
local runs.
