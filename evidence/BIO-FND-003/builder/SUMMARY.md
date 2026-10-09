# BIO-FND-003 — Builder evidence

**Story:** Create local Docker development infrastructure
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-FND-003`, branch `feature/BIO-FND-003` off `develop`
**Dependency:** BIO-FND-002 (DONE)

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — Compose starts control-plane MySQL, tenant MySQL, Redis, RabbitMQ, Azurite, Mailpit | `infrastructure/docker-compose.local.yml` defines all six as default-profile services. | `validate-infra-compose-output.txt`, live: `wait-for-infra: all services ready` in `developer-test-output.txt` |
| AC02 — standard profile starts a second tenant DB for isolation testing | `mysql-tenant-grace` is gated behind Compose's `multi-tenant` profile (`docker compose --profile multi-tenant up`); NOT started by default. Live-confirmed cross-tenant isolation: a row written into `mysql-tenant-asu`'s `biolog_asu` DB is invisible from `mysql-tenant-grace` (separate MySQL instances, separate volumes). | `validate-infra-compose-output.txt` (profile checks), session transcript (isolation test) |
| AC03 — health checks defined, bootstrap waits for readiness | Every service declares a Docker healthcheck; `scripts/wait-for-infra.sh` polls `docker inspect`'s health status until all resolved services are healthy or times out. Live-verified: all 7 services (default + multi-tenant) reached `healthy`. | `developer-test-output.txt` (live smoke test) |
| AC04 — volumes named, reset commands documented | Every service has exactly one named top-level volume (`biolog_mysql_control`, `biolog_redis_data`, etc. - `name: biolog` pins the project prefix). `scripts/infra-reset.sh` (no args = full wipe, or named services = targeted wipe) documented in `docs/LOCAL-INFRASTRUCTURE.md`. Live-verified: single-service and full reset both removed exactly the targeted volume(s), leaving the rest running. | `validate-infra-compose-output.txt`, session transcript (reset tests) |
| AC05 — no production credentials required | Every credential (`MYSQL_ROOT_PASSWORD`, `RABBITMQ_USER`/`PASSWORD`) has a synthetic local-only default (`localroot` / `biolog` / `localdev`); `docker compose config` resolves successfully with zero environment variables set. | `validate-infra-compose-output.txt` |

## Developer tests (9/9 passing)

`tests/foundation/BIO-FND-003.test.sh` — see `developer-test-output.txt`. Two portability
regressions plus one per AC:
- Static: `scripts/infra-*.sh` parse under `/bin/bash` (macOS's stock bash 3.2) and contain no
  bash 4+ associative arrays (see "Real defects found live" below - this class of bug is why these
  two checks exist).
- AC01: removing a required service from a scratch copy fails `validate-infra-compose.sh`.
- AC02: un-gating the second tenant (removing its `profiles:` key) fails the check.
- AC03: stripping a service's healthcheck fails the check.
- AC04: stripping a service's volume mount fails the check.
- AC05: removing a credential's synthetic default fails the check.
- AC03 (live): `scripts/infra-up.sh` → `wait-for-infra.sh` → `infra-down.sh` against real
  containers, using dedicated ports so the test doesn't depend on (or collide with) whatever else
  is already bound on the host.

## Real defects found and fixed by live testing

Docker Desktop's daemon was not running at the start of this story but was started during
development, enabling genuine end-to-end verification rather than structural validation alone.
Two real bugs were only caught this way:

1. **Azurite healthcheck crashed on every attempt.** The Node one-liner assumed the `'connect'`
   event listener receives the socket as an argument (`c => c.end()`); Node does not pass it, so
   every invocation threw `TypeError: Cannot read properties of undefined (reading 'end')` and the
   container stayed `unhealthy` indefinitely. Fixed by capturing the socket in a variable before
   the event fires.
2. **`infra-reset.sh` silently removed nothing.** Without a pinned Compose `name:`, the project
   name defaulted to the containing directory (`infrastructure`), so Compose created volumes named
   `infrastructure_biolog_redis` while the reset script looked for `biolog_redis` - the `-f` flag on
   `docker volume rm` swallowed the "not found" error, so the script printed "done" having removed
   nothing. Fixed by pinning `name: biolog` and renaming the volume keys to match what the scripts
   already expected.

A third, more severe class of bug was also found and fixed: **macOS's default `/bin/bash` is
version 3.2** (no associative arrays; an empty array expanded under `set -u` is an "unbound
variable" error, not empty). This broke `infra-reset.sh` (via `declare -A`) and the no-`--multi-tenant`
path of `infra-up.sh`/`wait-for-infra.sh` (via `"${profile_args[@]}"` on an empty array) even though
both parsed and ran fine under a newer bash. Since `#!/usr/bin/env bash` resolves to whichever
`bash` is first in `PATH`, and this machine's `PATH` has no newer bash ahead of `/bin/bash`, this
is exactly the environment most BIOLOG contributors on stock macOS will actually run these scripts
under. Fixed by replacing the associative array with a portable `case` statement and the
possibly-empty arrays with plain string flags; added a regression test asserting every infra script
still parses under `/bin/bash` specifically and contains no `declare -A`.

