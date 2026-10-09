# BIO-FND-002 — Builder evidence

**Story:** Pin local toolchain and dependency policy
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-FND-002`, branch `feature/BIO-FND-002` off `develop`
**Dependency:** BIO-FND-001 (DONE, commit `2224d4d`)

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — policy covers .NET LTS, Node LTS, package manager, Flutter, Docker, database client | `toolchain/versions.json` declares all six categories with a pinned version, match rule (`exact` or `min_major_minor`), and native pin file reference. `docs/TOOLCHAIN-POLICY.md` documents the table and the rationale for the two match rules. | `check-toolchain-output.txt`, `developer-test-output.txt` (AC01 checks) |
| AC02 — exact versions recorded in repository-managed version files during first bootstrap | `toolchain/versions.json` (single source of truth) plus native pins kept in sync: `global.json` (.NET SDK, `rollForward: disable`), `.nvmrc` (Node), `package.json` `packageManager` field (pnpm, Corepack-managed). Versions were read directly off this bootstrap machine (`dotnet --version`, `node --version`, `docker --version`, `docker compose version --short`, `flutter --version --machine`) except pnpm and the MySQL client, which are documented as policy targets since neither is installed here. | `toolchain/versions.json`, `global.json`, `.nvmrc`, `package.json`, `developer-test-output.txt` (AC02 checks) |
| AC03 — same lock files, no upgrading inside feature stories | `scripts/check-toolchain.sh` fails any manifest (`web/package.json`, `mobile/pubspec.yaml`, a `*.csproj`) that lacks its matching committed lock file, and fails any installed tool whose version doesn't satisfy its pinned match rule — including a version that was bumped without going through a dedicated toolchain story. `docs/TOOLCHAIN-POLICY.md` states the governance rule in words; the script enforces it mechanically. | `developer-test-output.txt` (lock-file and drift regression tests) |
| AC04 — version check command reports drift and fails on unsupported versions | `scripts/check-toolchain.sh`. Real run against this machine: `.NET SDK`, `Node.js`, `Docker Engine`, `Docker Compose` PASS; `pnpm` and `MySQL client` correctly reported `NOT INSTALLED` (see Known gaps below); overall exit `1` because of those two. | `check-toolchain-output.txt` |

## Developer tests (11/11 passing)

`tests/foundation/BIO-FND-002.test.sh` — see `developer-test-output.txt`. Covers, per AC:
- AC01: manifest has all six categories, each with a non-empty pinned version.
- AC02: `global.json`, `.nvmrc`, and `package.json` are each consistent with the manifest.
- AC03: a synthetic `web/package.json` without `pnpm-lock.yaml` fails the check (regression); the same fixture with the lock file added passes (boundary); a manifest copy edited to an uninstalled `.NET` version is reported as drift.
- AC04: the real run reports the two documented missing tools explicitly (not silently skipped), reports the matching installed tools as PASS, and exits non-zero while any tool is missing/drifted.

## Known gaps at this bootstrap (documented per the dispatch note, not blocking)

- **pnpm** is not installed on this laptop. Corepack (bundled with Node 20.20.2, confirmed present via `corepack --version` → `0.34.6`) will fetch the pinned `9.12.0` on first use once a real Node workspace exists. The exact patch was chosen without live npm-registry access and is flagged in the policy doc as unverified pending confirmation at first install.
- **MySQL client** is not installed on this laptop. Pinned minimum family `8.4.0` tracks the `mysql:8.4` container image already referenced by the control pack's local dev compose file.
- **gitleaks** remains not installed (carried over from BIO-FND-001); out of scope for this story since it isn't one of the six AC01 categories.
- Flutter's version check depends on running the real `flutter` binary, which on this specific sandboxed dev session needed elevated (non-default) execution permission to write its own cache/update-check stamp file — a local tooling-sandbox artifact of this session, not a defect in `check-toolchain.sh`. Re-run with normal permissions in a non-restricted shell and it reports the same PASS.

## Note for reviewers (Seyi)

Running `tests/foundation/BIO-FND-001.test.sh` from *this* worktree (checked out on `feature/BIO-FND-002`, branched off `develop`) shows 10/11 — the `BIO-FND-001-QA-01` regression test fails here. This is **not** a regression introduced by this story: neither `scripts/secret-scan.sh` nor `scripts/validate-bootstrap.sh` nor that test file were touched (see `git log` — this commit only adds new BIO-FND-002 files). The cause is that BIO-FND-001's regression test does `git clone --no-local $(git rev-parse --show-toplevel)` and then `git branch -f develop main`; when the clone source is checked out on `main` (the ordinary `biolog-source` checkout) that works, but when cloned from a worktree checked out on a *different* branch, the clone's checked-out branch matches the source's branch instead, so a local `main` never exists to branch `develop` off. Re-running the identical, untouched test from the canonical `biolog-source` checkout on `main` gives 11/11 (see `bio-fnd-001-suite-from-main-checkout.txt`). Flagging this as a latent fragility in BIO-FND-001's test for whoever owns it next, not fixing it myself — out of this story's `toolchain` conflict-group scope.

## CHANGES_REQUIRED round 1 — BIO-FND-002-QA-01 and QA-02 (fixed)

Seyi's independent review (`evidence/BIO-FND-002/codex/REVIEW.md`, `independent-review.sh`,
commit `29e57a1`) found two real defects, both now fixed in commit `d88f07e`:

- **QA-01 (High):** `scripts/check-toolchain.sh` never read `.nvmrc`, `global.json`, or
  `package.json` back and compared them against `toolchain/versions.json`. An isolated clone with
  every installed tool simulated at the manifest version still reported `SUCCESS` even after
  `.nvmrc` was changed to `99.0.0`, `global.json`'s SDK version to `9.9.999`, or `package.json`'s
  `packageManager`/`engines.node` to `pnpm@8.0.0`/`18.0.0`. **Fix:** added a
  `BIO-FND-002-AC02: native pin files vs toolchain/versions.json` section that reads each native
  pin and compares it to the manifest with the existing `check_exact` helper (plus a check that
  `global.json`'s `rollForward` is still `disable`).
- **QA-02 (Medium):** the MySQL client check used `check_min`'s plain "installed >= pinned"
  comparison, so a synthetic `mysql 9.0.0` passed against the pinned `8.4.0` baseline even though
  the policy names MySQL 8.4 as the specific supported family. **Fix:** added
  `check_min_same_major`, used only for the MySQL client (Docker Engine/Compose deliberately keep
  the original `check_min` — the policy never restricted their major; this distinction is now
  spelled out in `docs/TOOLCHAIN-POLICY.md` to prevent the same ambiguity resurfacing).

**Regression coverage added:** `tests/foundation/BIO-FND-002.test.sh` grew from 11 to 16 checks —
one per tampered native pin file (`.nvmrc`, `global.json`, `package.json`), one for the rejected
MySQL 9.x major, and one boundary case proving a same-major MySQL 8.5.0 still passes. Re-ran Seyi's
own `independent-review.sh` unmodified against the fix: all 11 of its probes now `PASS`, exit `0`
(see `independent-review-rerun-output.txt`; previously 4 `FAIL`).

Per the changes-required instruction, only `scripts/check-toolchain.sh`,
`tests/foundation/BIO-FND-002.test.sh`, and a clarifying addition to
`docs/TOOLCHAIN-POLICY.md` were touched. `evidence/BIO-FND-002/codex/` was left exactly as Seyi
supplied it (uncommitted, as with BIO-FND-001's review evidence).
