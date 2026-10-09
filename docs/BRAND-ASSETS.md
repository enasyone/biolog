# Versioned tenant brand asset library (BIO-CRD-001)

`api/Biolog.Api/Features/BrandAssets` lets a tenant designer upload approved logos, crests,
signatures and watermark/security artwork, per Card Designer v0.3 §9 (`reference/`) and the
approved story's five acceptance criteria below. It registers into the module host the same
way every other cross-cutting concern does (`Modules/IBiologModule.cs`, BIO-FND-008-AC05).

## Endpoints

All under `/api/v1/brand-assets`, versioned per ADR-0004 §1.1. Every request requires
`X-Biolog-Tenant-Id` **and** `X-Biolog-Tenant-Secret` headers (see "Tenant scoping" below);
every write additionally requires `Idempotency-Key` (ADR-0004 §4).

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/` | Upload a new brand asset (creates version 1 of a new lineage). |
| `POST` | `/{lineageId}/versions` | Upload a new version of an existing lineage. |
| `GET` | `/` | List the caller's tenant's assets. Filters: `assetType`, `status`, `organisationId`, `campusId`. Cursor-paginated (`limit`, `cursor`) per ADR-0004 §5.1. |
| `GET` | `/{id}` | Get one asset. `404` (not `403`) for an unknown id or another tenant's id - ADR-0002's anti-enumeration posture. |
| `POST` | `/{id}/status` | Transition status: `{"status": "Approved" \| "Rejected" \| "Archived"}`. |

Errors follow ADR-0004 §2.1's standard problem envelope
(`code`/`message`/`status`/`correlation_id`/`detail`?/`errors`?), `application/problem+json`.

## Acceptance criteria

- **AC01** PNG/JPEG/SVG uploads are supported. Format is detected from the file's own bytes
  (`BrandAssetFormatDetector`), never trusted from the caller's `Content-Type` or filename -
  a mismatched claim is exactly the disguise the malware hook (AC04) also guards against.
- **AC02** Every asset record carries `tenant_id`, `organisation_id`/`campus_id` (both
  optional), `version` and `status` (`BrandAssetModels.cs`). `lineage_id` groups every
  version of "the same" logical asset; each version is an independent, immutable record -
  a re-upload creates a new version rather than mutating one, matching the published-template
  immutability precedent used elsewhere in the Card Designer spec.
- **AC03** Raster quality/effective DPI (`RasterImageInspector.cs`): pixel dimensions and
  alpha support are read from PNG `IHDR`/JPEG `SOFn`; effective DPI prefers the caller's
  intended print size (`intendedWidthMm`) over the file's own native DPI metadata (PNG
  `pHYs`/JPEG JFIF `APP0`), and warns below a 150 DPI print-quality floor. SVG never carries
  a raster quality assessment - scalable by construction, never a false raster-DPI warning
  (Card Designer v0.3 test `BIO-TC-DES-022`).
- **AC04** SVG/content validation (`SvgValidator.cs`) rejects malformed XML, a non-`<svg>`
  root, `<script>`/`<foreignObject>` elements, `on*` event-handler attributes, `javascript:`
  URIs, and any `DOCTYPE` (closing the XXE/external-entity vector outright, not just
  known-bad entities) - Card Designer's adversarial case "invalid SVG or active-content
  upload". The malware-scanning hook (`IMalwareScanner`/`HeuristicMalwareScanner.cs`) runs
  before anything is persisted; a flagged upload is refused, never stored.
- **AC05** Cross-tenant asset access is denied (`BrandAssetRepository.cs`,
  `TenantAuthenticator.cs`, `TenantCredentialStore.cs`): every read/write is scoped by an
  explicit `tenantId` parameter, structurally incapable of returning another tenant's row;
  a lookup that doesn't match is `404`, identical to "does not exist", per ADR-0002's
  anti-enumeration posture. Which tenant a request acts as must itself be proven, not just
  declared: a request must present a per-tenant secret the credential store verifies
  (constant-time), not merely a self-asserted tenant-id header — closing BIO-CRD-001-QA-01
  (Seyi's independent review), where a bare id header let any caller pick any tenant.

## Deliberate scope boundaries (read before extending this feature)

Three things this story does **not** do, each because the real mechanism belongs to a
different story this one has no standing to build:

1. **Persistence is in-memory** (`InMemoryBrandAssetRepository`), not the tenant MySQL store
   the master specification targets. No prior story has wired real database access into
   `api/Biolog.Api`, and ADR-0005 requires every schema migration to go through a single,
   Kemi-held `migration-core` lock and sequence assignment - a cross-cutting process this
   story's own `brand-assets` conflict group has no standing to start unilaterally. A real,
   migration-backed repository is a drop-in replacement behind `IBrandAssetRepository`;
   nothing above it depends on being in-memory.
2. **Tenant authentication is a local, credential-bound claim, not ADR-0002 resolution.**
   `Tenancy.TenantContextMiddleware` deliberately always leaves its accessor null - real
   tenant resolution (host/principal/service channel agreement, a verified domain registry)
   is unbuilt anywhere in this host yet, on purpose, pending BIO-TEN-001 (tenant registry).
   `ITenantAuthenticator` requires a request to present BOTH a well-formed `X-Biolog-Tenant-Id`
   AND the matching `X-Biolog-Tenant-Secret` a `ITenantCredentialStore` was seeded with out
   of band (never through this module's own HTTP surface) - not a bare, self-asserted id
   alone, which BIO-CRD-001-QA-01 found grants access to any tenant a caller cares to name.
   "Well-formed" means the real ADR-0001 SS1.2 canonical shape `ten_<UUIDv7>` (e.g.
   `ten_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b`) - the exact id
   `api/features/bio-ten-001/Identifiers/CanonicalId.cs` mints, not an arbitrary slug.
   TD-CRD001-TENANT-ID-REGEX (Zainab's retroactive security review, filed after this story
   was already merged): the pattern originally shipped here was a generic slug pattern that
   happened to reject every real canonical tenant id, so a genuine BIO-TEN-001 tenant could
   never authenticate even with its correct provisioned secret - `TryAuthenticate` returned
   null before the secret was ever checked. Fixed to match `CanonicalId.cs`'s own pattern
   exactly.
   Every failure mode returns the identical generic `401 tenant_authentication_failed`, so a
   caller cannot use this endpoint to enumerate which tenant ids exist. This still fabricates
   nothing that looks like a genuine ADR-0002-resolved tenant to a downstream consumer - it
   is a narrower, honestly-scoped property ("this caller proved possession of a credential
   for this tenant id"), not host/principal/service channel agreement. When BIO-TEN-001
   lands, only `ITenantAuthenticator`'s implementation needs to change.
3. **ADR-0004's problem-response/idempotency shapes are implemented locally to this feature**
   (`ProblemResponse.cs`, `IdempotencyStore.cs`), not as shared `api/Biolog.Api`-wide
   infrastructure. No .NET port of `contracts/js/api.mjs` exists anywhere in this host, and
   creating shared host-level infrastructure is outside this story's own write scope without
   Kemi's sign-off (the ADR itself defers even migrating the three existing JS error classes
   onto a shared base as later work). These classes reproduce the ADR's wire shape exactly,
   so a future shared C# port is additive, not a breaking rename of what a client already
   received from these endpoints.

## Running it locally

No endpoint provisions a tenant credential - that is deliberate (see "Deliberate scope
boundaries" above). `TenantCredentialProvisioning.LoadFromConfiguration`
(`TenantCredentialStore.cs`), wired into `BrandAssetsModule.RegisterServices`, is the real
startup path: it reads every `BrandAssets:TenantCredentials:<tenantId>` entry
`IConfiguration` exposes - ASP.NET Core's standard layered configuration, so the ordinary
`BrandAssets__TenantCredentials__<tenantId>=<secret>` environment-variable shape (double
underscore = section separator) or `dotnet user-secrets` in Development work with no code
change. **No secret value is committed to this repository** - neither `appsettings.json`
nor `appsettings.Development.json` declares this section, on purpose
(`tests/foundation/BIO-CRD-001.test.sh` fails the build if one ever does).

```sh
export BrandAssets__TenantCredentials__ten_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b="<a locally chosen secret>"
dotnet run --project api/Biolog.Api

