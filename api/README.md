# api/

BIOLOG backend services: ASP.NET Core / .NET modular services, per
`02-ARCHITECTURE/MASTER-TECHNICAL-SPECIFICATION.md` section 1 (Stack).

`Biolog.Api/` is the modular host shell (BIO-FND-008): health/readiness endpoints, OpenAPI
generation, and a tenant middleware hook, registered through a small module pattern. See
`docs/API-SHELL.md` for the full guide and `dotnet run --project api/Biolog.Api` to run it
locally.

Business feature endpoints, real tenant resolution (ADR-0002), auth, and the observability
baseline (BIO-FND-009) are separate stories that register into this same module host rather
than being added to this directory ad hoc.
