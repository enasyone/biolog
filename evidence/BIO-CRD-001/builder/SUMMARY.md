# BIO-CRD-001 — Builder evidence

**Story:** Implement versioned tenant brand asset library
**Owner:** Ngozi — Credential & Card Engineer
**Worktree:** `biolog-worktrees/BIO-CRD-001`, branch `feature/BIO-CRD-001` off `develop`
**Dependencies:** BIO-FND-007 (DONE), BIO-ARC-006 (DONE)
**Conflict group:** `brand-assets`
**Implementation commit:** `e697bd1f669b0b092c88f7f17a748a7c1b6f59ac` (round 1), remediated at `57580d71ce689cab938e218a049e41a312d32efc` (round 2, QA-01) and `d267988c0db304ae55036e74c8b3b007a2a4910b` (round 3, QA-02, current `reviewed_commit`)

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — PNG/JPEG/SVG uploads are supported | `BrandAssetFormatDetector` detects the real format from the file's own bytes (PNG signature/IHDR, JPEG SOI/SOF, SVG root element) — never the caller's claimed `Content-Type` or filename, which the malware hook (AC04) also depends on not being trusted. `POST /api/v1/brand-assets` accepts multipart uploads in all three formats. | `dotnet-test-unit-output.txt`, `dotnet-test-integration-output.txt` (`AC01_uploads_a_png...`, `AC01_uploads_a_jpeg_and_a_svg...`, `AC01_rejects_an_unsupported_file_format`) |
| AC02 — Assets record tenant, organisation/campus usage, version and status | `BrandAsset` record carries `tenant_id`, optional `organisation_id`/`campus_id`, `version` and `status` (`Pending`/`Approved`/`Rejected`/`Archived`). `lineage_id` groups every version of one logical asset as independent, immutable records — `POST /{lineageId}/versions` creates version N+1, never mutates an existing version. `POST /{id}/status` transitions status. | `dotnet-test-integration-output.txt` (`AC02_uploading_a_new_version...`, `Status_transition_endpoint...`), `dotnet-test-unit-output.txt` (`BrandAssetRepositoryTests`) |
| AC03 — Raster quality/effective DPI warnings are calculated | `RasterImageInspector` is a small, dependency-free PNG (`IHDR`/`pHYs`) and JPEG (`SOFn`/JFIF `APP0`) header parser: pixel dimensions, alpha support, native DPI. Effective DPI prefers a caller-supplied intended print size over native DPI; warns below a 150 DPI floor. SVG never carries a raster assessment — no false raster-DPI warning (Card Designer v0.3 test `BIO-TC-DES-022`). | `dotnet-test-unit-output.txt` (`RasterImageInspectorTests`, 9 tests incl. threshold boundary), `dotnet-test-integration-output.txt` (`AC02_AC03_low_resolution_warning...`) |
| AC04 — SVG/content validation and malware scanning hooks exist | `SvgValidator` rejects malformed XML, a non-`<svg>` root, `<script>`/`<foreignObject>`, event-handler attributes, `javascript:` URIs, and any `DOCTYPE` (closes XXE outright via `DtdProcessing.Prohibit` + `XmlResolver = null`, not just known-bad entity patterns). `IMalwareScanner`/`HeuristicMalwareScanner` (EICAR-detecting, executable-magic-byte detecting) runs before anything is persisted — a flagged upload is refused, never stored. | `dotnet-test-unit-output.txt` (`SvgValidatorTests` — 5 active-content/XXE cases, `HeuristicMalwareScannerTests`), `dotnet-test-integration-output.txt` (`AC04_rejects_svg_active_content_and_xxe_attempts` ×5, `AC04_rejects_a_file_flagged_by_the_malware_scan...`) |
| AC05 — Cross-tenant asset access is denied | `IBrandAssetRepository` scopes every read/write by an explicit `tenantId` parameter — structurally incapable of returning another tenant's row. `GET /{id}` for another tenant's asset is `404`, not `403` (ADR-0002's anti-enumeration posture, matched deliberately). `ITenantAuthenticator`/`HeaderTenantAuthenticator` (round 2, post-QA-01) requires BOTH a well-formed `X-Biolog-Tenant-Id` AND the matching `X-Biolog-Tenant-Secret`, loaded at real startup from application configuration by `TenantCredentialProvisioning.LoadFromConfiguration` (round 3, post-QA-02) — no secret committed to source, no HTTP self-registration; every failure mode returns the identical `401 tenant_authentication_failed`. | `dotnet-test-unit-output.txt` (`BrandAssetRepositoryTests`, `HeaderTenantAuthenticatorTests` incl. the QA-01 forgery case, `TenantCredentialProvisioningTests`), `dotnet-test-integration-output.txt` (`AC05_a_second_tenant_cannot_read...`, `AC05_list_is_scoped_to_the_caller_tenant_only`, `QA01_a_caller_cannot_read_another_tenants_asset_by_swapping_the_tenant_id_header...`, `QA02_a_tenant_credential_provisioned_through_real_application_configuration_can_authenticate`) |

## Developer tests (141/141 passing across two suites, plus 26/26 structural checks)

- `dotnet test tests/dotnet/Biolog.UnitTests` — **106/106** (54 new: `RasterImageInspectorTests` 9, `SvgValidatorTests` 8, `HeuristicMalwareScannerTests` 4, `BrandAssetRepositoryTests` 7, `IdempotencyStoreTests` 6, `HeaderTenantAuthenticatorTests` 7 + `InMemoryTenantCredentialStoreTests` 2 (round 2), `TenantCredentialProvisioningTests` 3 (round 3), plus the 52 pre-existing baseline/other-story tests, all still green). See `dotnet-test-unit-output.txt`.
- `dotnet test tests/dotnet/Biolog.IntegrationTests` — **35/35**, real HTTP requests through `WebApplicationFactory<Program>`: every AC end-to-end, every rejection path (missing/wrong tenant credential, missing idempotency-key header, unsupported format, 5 SVG active-content/XXE variants, EICAR malware hit), the QA-01 forgery attack end-to-end, the QA-02 real-configuration-provisioning path end-to-end, versioning, cross-tenant `404`, tenant-scoped listing, status transitions, idempotency replay (same key+content returns the same asset, does not duplicate) and reuse-with-different-content (`409 idempotency_key_reuse`). See `dotnet-test-integration-output.txt`.
- `tests/foundation/BIO-CRD-001.test.sh` — **26/26** offline-safe structural checks, wired into `scripts/quality.sh`'s base tier — including a check that no tenant secret is ever committed to checked-in `appsettings*.json`.
- `scripts/check-dotnet-vulnerabilities.sh` against all three `.csproj`s — clean (no new NuGet package was added by this story; only built-in BCL types are used — `System.Xml`, `System.Text.Json`, `System.Security.Cryptography`, `System.Buffers`).
- `python3 scripts/check-traceability.py --all` → `PASS (17 implemented stories)`. `python3 scripts/check-traceability.py governance/stories/BIO-CRD-001.json` → `PASS (BIO-CRD-001; 5 ACs)`. Added proactively as part of this story (BIO-FND-007's builder had to retrofit this after the fact when BIO-FND-010's tooling landed mid-flight; doing it upfront here avoids repeating that for whoever merges next).

Full `scripts/quality.sh --full` output is `quality-full-output.txt`. Everything specific to this story is green; the only failures in that run are:
- **`frontend unit tests` / `Playwright tests`** — `tests/frontend/node_modules` is not installed in this worktree (`sh: vitest: command not found`). This story never touches `web/`/`tests/frontend`; it is the documented, accepted "fails rather than silently skipping when ... installed Node modules ... are unavailable" gap (`docs/TESTING.md`), not a regression.

## CHANGES_REQUIRED round 1 — BIO-CRD-001-QA-01 (fixed)

Seyi's independent review (`evidence/BIO-CRD-001/codex/REVIEW.md`, commit `86c8375`, reviewing
implementation `e697bd1`) found one real, Critical defect:

