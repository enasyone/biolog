# BIO-DEV-003 — Builder evidence

**Story:** Implement vendor adapter framework for devices
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-DEV-003`, branch `feature/BIO-DEV-003` off `develop`
**Dependencies:** BIO-DEV-001 (device registry, merged), BIO-DEV-002 (device certification, merged)
**Commit:** `b26de8c`

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — vendor SDK code is isolated behind typed interfaces | `Biolog.Features.DeviceAdapters/Domain/IDeviceAdapter.cs` (one typed interface), four concrete adapters (`Adapters/*.cs`) — Acme (fingerprint scanner), NovaVision (face terminal), OrbitID (NFC/UHF reader), PrintWorks (card printer) — each the sole caller of its own internal vendor SDK simulator | `DeviceAdapterRegistryTests.cs` (7/7), `BIO-DEV-003-structural-harness-output.txt` |
| AC02 — device capability discovery and health are supported | `DeviceAdapterService.DiscoverCapabilitiesAsync`/`CheckHealthAsync`; `GET /api/v1/devices/{id}/capabilities` and `GET /api/v1/devices/{id}/health` (`BioDev003Module`) | `DeviceAdapterServiceTests.cs`, `DeviceAdapterEndpointsTests.cs` (real host) |
| AC03 — unsupported firmware/SDK combinations surface certification status | `ICertificationGate` checked before the adapter registry is even resolved; `BioDev003Module.CertificationGateAdapter` bridges the real `DeviceCertificationService`; `409 device_combination_unsupported` with `certification_status` | `DeviceAdapterServiceTests.cs` (gate-before-registry proof), `DeviceAdapterEndpointsTests.cs` (real certification matrix, both "no record" and explicit "Unsupported" paths) |
| AC04 — no vendor SDK type leaks into domain contracts | `VendorSdks/**` all `internal`, compiler-enforced; `VendorSdkIsolationTests.cs` reflection test walks every public method in the assembly | `VendorSdkIsolationTests.cs` (4/4), structural harness |
| AC05 — `/devices` connected or explicitly documented HYBRID | `/devices` (BIO-UX-010's owned surface) left unchanged; HYBRID state documented with the specific form/list-endpoint gaps that block a live swap, both outside this story's write scope | `docs/features/bio-dev-003/README.md`'s "AC05: web surface state" |

## Developer tests

- `tests/features/bio-dev-003/DeviceAdapterRegistryTests.cs` — 5 tests (AC01: routing per vendor, case-insensitivity, unknown vendor).
- `tests/features/bio-dev-003/DeviceAdapterServiceTests.cs` — 7 tests (AC02: discovery/health/online/offline/degraded; AC03: gate-before-registry, typed exceptions).
- `tests/features/bio-dev-003/VendorSdkIsolationTests.cs` — 3 tests (AC04: reflection-based isolation proof).
- `tests/dotnet/Biolog.IntegrationTests/DeviceAdapterEndpointsTests.cs` — 6 tests against the real composed host: real device registration, real certification registration, real capability/health success, real "no record" and real "Unsupported" rejection paths, real adapter-not-available path, real tenant fail-closed and not-found paths.

Reproduce with:
```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj
dotnet test tests/features/bio-dev-003/Biolog.Features.DeviceAdapters.Tests.csproj
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj
bash tests/features/bio-dev-003/BIO-DEV-003.test.sh
bash scripts/check-module-project-references.sh
python3 scripts/check-traceability.py --all
bash scripts/secret-scan.sh --all
```
(exact commands in `metadata.json`'s `test_command`.)

## Results

- `dotnet build api/Biolog.Api/Biolog.Api.csproj`: PASS, 0 warnings, 0 errors — full host (all 37
  feature projects) builds clean with the new project wired in.
- `Biolog.Features.DeviceAdapters.Tests`: 16/16 PASS.
- `Biolog.IntegrationTests` (full suite, not just this story's new file): 134/134 PASS — 0
  pre-existing failures reintroduced.
- `Biolog.UnitTests` (full suite): 200/200 PASS.
- `BIO-DEV-003.test.sh`: 23/23 PASS.
- `check-module-project-references.sh`: PASS — the new feature project has no `ProjectReference`
  back to `Biolog.Api.csproj`.
- `check-traceability.py --all`: PASS (31 implemented stories) — BIO-DEV-003 does not add a
  `tests/foundation/BIO-DEV-003.test.sh` or `governance/stories/BIO-DEV-003.json`, matching the
  established precedent for every other `api/features/`-convention story with no mobile
  component (`BIO-BIO-*`, `BIO-CRD-002+`, `BIO-IDN-*`, `BIO-PHO-*`, `BIO-ACC-001`) — confirmed by
  inspecting `tests/foundation/` and `governance/stories/` before relying on this, not assumed
  from BIO-UX-014's own, narrower precedent note.
- `secret-scan.sh --all`: clean.

## A design decision worth recording: gate-before-registry, not gate-after-resolve

An earlier draft resolved the adapter first and asked the certification gate only before
actually invoking `DiscoverCapabilitiesAsync`/`CheckHealthAsync` on it. Reversed to
gate-then-resolve: AC03's own wording ("unsupported ... combinations surface certification
status") reads as a precondition on even ATTEMPTING the combination, not merely on its outcome,
and `DeviceAdapterServiceTests.AC03_the_certification_gate_is_checked_before_the_registry_is_even_consulted`
proves this with a registry double that would throw if `Resolve()` were ever reached for an
uncertified combination — the test only passes because the gate's own exception is what
propagates.

## Two "not usable" outcomes kept distinct, not merged

`UnsupportedDeviceCombinationException` (certification says no, or has no record at all —
fails closed on "never certified") and `DeviceAdapterNotAvailableException` (certified, but no
adapter implementation exists for this vendor — a genuine gap) map to different HTTP problem
types (`409`/`503`) with different titles, so a caller — and a future story extending adapter
coverage — can tell "not approved" apart from "not yet built" without parsing free text.

## Scope boundaries, documented rather than silently assumed

1. **No changes to `DeviceRegistry`/`DeviceCertification`** (BIO-DEV-001/002's own owned,
   already-accepted files) — read-only usage of `DeviceService.GetAsync` and
   `DeviceCertificationService.GetEffectiveAsync` only, via this project's own
   `DeviceDescriptor`/`ICertificationGate` seam (mirroring BIO-MOB-003's
   `IDeviceTenantResolutionGateway` precedent for the identical one-way-dependency problem).
2. **No changes to `web/features/devices/**`/`web/app/devices/**`** (BIO-UX-010's owned files) —
   AC05 resolved as a documented HYBRID state instead; see "AC05: web surface state" in
   `docs/features/bio-dev-003/README.md` for the specific, named reasons (form field gap,
   missing list endpoint) and what's flagged to god/Chinedu as a follow-on.
3. **Discovery only, not operations.** Capturing a fingerprint, matching a face, printing or
   encoding a card are the `BIO-BIO-*`/`BIO-CRD-*` epics' own scope — this framework reports
   what a combination declares it supports, never performs the operation itself.
