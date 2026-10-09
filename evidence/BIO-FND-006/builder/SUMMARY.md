# BIO-FND-006 Builder Evidence

Implementation commit: `1bd777655c0e02646a75e2127fd22310118a7618`

## Acceptance-criterion mapping

| Criterion | Implementation | Verification |
|---|---|---|
| AC01 .NET unit/integration projects | `tests/dotnet/Biolog.UnitTests/`, `tests/dotnet/Biolog.IntegrationTests/` | Both projects exist, are marked `IsTestProject`, and contain synthetic xUnit tests. |
| AC02 frontend unit and Playwright projects | `tests/frontend/` with Vitest and Playwright configs/specs | Package scripts, configs, and test files validated by the foundation suite. |
| AC03 Flutter scaffold | `tests/flutter/pubspec.yaml`, `tests/flutter/test/baseline_test.dart` | Flutter test dependency and synthetic test validated. |
| AC04 scan commands | `docs/TESTING.md` | Secret, NuGet vulnerability, pnpm audit, and Flutter outdated commands documented. |
| AC05 single quality command | `scripts/quality.sh` | Offline baseline command exited 0; `--full` orchestrates all executable ecosystem suites and fails on missing prerequisites. |

## Results

- `scripts/quality.sh`: PASS.
- `tests/foundation/BIO-FND-006.test.sh`: 10/10 PASS.
- `scripts/secret-scan.sh --staged` before implementation commit: clean.
- `scripts/secret-scan.sh --all` after implementation commit: clean.
- Shell and JavaScript syntax checks: PASS.
- Unknown quality argument: exit 2 as expected.
- `scripts/quality.sh --full`: exit 1 as expected on this bootstrap environment because ecosystem dependencies are not installed/restored. NuGet restore was also blocked by unavailable network access; the baseline command does not hide or download prerequisites.
- `git diff --check`: PASS.
- Git remote: none.

All test fixtures use explicit `SYNTHETIC` markers. No production data, secrets, remote, or push was used.