- **QA-01 (Critical):** `HeaderTenantClaimReader` trusted a caller-supplied `X-Biolog-Tenant-Id`
  header as tenant authority after only a syntax check. Any caller could set that header to a
  victim tenant's id and every endpoint — which correctly filters by whatever tenant string it
  is given — would filter by the attacker's chosen victim. The existing cross-tenant tests only
  proved "tenant B cannot read tenant A's asset while honestly claiming B"; they never modelled
  a caller that lies about which tenant it is, so they could not catch this. **Fix:**
  - Replaced `ITenantClaimReader` with `ITenantAuthenticator`
    (`api/Biolog.Api/Features/BrandAssets/TenantAuthenticator.cs`), which requires BOTH a
    well-formed `X-Biolog-Tenant-Id` AND the matching `X-Biolog-Tenant-Secret` a new
    `ITenantCredentialStore` (`TenantCredentialStore.cs`) was seeded with — out of band, never
    through this module's own HTTP surface, so a caller cannot mint its own credential over the
    API.
  - Secret comparison is constant-time (`CryptographicOperations.FixedTimeEquals` against a
    deterministic dummy hash for an unregistered tenant id), so a caller cannot use response
    timing to distinguish "wrong secret" from "unknown tenant."
  - Every failure mode — missing headers, malformed id, wrong secret, or a victim's id paired
    with the attacker's own genuinely-valid secret for a *different* tenant — returns the
    identical `401 tenant_authentication_failed` (`TenantAuthenticationFailedException`,
    replacing `TenantClaimRequiredException`), so the response carries no oracle over which
    tenant ids exist or which part of a forged pair was wrong.
  - New tests prove the exact attack Seyi named is closed, not just that honest callers still
    work: `HeaderTenantAuthenticatorTests.QA01_a_caller_cannot_claim_a_victim_tenant_by_pairing_its_id_with_its_own_valid_secret`
    (unit) and
    `BrandAssetsEndpointTests.QA01_a_caller_cannot_read_another_tenants_asset_by_swapping_the_tenant_id_header_while_reusing_its_own_valid_secret`
    (integration, end-to-end through the real HTTP pipeline).
  - Re-ran the full suite after the fix: `tsc`-equivalent (`dotnet build`) clean,
    `tests/foundation/BIO-CRD-001.test.sh` 23/23, unit tests 103/103, integration tests 34/34 —
    all captured fresh in this evidence directory, superseding the round-1 output. `governance/stories/BIO-CRD-001.json`'s `reviewed_commit` and `evidence/BIO-CRD-001/builder/metadata.json` were updated to the remediation commit (`57580d7`).

