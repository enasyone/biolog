# Local testing and quality

Run the repository baseline from the repository root:

```sh
scripts/quality.sh
```

This offline-safe command validates every test scaffold, runs foundation checks,
and scans tracked content for secrets. It does not download dependencies. Once
the pinned toolchains and dependencies are installed, run the executable suites:

```sh
scripts/quality.sh --full
```

`--full` runs .NET unit/integration tests, Vitest, Playwright and Flutter tests.
It fails rather than silently skipping when `dotnet`, `pnpm`, Flutter, installed
Node modules, or Playwright browsers are unavailable. Its Playwright run
(`tests/frontend/test:e2e`) auto-starts the `web/` app (BIO-FND-007), so
`web/`'s own dependencies must be installed too:

```sh
corepack pnpm --dir web install
corepack pnpm --dir tests/frontend install
corepack pnpm --dir tests/frontend exec playwright install chromium
```

All committed fixtures are synthetic and must be labelled `SYNTHETIC`. Never use
production personally identifiable information, biometrics, secrets or tenant data.

## Security and dependency scans

Run these locally from the repository root:

```sh
scripts/secret-scan.sh --all
dotnet list api/Biolog.Api/Biolog.Api.csproj package --vulnerable --include-transitive
dotnet list tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj package --vulnerable --include-transitive
dotnet list tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj package --vulnerable --include-transitive
pnpm --dir tests/frontend audit --audit-level high
flutter pub outdated --directory tests/flutter
```

The secret scan always runs in the baseline quality command. Dependency audits
need their ecosystem dependencies restored and may need cached or network-backed
advisory data; failures remain visible and non-zero in `--full` mode.

`dotnet list package --vulnerable` always exits `0` regardless of what it finds,
so it cannot fail a script on its own. `scripts/check-dotnet-vulnerabilities.sh`
wraps it and turns a found vulnerability into a failing exit code; it runs as
part of `scripts/quality.sh --full`'s `.NET vulnerability audit` stage against
every `.NET` project. `api/Biolog.Api` previously carried a High finding in
`Microsoft.OpenApi` (GHSA-v5pm-xwqc-g5wc) — resolved by pinning a patched 2.x
release rather than the breaking 3.x major; see `docs/API-SHELL.md` "Resolved:
Microsoft.OpenApi advisory".
