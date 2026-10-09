# BIOLOG ASP.NET Core API shell (BIO-FND-008)

`api/Biolog.Api` is a modular .NET host: health, OpenAPI, and a tenant middleware hook,
wired together through a small module-registration pattern rather than directly in
`Program.cs`. Auth hooks and the observability baseline (BIO-FND-009) are separate stories
that register into the same module host - this shell does not implement either.

## Running it locally (AC01)

```sh
dotnet run --project api/Biolog.Api
```

Boots against `ASPNETCORE_ENVIRONMENT=Development` by default
(`api/Biolog.Api/Properties/launchSettings.json`), which layers
`appsettings.Development.json` over `appsettings.json` - standard ASP.NET Core
configuration layering, unchanged by this shell. `tests/dotnet/Biolog.IntegrationTests`
asserts the layered value is actually read, not just present on disk.

## Health and readiness (AC02)

| Endpoint | Purpose |
|---|---|
| `GET /health/live` | Liveness: the process responded. Never evaluates a dependency check, so a slow dependency cannot make an orchestrator restart a healthy process. |
| `GET /health/ready` | Readiness: evaluates every registered health check. None are registered yet - this shell has no dependencies of its own - so it is `Healthy` by construction; a future story adds real checks (database, cache, broker) via `services.AddHealthChecks().AddCheck(...)` in `Modules/HealthModule.cs`. |

Deliberately **not** under `/api/v1` - ADR-0004's path versioning governs the platform's
public and service-to-service business API, not infrastructure liveness/readiness probes
consumed by an orchestrator or load balancer.

## OpenAPI (AC03)

`GET /openapi/v1.json` serves a generated OpenAPI 3.1 document via the framework's native
`Microsoft.AspNetCore.OpenApi` package (`services.AddOpenApi()` / `endpoints.MapOpenApi()`),
not a third-party generator - this avoids an extra dependency for a capability .NET now
ships itself. The document is currently empty (`paths: {}`): no business endpoints exist
yet for it to describe.

### Resolved: Microsoft.OpenApi advisory (GHSA-v5pm-xwqc-g5wc / CVE-2026-49451) — BIO-FND-008-QA-01

`Microsoft.AspNetCore.OpenApi` 10.0.5 pulls in `Microsoft.OpenApi` 2.0.0, which has a known
high-severity advisory (unauthenticated, network-triggerable process termination when
parsing a crafted circular OpenAPI document). `Biolog.Api.csproj` pins `Microsoft.OpenApi`
**2.12.2** explicitly - a patched release on the same 2.x line, not the 3.x major.

An earlier version of this shell claimed the whole 2.x line was vulnerable and only
switching to the breaking 3.x major would fix it. That claim was wrong: it was based on the
newest 2.x version that happened to already be in the local NuGet cache (2.4.1), never on
checking the full list of 2.x releases. The maintainer advisory's affected range actually
ends at 2.7.4; 2.7.5 and later 2.x releases are patched (confirmed independently by Seyi's
review). `dotnet package search Microsoft.OpenApi --exact-match` lists 2.5.0 through 2.12.2
that were never checked the first time. 2.12.2 (the latest 2.x at the time of this fix)
builds cleanly with zero warnings and `dotnet list ... --vulnerable` reports no vulnerable
packages.