Still explicitly NOT full ADR-0002 resolution — see "Deliberate scope boundaries" below and in
`docs/BRAND-ASSETS.md`; that remains BIO-TEN-001's job. This fix closes the specific gap Seyi
found (self-assertion with no verification) with the minimum unforgeable binding needed to make
AC05 a real, adversarially-testable property.

## CHANGES_REQUIRED round 2 — BIO-CRD-001-QA-02 (fixed)

Seyi's remediation re-review (`evidence/BIO-CRD-001/codex/REREVIEW.md`, commit `fa08377`,
reviewing `57580d7`) found the round-1 fix introduced a real regression:

- **QA-02 (High):** `BrandAssetsModule` registered an empty `InMemoryTenantCredentialStore`
  and no production startup/configuration code ever called `Register` — only unit tests and
  integration-test fixtures provisioned credentials, by resolving the store directly from DI.
  Every request to the actually composed host therefore returned
  `tenant_authentication_failed`, including a perfectly valid PNG/JPEG/SVG upload: the QA-01
  security fix had made the feature unusable, which is not a closure of AC01/AC05, it is a
  different failure mode of the same defect (no real tenant can ever authenticate). **Fix:**
  - Added `TenantCredentialProvisioning.LoadFromConfiguration` (`TenantCredentialStore.cs`):
    reads every `BrandAssets:TenantCredentials:<tenantId>` entry `IConfiguration` exposes —
    ASP.NET Core's standard layered configuration, so an operator sets
    `BrandAssets__TenantCredentials__<tenantId>=<secret>` as an environment variable (or
    `dotnet user-secrets` in Development) with no code change.
  - Wired as the real singleton factory in `BrandAssetsModule.RegisterServices` — this is
    the actual startup path the composed host uses, not a test-only shortcut.
  - No secret value is committed to source: neither `appsettings.json` nor
    `appsettings.Development.json` declares this section, and
    `tests/foundation/BIO-CRD-001.test.sh` now fails the build if one ever does. Still
    deliberately not an HTTP endpoint — provisioning is an operator/deployment concern.
  - New composed-host integration test
    (`QA02_a_tenant_credential_provisioned_through_real_application_configuration_can_authenticate`)
    configures a `WebApplicationFactory` via the same configuration mechanism an environment
    variable would use, and makes a real upload succeed — proving the production wiring
    itself, deliberately without calling `SeedTenant`/`ITenantCredentialStore.Register`
    directly the way every other test still does for convenience. This is exactly the
    "positive composed-host test that uses the same production provisioning path" Seyi's
    review required. Plus 3 new unit tests for the configuration-binding logic in isolation.
  - Re-ran the full suite after the fix: `dotnet build` clean, `tests/foundation/BIO-CRD-001.test.sh`
    26/26, unit tests 106/106, integration tests 35/35 — all captured fresh in this evidence
    directory, superseding rounds 1–2. `governance/stories/BIO-CRD-001.json`'s
    `reviewed_commit` and `evidence/BIO-CRD-001/builder/metadata.json` updated to the
    remediation commit (`d267988`).