## Known limitation

This machine already had unrelated Docker containers from other projects bound to `localhost:6379`
during manual verification, demonstrating the Redis port collision `infrastructure/.env`'s
`REDIS_PORT` override exists to solve - but meaning the *default* port set was not proven
collision-free on a clean machine with nothing else running. This is inherent to any fixed-port
local stack, not specific to this implementation.

## CHANGES_REQUIRED round 1 — BIO-FND-003-QA-01 and QA-02 (fixed)

Seyi's independent review (`evidence/BIO-FND-003/codex/REVIEW.md`, `independent-review.sh`,
reviewed commit `935524a`) ran in a sandbox where the Docker socket was also inaccessible, but
still found two real defects via a fake-`docker`-on-`PATH` shim, both fixed in commit `fba5227`:

- **QA-01 (High):** `wait-for-infra.sh` explicitly accepted `no-healthcheck` as a ready state
  (`[ "$status" != "healthy" ] && [ "$status" != "no-healthcheck" ]`). Every service in this
  compose file always declares a healthcheck by design, so that status at runtime means the health
  contract is missing or broken - a readiness gate that shrugs at that isn't gating on health at
  all. **Fix:** only `healthy` counts as ready; anything else (`no-healthcheck`, `starting`,
  `unhealthy`, `unknown`) counts as pending and eventually times out with a clear
  `service(no-healthcheck)`-style message.
- **QA-02 (High):** `infra-reset.sh` swallowed every Docker failure with `|| true` on both the
  targeted-service and full-reset paths, so a Docker shim that fails every command still printed
  "done" having cleaned up nothing. **Fix:** `docker compose stop`/`rm` and `docker volume rm`
  failures now propagate with a clear message. To avoid a legitimate case - resetting a service
  that was never started, so its volume was never created - becoming a false failure, volume
  removal checks existence via `docker volume inspect` first and treats "does not exist" as a
  no-op; any other failure (including `inspect` itself failing for a different reason) is fatal.

**Regression coverage added:** `tests/foundation/BIO-FND-003.test.sh` grew from 9 to 11 checks -
one proving a no-healthcheck container is reported not-ready, one proving a full-failure Docker
shim makes `infra-reset.sh` exit non-zero without printing "done", and a boundary case proving
resetting a never-started service's volume still succeeds cleanly. All three use a fake `docker` on
`PATH`, matching the environment constraint (Docker socket blocked in this sandbox) rather than
depending on a live daemon. Re-ran Seyi's own `independent-review.sh` unmodified against the fix:
all 7 of its probes now `PASS`, exit `0` (see `independent-review-rerun-output.txt`; previously 2
`FAIL`).