The two things confirmed while investigating the *wrong* first answer remain true and are
still useful context: upgrading to the `Microsoft.OpenApi` **3.x** major does break the
build (`IOpenApiMediaType.Example` became read-only, incompatible with this source
generator's 2.x-shaped calls), and `Swashbuckle.AspNetCore` 10.1.7 resolves the same
vulnerable 2.4.1 by default - neither observation was wrong, they just didn't need to matter
once a patched 2.x release was found.

**Gate, not just documentation:** `dotnet list package --vulnerable` always exits `0`
regardless of what it finds, so a High/Critical finding here previously could not fail
`scripts/quality.sh --full` on its own - the story merely documented the warning.
`scripts/check-dotnet-vulnerabilities.sh <csproj>...` inspects the command's own output for
its "has the following vulnerable packages" phrasing and fails when present; it runs as
part of `scripts/quality.sh --full`'s `.NET vulnerability audit` stage against
`api/Biolog.Api`, `tests/dotnet/Biolog.UnitTests` and `tests/dotnet/Biolog.IntegrationTests`.

### Resolved: vulnerability gate false-success on audit failure — BIO-FND-008-QA-02

The first version of `scripts/check-dotnet-vulnerabilities.sh` only checked the captured
output for the vulnerable-packages phrase and ignored everything else, including the
`dotnet` invocation's own exit status. Seyi's re-review reproduced two false-PASS paths: a
fake `dotnet` returning a nonzero exit code, and a live environment where NuGet
advisory-source resolution failed (a `NU1900`-style warning) but `dotnet` still exited `0`
without printing either the vulnerable-packages phrase or the clean-result phrase - in both
cases the wrapper printed `DOTNET VULNERABILITY AUDIT: PASS`.

The wrapper now fails closed on three conditions instead of one: the vulnerable-packages
phrase, a nonzero exit status from `dotnet list`, and - since dotnet's only positive-success
text is `has no vulnerable packages` - output containing neither phrase (which covers an
advisory-source warning or any other unexpected output silently replacing the clean-result
line). `tests/foundation/BIO-FND-008.test.sh` regression-tests all three failure paths plus
the clean and vulnerable-found cases against a fake `dotnet` on `PATH`, offline.

## Tenant middleware hook (AC04)

`Tenancy/TenantContextMiddleware` runs on every request and gives later code one place to
read `ITenantContextAccessor.Context` - but it never resolves a tenant itself.
`Context` is `null` on every request in this shell, unconditionally, regardless of any
header or claim present. Per [ADR-0002](adr/ADR-0002-tenant-context-and-database-routing.md)
("never resolve tenancy yourself... there is no default tenant"), inventing a placeholder
tenant here would be worse than leaving requests unresolved - it would look like a
genuine, resolved tenant to every downstream consumer.

`TenantContext`'s field names (`TenantId`, `BindingKey`, `Channels`) loosely mirror
`contracts/schemas/tenant-context.schema.json` so a future story that ports ADR-0002's real
resolution logic to .NET has an obvious seam to replace `TenantContextMiddleware` with,
without renaming anything downstream - this record is **not** an implementation of that
contract (no channel-agreement, canonical-identifier or audit semantics) and nothing in
this shell constructs a non-null one.

## Module registration pattern (AC05)

Every cross-cutting concern implements `Modules/IBiologModule.cs`
(`RegisterServices`/`Configure`/`MapEndpoints`) and declares the names of modules it
depends on via `DependsOn`. `Modules/ModuleHost.cs` topologically sorts the registered
modules (Kahn's algorithm) once, in its constructor, before any lifecycle hook runs:

- A module always registers/configures/maps after every module it depends on.
- A cycle (including a module depending on itself, or a cycle hidden among otherwise
  resolvable modules) is rejected immediately with `CircularModuleDependencyException`
  naming every module that could not be ordered, instead of stack-overflowing or
  registering in an arbitrary, dependency-violating order.
- A module depending on a name that was never registered is rejected with
  `UnknownModuleDependencyException` - a distinct, more specific error than a generic cycle.

The three modules Program.cs actually registers (`HealthModule`, `OpenApiModule`,
`TenantModule`) currently have no dependencies on each other; the pattern is proven against
both synthetic fixtures and this real module list in
`tests/dotnet/Biolog.UnitTests/Modules/ModuleHostTests.cs`. A future story (observability,
auth, or a real tenant resolver) registers as an additional module through the same
pattern rather than being wired into `Program.cs` directly.

**The project-reference direction is one-way: `Biolog.Api` → feature project, never back.**
A story's `IBiologModule` adapter belongs in `api/Biolog.Api/Modules/` (see
`DocumentModule.cs`, `DeviceRegistryModule.cs`, `BioTen001Module.cs`), not inside the
feature project itself — putting it there forces the feature project's `.csproj` to
reference `Biolog.Api.csproj` back, and `dotnet build` reports the resulting circular
project reference as a silent multi-minute hang at "Determining projects to restore..."
with no diagnostic text, not a normal build error. `scripts/check-module-project-references.sh`
fails closed on this shape and runs in `scripts/quality.sh`.

## Project layout

```text
api/Biolog.Api/
├── Program.cs                      # module list + host bootstrap
├── appsettings.json / .Development.json
├── Modules/
│   ├── IBiologModule.cs
│   ├── ModuleHost.cs                # topological sort + cycle detection
│   ├── CircularModuleDependencyException.cs
│   ├── HealthModule.cs
│   ├── OpenApiModule.cs
│   └── TenantModule.cs
└── Tenancy/
    ├── TenantContext.cs
    ├── ITenantContextAccessor.cs
    └── TenantContextMiddleware.cs
```

## Tests

- `tests/dotnet/Biolog.UnitTests/Modules/ModuleHostTests.cs` - AC05: ordering, cycle
  detection (including self-referential and hidden-among-resolvable-modules cases),
  unknown-dependency and duplicate-name rejection, and the real shell module list.
- `tests/dotnet/Biolog.UnitTests/Tenancy/TenantContextMiddlewareTests.cs` - AC04: context
  stays `null` with no headers, with tenant-looking headers, and even overwriting a
  pre-existing non-null value (the hook must be authoritative, not merely additive).
- `tests/dotnet/Biolog.IntegrationTests/ApiShellTests.cs` - AC01/02/03 through a real
  in-memory HTTP pipeline (`WebApplicationFactory<Program>`): development configuration is
  actually layered in, both health endpoints respond `Healthy`, and the OpenAPI document is
  well-formed.

Run everything: `dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj` and
`dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj`, or
`scripts/quality.sh --full`.
