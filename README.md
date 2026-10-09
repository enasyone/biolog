# BIOLOG

Local-only source repository for the BIOLOG platform. This repository is built and
accepted entirely on the laptop; a GitHub remote is intentionally absent until
Wave 15 (W15) unlocks publication (see `governance/release-state.yaml`).

## Layout

| Path             | Purpose                                                              | Populated by |
|------------------|-----------------------------------------------------------------------|--------------|
| `web/`           | React/Next.js + TypeScript web application                            | BIO-FND-007 |
| `api/`           | ASP.NET Core / .NET modular backend services                          | BIO-FND-008 |
| `mobile/`        | Flutter mobile application (Android first, iOS later)                 | later wave |
| `tests/`         | Cross-cutting and per-story developer tests                           | ongoing, formalised by BIO-FND-006 |
| `infrastructure/`| Local Docker development infrastructure and IaC                       | BIO-FND-003 |
| `contracts/`     | Shared cross-cutting contracts (identifiers, clocks, versioning); owned by Kemi | BIO-ARC-001 |
| `docs/`          | Engineering and product documentation, including ADRs in `docs/adr/`  | ongoing |
| `evidence/`      | Builder/reviewer/acceptance evidence per story, keyed by story ID     | ongoing, formalised by BIO-FND-004 |
| `scripts/`       | Bootstrap, validation, secret-scan and toolchain-check tooling         | BIO-FND-001, BIO-FND-002 |
| `.githooks/`     | Local git hooks, including the mandatory pre-push guard               | BIO-FND-001 |
| `governance/`    | This repository's own local release-state (github_push_enabled flag)  | BIO-FND-001 |
| `toolchain/`     | Pinned toolchain version manifest (single source of truth)            | BIO-FND-002 |
| `global.json`, `.nvmrc`, `package.json` | Native per-tool version pins kept in sync with `toolchain/versions.json` | BIO-FND-002 |

Each populated-later directory currently contains a `README.md` placeholder only,
so the story that owns it can build without conflicting with this one.

## Local-first rule

- No GitHub remote may be added and no `git push` may succeed before W15.
- `git config core.hooksPath .githooks` is set on this repository; `.githooks/pre-push`
  blocks any push while `governance/release-state.yaml` does not have
  `github_push_enabled: true`.
- Use synthetic data only. Never commit secrets, credentials, or machine-specific
  absolute paths.

## Bootstrap and validation

```sh
scripts/validate-bootstrap.sh
```

Runs the BIO-FND-001 structural, git, guard and secret-scan checks and exits `0`
only when all pass. See `tests/foundation/BIO-FND-001.test.sh` for the developer
test suite mapped to each acceptance criterion, and
`evidence/BIO-FND-001/builder/` for recorded builder evidence.

## Toolchain policy

```sh
scripts/check-toolchain.sh
```

Reports drift against the pinned toolchain in `toolchain/versions.json` and
fails when an installed tool is missing or does not satisfy its pinned
version. See `docs/TOOLCHAIN-POLICY.md` for the full policy,
`tests/foundation/BIO-FND-002.test.sh` for developer tests mapped to each
acceptance criterion, and `evidence/BIO-FND-002/builder/` for recorded
builder evidence.
## Acceptance evidence

Every story uses the versioned four-stage convention and metadata contract in
`evidence/README.md`. Check completed evidence locally with:

```sh
python3 scripts/check-evidence.py <STORY-KEY>
```
## Baseline quality suite

```sh
scripts/quality.sh
```

Runs the offline-safe baseline harness and tracked secret scan. Use
`scripts/quality.sh --full` after ecosystem dependencies are installed to run
the .NET unit/integration, Vitest, Playwright and Flutter suites. Security and
dependency-scan commands are documented in `docs/TESTING.md`.
