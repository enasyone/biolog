# BIO-PHO-002 — Builder evidence

**Story:** Implement Photo Booth integration contract
**Owner:** Ngozi — Credential & Card Engineer
**Conflict group:** `photo-booth`
**Dependencies:** BIO-PHO-001 (genuine runtime dependency — approval promotes into the real `PhotoMasterService`, already merged to `develop`), BIO-IDN-001 (person existence), BIO-DEV-001 (device existence/status)

## Source hierarchy check

No conflict found between the Master Technical Specification, accepted ADRs (ADR-0001,
ADR-0002, ADR-0003, ADR-0004, ADR-0005, ADR-0006) and the approved story/AC text. No new
shared-contract or migration change was needed or made.

## AC-to-evidence map

- **AC01 (capture specification):** `Domain/CaptureSpecification.cs` (fixed, versioned baseline
  naming frame size, aspect ratio, minimum DPI, face-height and background rules), `GET
  /api/v1/photo-booth/capture-specification`. Tests: `CaptureQualityGateTests` (AC01 tests),
  `PhotoBoothEndpointsIntegrationTests.AC01_the_capture_specification_is_readable_without_any_authentication`.
- **AC02 (device authentication):** `Api/DeviceAuthenticator.cs` (`HeaderDeviceAuthenticator`,
  requiring both a device id and a verified, out-of-band-provisioned secret, plus real
  `DeviceStatus.Active` status via `IDeviceExistenceGateway`), `Domain/DeviceCredentialProvisioning.cs`
  (real `IConfiguration`-backed startup path, applying BIO-CRD-001-QA-01/QA-02 from the start).
  Tests: `DeviceAuthenticatorTests` (incl. `BIOPHO002_QA01_...` forgery test and an
  inactive-device rejection test), `PhotoBoothEndpointsIntegrationTests` (AC02 tests),
  `PhotoBoothHostChannelTests` (real device registration/activation through BIO-DEV-001's own
  endpoints, plus a cross-tenant device-forgery rejection).
- **AC03 (idempotent retries):** `Domain/IdempotencyStore.cs` (claim/complete/release/get,
  ADR-0001 §3.4) wrapping `CaptureService.SubmitAsync`. Tests: `IdempotencyStoreTests`,
  `CaptureServiceTests` (AC03 tests, incl. a rejected-submission replay test),
  `PhotoBoothEndpointsIntegrationTests` (AC03 tests).
- **AC04 (no silent promotion):** `Domain/CaptureQualityGate.cs` (evaluated before any
  `BoothCapture` record exists), `Domain/CaptureStatus.cs` (Pending/Rejected/Promoted only),
  `Domain/IBoothCaptureRepository.cs` (repository-boundary refusal to replace a non-`Pending`
  capture), `CaptureService.ApproveAsync` (the one call site of
  `IPhotoMasterPromotionGateway.PromoteAsync`). Tests: `CaptureServiceTests` (AC04 tests, incl.
  rejected-can-never-be-approved and promoted-is-terminal tests),
  `PhotoBoothHostChannelTests.AC04_approving_a_real_capture_creates_a_real_independently_readable_BIOPHO001_photo_master`
  (a real promotion, independently confirmed by reading the resulting master back through
  BIO-PHO-001's own endpoint).

## Decisions and assumptions (read `docs/features/bio-pho-002/README.md` for the full reasoning)

- **Followed BIO-PHO-001's own project-per-feature convention** exactly
  (`api/features/bio-pho-002/Biolog.Features.PhotoBooth.csproj`, a thin one-way
  `Biolog.Api.Modules/BioPho002Module.cs` adapter). No `ProjectReference` to any other feature
  project — person existence, device existence, and master promotion each go through this
  project's own narrow gateway interface, implemented by an adapter in the module.
- **The one genuine cross-story runtime dependency in this epic so far.** Every prior sibling
  story's "Dependencies" metadata line has been sequencing, not coupling. This one calls the
  real BIO-PHO-001 `PhotoMasterService.AppendAsync` on approval — disclosed explicitly rather
  than following the "sequencing, not coupling" precedent by default.
- **Applied BIO-CRD-001-QA-01/QA-02's lessons from the start**, not after a review round: device
  authentication requires both a well-formed id and a verified, out-of-band-provisioned secret,
  and that provisioning reads real startup configuration from day one.
- **Applied BIO-PHO-001-QA-01's lesson from the start**: `BoothCapture.Content` uses the
  defensive-copy-in/defensive-copy-out treatment immediately, with matching regression tests.
- **Face-height/background compliance is booth-attested, not measured** — no computer-vision
  library exists anywhere in this host, and adding one is outside this story's declared scope.
  A `false` or omitted attestation is treated as a quality failure with the same weight as a
  measured DPI/dimension failure.
- **A DPI-rounding fix was applied to the quality gate during development** (not a review
  finding — caught by my own test run before commit): a PNG's `pHYs` chunk stores pixels-per-metre,
  not DPI directly, so converting a whole-number target DPI through that unit and back can
  legitimately land a fraction of a unit below it. The DPI check now rounds to the nearest whole
  number before comparing against the minimum, so a capture genuinely at the target resolution
  is never wrongly rejected by unit-conversion noise.
- **No new ADR-0001 canonical identifier type minted.** `BoothCapture` is keyed by
  `(PersonId, SequenceNumber)`, the same choice `PhotoMaster` (BIO-PHO-001) made.
- **Actor identity on approve/reject is audit-attribution only** (ADR-0006 Decision 1/2/4) — no
  role-gating logic exists anywhere in this feature; distinct from device authentication (AC02),
  which does verify a real credential.
- **Persistence is in-memory** (ADR-0005) — every repository interface is the seam a real,
  migration-backed implementation drops in behind.

## Test results (commit `e3a957503be9605756e2606a9b0f3227df6c9791`)

- `bash tests/features/bio-pho-002/BIO-PHO-002.test.sh` — 35/35 PASS
- `dotnet test tests/features/bio-pho-002/Biolog.Features.PhotoBooth.Tests.csproj` — 61/61 PASS
- `dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj` — 200/200 PASS
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj` — 104/104 PASS
  (includes 3 new `PhotoBoothHostChannelTests`, through the REAL packaged host)
- `bash scripts/check-module-project-references.sh` — PASS
- `python3 scripts/check-contract-freeze.py` — PASS (no shared-contract file touched)
- `python3 scripts/check-traceability.py --all` — PASS (21 implemented stories; this story is
  correctly outside the `governance/stories/*.json` gate, same as every project-per-feature
  sibling — its structural harness lives at `tests/features/bio-pho-002/`, not
  `tests/foundation/`)
- `bash scripts/quality.sh` (base tier) — **PASS**, see `evidence/BIO-PHO-002/builder/quality-output.txt`
- `bash scripts/quality.sh --full` — see `evidence/BIO-PHO-002/builder/quality-full-output.txt`.
  Every .NET check passes (unit 200/200, integration 104/104, document-verify, biometric-provider,
  mrz-engine, photography and photo-booth unit suites all PASS, `.NET vulnerability audit` PASS).
  `frontend unit tests` and `Playwright tests` fail with `vitest`/`playwright: command not
  found` — `tests/frontend/node_modules` is missing on the untouched canonical checkout too
  (confirmed directly), the same pre-existing, unrelated gap already documented for BIO-CRD-001
  and BIO-PHO-001; this story never touches `web/` or `tests/frontend/`. `Flutter tests` pass.
  Overall script exit reflects that pre-existing gap, not a regression from this story.

Full transcripts captured alongside this file.