curl -X POST http://localhost:5000/api/v1/brand-assets \
  -H "X-Biolog-Tenant-Id: ten_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b" \
  -H "X-Biolog-Tenant-Secret: <the same secret>" \
  -H "Idempotency-Key: $(uuidgen)" \
  -F "assetType=PrimaryCrest" \
  -F "file=@crest.png;type=image/png"
```

## Tests

- `tests/dotnet/Biolog.UnitTests/Features/BrandAssets/` - pure-logic tests for DPI
  calculation, SVG active-content rejection, the malware heuristic, tenant credential
  authentication (including the QA-01 forged-id-with-a-different-tenant's-own-valid-secret
  attack), tenant-scoped repository behaviour, and idempotency store semantics
  (claim/complete/release, concurrent-claim exclusivity, scope-key injectivity).
- `tests/dotnet/Biolog.IntegrationTests/Features/BrandAssets/` - real HTTP requests through
  `WebApplicationFactory<Program>`: upload success across all three formats, every rejection
  path (missing/wrong tenant credential, missing idempotency key, unsupported format,
  active-content SVG, malware hit), the QA-01 forgery attack end-to-end, versioning,
  cross-tenant denial (404), list scoping, status transitions, and idempotency replay/reuse.
- `tests/foundation/BIO-CRD-001.test.sh` - offline-safe structural checks, wired into
  `scripts/quality.sh`.
