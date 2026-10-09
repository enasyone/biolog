# BIO-FND-008 — Builder evidence

**Story:** Create ASP.NET Core API/service shell
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-FND-008`, branch `feature/BIO-FND-008` off `develop`
**Dependency:** BIO-ARC-006 (DONE — shared contracts frozen at W01 exit)
**Commit:** `8f14088787b38c4f66e167d197d32beeaaab9eed`

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — API boots locally against development configuration | `api/Biolog.Api` (ASP.NET Core, net10.0) with `Properties/launchSettings.json` setting `ASPNETCORE_ENVIRONMENT=Development`, layering `appsettings.Development.json` over `appsettings.json`. Live-verified with `dotnet run` + `curl` during development (health/OpenAPI both responded); an integration test asserts the environment name and a config value that only the Development file sets. | `dotnet-integration-test-output.txt` (`AC01_boots_against_development_configuration...`), `structural-test-output.txt` |
| AC02 — health/readiness endpoints exist | `Modules/HealthModule.cs`: `/health/live` (never evaluates checks — liveness only) and `/health/ready` (evaluates every registered check). Both return `200 Healthy` — proven live and by integration test. | `dotnet-integration-test-output.txt`, `structural-test-output.txt` |
| AC03 — OpenAPI generation works | `Modules/OpenApiModule.cs` using the framework's native `Microsoft.AspNetCore.OpenApi` (no third-party generator). `/openapi/v1.json` returns a well-formed OpenAPI 3.1 document — proven live and by integration test. | `dotnet-integration-test-output.txt`, `dotnet-vulnerable-packages.txt` (known advisory, see below) |
| AC04 — tenant middleware hook exists but does not invent tenant defaults | `Tenancy/TenantContextMiddleware.cs` unconditionally sets `ITenantContextAccessor.Context = null` — reads no header, claim or query value, and overwrites rather than merely defaulting a pre-existing context. Field shape in `Tenancy/TenantContext.cs` loosely mirrors `contracts/schemas/tenant-context.schema.json` for a future ADR-0002 resolver. | `dotnet-unit-test-output.txt` (`TenantContextMiddlewareTests`), `structural-test-output.txt` |
| AC05 — module registration pattern prevents circular dependencies | `Modules/IBiologModule.cs` + `Modules/ModuleHost.cs`: Kahn's-algorithm topological sort run once in the constructor; a cycle (two-module, self-referential, or hidden among otherwise resolvable modules) raises `CircularModuleDependencyException` naming every unresolved module; an unregistered dependency raises a distinct `UnknownModuleDependencyException`. | `dotnet-unit-test-output.txt` (`ModuleHostTests`, 8 cases), `structural-test-output.txt` |

## Developer tests (19 pass)

- `tests/dotnet/Biolog.UnitTests/Modules/ModuleHostTests.cs` (8 tests) — AC05.
- `tests/dotnet/Biolog.UnitTests/Tenancy/TenantContextMiddlewareTests.cs` (4 tests) — AC04.
- `tests/dotnet/Biolog.UnitTests/BaselineTests.cs` (1 test, pre-existing, now compiles — see below).
- `tests/dotnet/Biolog.IntegrationTests/ApiShellTests.cs` (4 tests) — AC01/AC02/AC03, through a real `WebApplicationFactory<Program>` HTTP pipeline, not mocked.
- `tests/dotnet/Biolog.IntegrationTests/BaselineIntegrationTests.cs` (1 test, pre-existing, now compiles).
- `tests/foundation/BIO-FND-008.test.sh` (35 checks) — offline-safe structural wiring checks, no `dotnet` toolchain required, matching the convention established by BIO-FND-006/007.

Reproduce with:
```sh
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
bash tests/foundation/BIO-FND-008.test.sh
```
(the exact `test_command` in `metadata.json`, chained with `&&`, exits 0.)

`scripts/quality.sh --full` was also run in full: its `.NET unit tests` and `.NET integration tests` stages both `PASS` (see `quality-full-output.txt`). The overall command exits non-zero only because of `frontend unit tests` / `Playwright tests` failing with `pnpm: command not found` — a pre-existing, unrelated gap on this machine (pnpm has never been installed here; documented back in BIO-FND-002's toolchain check) that has nothing to do with this story's `.NET` work.

## Pre-existing bug fixed incidentally: BIO-FND-006's baseline test files didn't compile

`tests/dotnet/Biolog.UnitTests/BaselineTests.cs` and
`tests/dotnet/Biolog.IntegrationTests/BaselineIntegrationTests.cs` (both from BIO-FND-006)
used `[Fact]`/`Assert` without a `using Xunit;` directive, so **both test projects failed to
build at all** (`CS0246: The type or namespace name 'FactAttribute' could not be found`) —
confirmed before making any change of my own. Since I need to add new files to these exact
same two projects, this wasn't a "wander into someone else's file" situation: the shared
project literally would not compile for me to add anything to it. Fixed with the minimal
one-line addition (`using Xunit;`) to each file; no other change to either file.

## CHANGES_REQUIRED round 1 (QA-01): Microsoft.OpenApi security advisory (GHSA-v5pm-xwqc-g5wc)

The original submission pinned `Microsoft.OpenApi` at **2.4.1** and documented the whole 2.x
line as unfixed, based only on checking the local NuGet cache rather than the full package
registry. Seyi's review (commit `372fd6b`) correctly identified that the maintainer
advisory's affected range ends at 2.7.4 and that 2.7.5+ is patched.

Fix (`fb8c488`): `dotnet package search Microsoft.OpenApi --exact-match` showed releases up
to 2.12.2; pinned that version. `dotnet build` is clean (0 warnings) and `dotnet list
package --vulnerable` reports no vulnerable packages. Since that command always exits `0`
regardless of findings, it was not an actual gate — `fb8c488` also added
`scripts/check-dotnet-vulnerabilities.sh`, wired into `scripts/quality.sh --full`, to turn
the vulnerable-packages phrase in its output into a real nonzero exit.

A self-introduced regression was caught and fixed in the same round: rewording the csproj's
advisory comment accidentally included a literal `--` inside an XML `<!-- -->` comment,
which is invalid and made the whole `.csproj` fail to load (`MSB4025`). Caught by reading
the tail of captured evidence output before trusting it, not by an external reviewer.
Fixed in `ee9f6d1`, all evidence recaptured from a real build.

See `docs/API-SHELL.md` "Resolved: Microsoft.OpenApi advisory" for the full narrative,
including the wrong first claim. `dotnet-vulnerable-packages.txt` /
`dotnet-vulnerability-audit-output.txt` hold the current clean result.

## CHANGES_REQUIRED round 2 (QA-02): vulnerability gate false-success on audit failure

Seyi's re-review (`evidence/BIO-FND-008/codex/REREVIEW-ROUND-2.md`) found that
`scripts/check-dotnet-vulnerabilities.sh` only checked the captured output for the
vulnerable-packages phrase and never checked `dotnet list`'s own exit status. A fake
`dotnet` returning a nonzero exit code, or a live NuGet advisory-source failure that leaves
`dotnet` exiting `0` without printing either the vulnerable-packages or clean-result phrase,
both fell through to a printed `PASS` and exit `0`.

Fixed by failing closed on three conditions instead of one: the vulnerable-packages phrase,
a nonzero `dotnet list` exit status, and — since `has no vulnerable packages` is the only
positive-success text `dotnet` prints — output containing neither phrase. Regression
coverage added to `tests/foundation/BIO-FND-008.test.sh` (clean, vulnerable-found, nonzero
exit, and advisory-failure-with-exit-0 cases, each via a fake `dotnet` on `PATH`, offline).
Re-ran Seyi's own `evidence/BIO-FND-008/codex/independent-review.py` probe script after the
fix — all 4 probes pass, including "vulnerability audit fails closed when dotnet audit
fails". See `docs/API-SHELL.md` "Resolved: vulnerability gate false-success on audit
failure" for the full narrative.

## Ownership-template gap (ratified resolution, per god)

`governance/feature-path-ownership.json` applies one uniform 6-path template to all 83
W02-W15 stories (`api/features/<key>/**`, `web/features/<key>/**`,
`mobile/features/<key>/**`, `tests/features/<key>/**`, `docs/features/<key>/**`,
`evidence/features/<key>/**`), including an entry for `BIO-FND-008` shaped identically to
every other story regardless of what that story actually builds. That template cannot
literally accommodate this story: the approved AC/DoD and the pre-existing `api/README.md`
(written when `api/` was first scaffolded, BIO-FND-001) both call for the API service shell
at `api/` root, not nested under `api/features/bio-fnd-008/`. Ngozi hit the identical
conflict on BIO-FND-007/`web/`; god ratified her resolution as the standing interpretation
and confirmed it applies here too: the source hierarchy already in the builder packet
(story/AC/DoD, then pre-existing repository text) outranks the ownership registry's
convenience per-feature template when the two conflict, and the registry's `owned_paths`
are planning/conflict-avoidance guidance, not a technical write-gate - no freeze amendment
or ADR is needed to scaffold at `api/` root. Nothing on the registry's own
`read_only_shared_contracts` list (`contracts/**`, `docs/adr/**`,
`governance/contract-freeze/**`, `governance/feature-path-ownership.json`,
`scripts/check-contract-freeze.py`, `docs/architecture/SHARED-CONTRACT-FREEZE.md`) was
touched by this story.

## Notes for reviewers (Seyi)

- No auth implementation and no full observability stack are included — the user story
  mentions "auth hooks" and "observability extension points" in prose, but neither is a
  numbered AC. Both are separate stories (observability is explicitly BIO-FND-009); the
  `IBiologModule` pattern this story ships is the seam either registers into later. No
  empty placeholder "AuthModule"/"ObservabilityModule" classes were added, since they would
  have had no real behavior to test.
- Health/readiness endpoints are deliberately **not** under `/api/v1` (ADR-0004's path
  versioning governs the platform's business API, not infrastructure liveness/readiness
  probes) — a judgment call, documented in `docs/API-SHELL.md`.
- `contracts/` (Kemi's, frozen at W01 exit) was read but not modified. `Tenancy/TenantContext.cs`
  is explicitly documented as NOT an implementation of `contracts/schemas/tenant-context.schema.json`
  or ADR-0002 — only a placeholder shape for a future story to replace.