Per the changes-required instruction, only `scripts/wait-for-infra.sh`,
`scripts/infra-reset.sh`, and `tests/foundation/BIO-FND-003.test.sh` were touched.
`evidence/BIO-FND-003/codex/` was left exactly as Seyi supplied it (uncommitted), consistent with
BIO-FND-001/002.

## CHANGES_REQUIRED round 2 — BIO-FND-003-QA-03 (fixed)

Seyi's round-2 re-review (`evidence/BIO-FND-003/codex/REREVIEW-ROUND-2.md`, reviewed fix commit
`fba5227`) confirmed QA-01 and QA-02 closed, then found one new defect in the QA-02 fix itself,
fixed in commit `f6ae3fa`:

- **QA-03 (High):** `remove_volume_if_present`'s QA-02 fix checked `docker volume inspect`'s exit
  code alone and treated ANY non-zero result as "volume absent, nothing to clean up" - including a
  permission error or an unreachable daemon, not just Docker's genuine "no such volume" case. A
  Docker shim that made `volume inspect` fail with `permission denied` still made
  `infra-reset.sh redis` exit `0` and print "done" without having confirmed or achieved anything -
  exactly the fail-closed contract QA-02 was supposed to establish, undermined by an incomplete
  absence check. **Fix:** capture `docker volume inspect`'s stderr and only treat Docker's own "no
  such volume" diagnostic as confirmed absence; every other inspect failure now fails closed with
  `infra-reset: could not confirm whether volume <name> exists: <diagnostic>` and a non-zero exit.

**Regression coverage added:** a new test simulates `volume inspect` failing with `permission
denied` (no "no such volume" text) and asserts `infra-reset.sh` exits non-zero without printing
"done". The existing "never-started service" boundary test's fake `docker` was updated to emit
Docker's actual `Error: No such volume: <name>` diagnostic, since that exact signal is now what the
fix keys off of - it was previously just a bare non-zero exit, which no longer discriminates
correctly given the more precise check. 12/12 developer tests pass. Re-ran Seyi's own
`independent-review.sh` unmodified against the fix: all 8 of its probes now `PASS`, exit `0` (see
`independent-review-rerun-output.txt`; previously 1 `FAIL`).

Only `scripts/infra-reset.sh` and `tests/foundation/BIO-FND-003.test.sh` were touched for this
round; `evidence/BIO-FND-003/codex/` again left exactly as supplied.

## Live runtime evidence for the final commit (evidence-only, no code changes)

Chioma's acceptance attempt on commit `f6ae3fa`/`b15f6b2` correctly blocked because no stored,
reproducible live-runtime transcript existed for AC01-AC03 at that specific commit (the earlier
live testing during initial development was against an older, pre-QA-fix commit and was never
re-captured to a file). With Docker socket access confirmed working again, this was recaptured
end-to-end and stored at `builder/live-verification-2026-09-20.txt`:

- `scripts/infra-up.sh --multi-tenant --wait` brought up all 7 services (default profile + the
  `multi-tenant` profile's second tenant); `docker compose ps` shows every one `healthy` (AC01,
  AC03).
- Cross-tenant isolation (AC02) reproduced directly: `SHOW DATABASES` on each tenant instance shows
  only its own database; a table written into `mysql-tenant-asu`'s `biolog_asu` is confirmed absent
  from `mysql-tenant-grace`'s `biolog_grace` (`SHOW TABLES` returns empty there).
- `scripts/infra-reset.sh redis` (targeted) removed only `biolog_redis_data`, leaving the other 6
  services running; `scripts/infra-reset.sh` (full) then stopped everything and removed every
  remaining `biolog_*` volume (AC04), confirmed by `docker volume ls` before and after each.
- `infrastructure/.env` overrode `REDIS_PORT` to `63790` for this run only (not committed) - this
  machine still has the unrelated `clipforge_redis` container from another project bound to the
  default `6379`, the same known limitation already documented in
  `docs/LOCAL-INFRASTRUCTURE.md`. All Docker state created for this verification (containers,
  volumes, the temporary `.env`) was removed afterward; confirmed clean via `docker ps -a` /
  `docker volume ls`.
