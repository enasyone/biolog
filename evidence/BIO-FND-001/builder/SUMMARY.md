# BIO-FND-001 — Builder evidence

**Story:** Initialise local-only repository and directory structure
**Owner:** Emeka
**Repository:** `biolog-source/` (sibling of the control pack), created fresh — no worktree, per orchestrator dispatch.

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — documented structure for web, API, mobile, tests, infrastructure, docs, evidence | Created `web/`, `api/`, `mobile/`, `tests/`, `infrastructure/`, `docs/`, `evidence/` (plus `scripts/`, `.githooks/`, `governance/` for tooling). Directories owned by later stories (BIO-FND-007/008/003/006/004) contain only a `README.md` placeholder to avoid scope conflict. | `directory-tree.txt`, `bootstrap-validation-output.txt` (AC01 section), `developer-test-output.txt` (AC01 checks) |
| AC02 — git initialised, main + develop, no remote | `git init -b main`, `develop` branch created and kept in sync with `main`; no remote ever added (verified live by adding then removing a throwaway local bare remote and confirming `git push` was rejected by the guard, not by remote absence). | `git-state.txt`, `bootstrap-validation-output.txt` (AC02), `developer-test-output.txt` (AC02) |
| AC03 — pre-push guard blocks push while `github_push_enabled` is false | Installed `.githooks/pre-push` (blocks unless `governance/release-state.yaml` has `github_push_enabled: true`) and set `core.hooksPath=.githooks`. Verified three ways: (1) direct hook invocation with the real (false) state, (2) a boundary test flipping the flag to true in a scratch copy of the state file to prove the hook is not always-fail, (3) a real end-to-end `git push` against a temporary local bare remote, which the guard rejected; the temporary remote was then removed and `git remote -v` / `.git/config` confirmed clean. | `bootstrap-validation-output.txt`, `developer-test-output.txt` (AC03 checks — both directions) |
| AC04 — no secrets or machine-specific absolute paths committed | `scripts/secret-scan.sh` (gitleaks if installed, otherwise a built-in pattern scanner) run before the first commit and again over the full tracked tree; a separate check greps tracked files for `/Users/<name>/` and `/home/<name>/`-style paths. A developer test proves the scanner actually rejects a fabricated AWS-style key, not just that it no-ops. | `secret-scan-output.txt`, `developer-test-output.txt` (AC04 checks, including the fixture-detection test) |
| AC05 — clean bootstrap validation script returns success | `scripts/validate-bootstrap.sh` re-checks AC01-AC04 end to end and exits `0` only when every check passes. | `bootstrap-validation-output.txt` (exit 0 recorded at file end) |

## Security/privacy criterion
"Secret scanning runs before the first commit" — `scripts/secret-scan.sh --staged` was run against the staged tree and confirmed clean immediately before `git commit` for both commits in this story (see command transcript in the session; result reproduced in `secret-scan-output.txt`).

## Notes for reviewers (Seyi, Zainab)
- Tools not installed on this laptop at build time: `pnpm`, `gitleaks`. `pnpm` is out of scope for this story (no Node project exists yet — BIO-FND-002 pins the toolchain, BIO-FND-007 creates the web app). `gitleaks` absence is handled by the documented fallback scanner in `scripts/secret-scan.sh`; installing gitleaks later requires no script changes since it is auto-detected.
- `flutter`/`codex` produced non-fatal warnings related to local sandbox permissions on this machine unrelated to repository content; they do not affect any AC for this story.
- This story intentionally does not scaffold real application code inside `web/`, `api/`, or `mobile/` — see each directory's `README.md` for the story that owns that work, to respect conflict-group boundaries.
- No GitHub remote exists in the repository. The one used to prove AC03 end-to-end was local-only (`git init --bare` under `$TMPDIR`), added, exercised, then fully removed in the same command sequence.

## CHANGES_REQUIRED round 1 — BIO-FND-001-QA-01 (fixed)

Seyi's independent review (`evidence/BIO-FND-001/codex/REVIEW.md`, `independent-review.sh`) found
that `scripts/secret-scan.sh` captured `git ls-files` / `git diff --cached --name-only` into a
whitespace-delimited shell string and iterated with `for f in $files`. A tracked path containing a
space (e.g. `docs/leaked secret.txt`) was split into nonexistent path fragments and never scanned,
so a committed secret in such a path was missed and `scripts/validate-bootstrap.sh` returned a
false success (High severity, security-gate bypass).

**Fix (commit `5dc09e6`):** enumerate tracked/staged paths NUL-delimited (`git ... -z`) and read
them via `while IFS= read -r -d ''` fed by process substitution, never through a plain shell
variable (which cannot hold NUL bytes). No other logic changed; `scripts/validate-bootstrap.sh`
required no direct edit since it already calls the fixed scanner.

**Regression coverage added:** `tests/foundation/BIO-FND-001.test.sh` now clones the repository,
commits a synthetic AKIA-style key under a path containing a space, and asserts both
`secret-scan.sh` and `validate-bootstrap.sh` fail on it (11/11 checks pass, see
`developer-test-output.txt`). Seyi's own `independent-review.sh` was re-run unmodified and now
exits `0`, with `PASS AC04 scanner catches committed secret in whitespace path` and
`PASS AC05 validator rejects committed secret in whitespace path` both passing.

Per the changes-required instruction, only `scripts/secret-scan.sh` and
`tests/foundation/BIO-FND-001.test.sh` were touched; `evidence/BIO-FND-001/codex/` was left exactly
as Seyi supplied it.