## Direct fix — TD-CRD001-TENANT-ID-REGEX (2026-09-22, post-acceptance, already merged to develop)

Zainab's retroactive security review of the already-merged, already-accepted story found a
real High-severity defect: `TenantAuthenticator.cs`'s `TenantIdPattern` was
`^[a-z0-9][a-z0-9-]{0,63}$` — a generic slug pattern that can never match the real ADR-0001
SS1.2 canonical tenant id shape `ten_<UUIDv7>` (`api/features/bio-ten-001/Identifiers/CanonicalId.cs`),
because of the underscore immediately after the prefix. `TryAuthenticate` returned `null` the
moment the regex failed, **before the provisioned secret was ever checked** — so a genuine
tenant minted by the accepted BIO-TEN-001 registry could never authenticate to brand-assets,
even with the correct secret. Every existing test fixture used a non-canonical slug id (e.g.
`"tenant-alpha"`), which the old, too-permissive-in-the-wrong-way pattern happened to accept,
so none of them ever exercised a real canonical id and the defect shipped undetected. God
routed this directly to me (not through a full governance review cycle, since the story is
already accepted/merged) with instructions to fix it, run the existing harness plus a new
regression test, and report back for a direct merge.

**Fix:**
- Widened `TenantIdPattern` to `^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$`
  — matching `CanonicalId.cs`'s own `TenantIdPattern` exactly, not inventing a parallel
  definition of "valid tenant id."
- Updated every tenant-id fixture across `HeaderTenantAuthenticatorTests.cs` and
  `BrandAssetsEndpointTests.cs` from bare slugs to genuine `ten_<UUIDv7>` ids (a
  `NewTenantId()` helper using `Guid.CreateVersion7()` in each file, plus a couple of fixed
  literal canonical ids where a test needed a stable, readable constant) — these tests now
  actually exercise the real canonical shape end-to-end through the composed host, which is
  exactly the gap that let this regress silently.
- Added explicit regression coverage: a unit test
  (`Authenticates_a_real_canonical_ten_prefixed_uuid_v7_tenant_id`) and a full HTTP
  integration test
  (`TD_CRD001_a_real_canonical_ten_prefixed_uuid_v7_tenant_id_can_authenticate_end_to_end`),
  both using a fixed, genuine `ten_<UUIDv7>` literal so this can never silently regress
  again. Also widened the malformed-id theory cases to cover the canonical-adjacent near
  misses (hyphen instead of underscore after the prefix, wrong type prefix, wrong UUID
  version nibble, uppercase hex) so the pattern can't silently over-widen either.
- Added two structural checks to `tests/foundation/BIO-CRD-001.test.sh` (now 28/28): the
  pattern source itself accepts the canonical shape, and both new regression tests exist.
- Re-ran the full suite after the fix: `tests/foundation/BIO-CRD-001.test.sh` 28/28, unit
  tests 112/112, integration tests 36/36, `.NET vulnerability audit` clean. Committed as
  `159ec1c` on `feature/BIO-CRD-001`. `governance/stories/BIO-CRD-001.json`'s
  `reviewed_commit` and this file's `metadata.json` `commit_hash` updated to `159ec1c`.
- Did not touch ADR-0002/BIO-TEN-001 scope, migration-core, or any other file outside
  `brand-assets` — this is a narrow, single-cause fix, not a re-opening of the story.

## Decisions and assumptions (read `docs/BRAND-ASSETS.md` "Deliberate scope boundaries" for the full reasoning)

- **Persistence is in-memory**, not the tenant MySQL store the master spec targets. No prior story has wired real database access into `api/Biolog.Api`, and ADR-0005 requires every schema migration through a single, Kemi-held `migration-core` lock — a cross-cutting process this story's own `brand-assets` conflict group has no standing to start unilaterally. `IBrandAssetRepository` is the seam a real, migration-backed implementation drops in behind.
- **Tenant authentication is a local, credential-bound claim** (`X-Biolog-Tenant-Id` + `X-Biolog-Tenant-Secret`, verified against `ITenantCredentialStore`), not ADR-0002 resolution. `Tenancy.TenantContextMiddleware` deliberately always leaves its accessor null — real resolution is unbuilt anywhere in this host, pending BIO-TEN-001 (tenant registry, same wave, a different owner). Only `ITenantAuthenticator`'s implementation needs to change when that lands. (Round 1 shipped with just the id header and no credential check; Seyi's QA-01 review correctly identified that as insufficient — see "CHANGES_REQUIRED round 1" above.)
- **ADR-0004's problem-response/idempotency shapes are implemented locally to this feature**, not as shared `api/Biolog.Api`-wide infrastructure, since no .NET port of `contracts/js/api.mjs` exists yet and adding shared host infrastructure is outside this story's write scope without Kemi's sign-off. The wire shape matches the ADR exactly, so a future shared port is additive.
- **Idempotency-Key was made required on both upload endpoints**, not only the ADR's explicitly-named set (card activation, production completion, offline sync intake, webhook intake) — the ADR states that set "is not closed; a later story marks its own write endpoint as retry-prone the same way." A multipart file upload over a network is exactly the retry-prone shape the convention exists for, and Seyi's "TEST AT MINIMUM" list names replay/idempotency explicitly.
- **DPI floor of 150** is not specified anywhere in the repository; chosen as a conventional print-quality threshold and documented in code (`RasterImageInspector.LowResolutionDpiThreshold`) so it is one named constant to revise, not a magic number scattered through the feature.
- **governance/feature-path-ownership.json's BIO-CRD-001 entry** uses the same uniform per-feature-subtree template already flagged for the W02 shell stories (`api/features/bio-crd-001/**`, lowercase) — incompatible with the real `api/Biolog.Api/**` project structure BIO-FND-008 established. Followed the same precedent already recorded for BIO-FND-007/008: code lives at `api/Biolog.Api/Features/BrandAssets/**` (a real, compiling location, matching the sibling `Tenancy/`/`Observability/`/`Modules/` folders and the module-registration pattern), tests at `tests/dotnet/**/Features/BrandAssets/**` (matching the existing test-project layout), evidence/docs at the established repo-root conventions (`evidence/BIO-CRD-001/`, `docs/BRAND-ASSETS.md`). Not re-flagging this to god again since it is the identical, already-reported class of template mismatch.
