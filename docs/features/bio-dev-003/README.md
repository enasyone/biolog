# BIO-DEV-003 — Vendor adapter framework for devices

As BIOLOG, we need adapter boundaries for fingerprint scanners, face terminals, NFC/UHF
readers and card printers, so that vendor SDK code never leaks into core device or identity
logic, and an unsupported firmware/SDK combination is refused with a clear reason instead of
attempted blind.

**Feature project:** `api/features/bio-dev-003/Biolog.Features.DeviceAdapters` (namespace
`Biolog.Features.DeviceAdapters`), following the same separate-project, one-way-dependency
convention `Biolog.Features.OfflineSync` (BIO-MOB-003) and `Biolog.Features.Biometrics`
(BIO-BIO-001) already established — never `api/Biolog.Api/Features/DeviceAdapters`, which
would make it part of the host project directly (the older shape `DeviceRegistry`/
`DeviceCertification` predate this convention with). Registered into the host via
`api/Biolog.Api/Modules/BioDev003Module.cs`.

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — vendor SDK code is isolated behind typed interfaces | `Domain/IDeviceAdapter.cs`: one typed interface, four concrete adapters (`Adapters/*.cs`) — one per device kind the user story names. Each adapter is the ONLY code that ever constructs its vendor's simulator (`VendorSdks/*.cs`, all `internal`) | `DeviceAdapterRegistryTests.cs`, `BIO-DEV-003.test.sh` |
| AC02 — device capability discovery and health are supported | `Domain/DeviceCapabilities.cs`/`DeviceHealth.cs`, `DeviceAdapterService.DiscoverCapabilitiesAsync`/`CheckHealthAsync`, `GET /api/v1/devices/{id}/capabilities` and `GET /api/v1/devices/{id}/health` | `DeviceAdapterServiceTests.cs`, `DeviceAdapterEndpointsTests.cs` |
| AC03 — unsupported firmware/SDK combinations surface certification status | `ICertificationGate` is checked BEFORE the adapter registry is even consulted (`DeviceAdapterService.GateThenInvokeAsync`); `BioDev003Module.CertificationGateAdapter` bridges the real `DeviceCertificationService`; an unsupported/uncertified combination throws `UnsupportedDeviceCombinationException`, mapped to `409 device_combination_unsupported` with a `certification_status` field | `DeviceAdapterServiceTests.cs` (gate-before-registry proof), `DeviceAdapterEndpointsTests.cs` (real host, real certification matrix) |
| AC04 — no vendor SDK type leaks into domain contracts | See "No vendor SDK type leaks" below | `VendorSdkIsolationTests.cs`, `BIO-DEV-003.test.sh` |
| AC05 — `/devices` connected to this story's live implementation or a documented HYBRID state | See "AC05: web surface state" below | `BIO-DEV-003.test.sh` |

## Why a separate feature project, not inside `Biolog.Api.Features.DeviceRegistry`

`DeviceRegistry` (BIO-DEV-001) and `DeviceCertification` (BIO-DEV-002) both predate the
separate-project convention and live inline inside `Biolog.Api` itself. Every story since
(`BIO-BIO-*`, `BIO-CRD-*`, `BIO-IDN-*`, `BIO-PHO-*`, `BIO-MOB-003`, `BIO-DOC-009`, ...) uses a
dedicated project under `api/features/<key>/` with a thin `IBiologModule` adapter in
`api/Biolog.Api/Modules/`, enforced one-way by `scripts/check-module-project-references.sh`.
BIO-DEV-003's own user story — "adapter boundaries ... so vendor SDK code does not leak into
core logic" — is the closest match in this codebase to `BIO-BIO-001`'s own "provider-neutral
biometric contracts so vendor SDKs do not leak into core identity logic", so this story follows
that same shape rather than extending the older inline one.

Because the one-way rule means this project cannot reference `Biolog.Api.csproj` (and
`DeviceRegistry`/`DeviceCertification` live inside it, not in their own project), this project
never references either directly:

- **`Domain/DeviceDescriptor.cs`** is this project's own minimal copy of "which real device"
  (Vendor/Model/Firmware/Sdk/Serial) — field-for-field matching `Features.DeviceRegistry.Device`
  and `Features.DeviceCertification.DeviceCertificationKey`'s own field names, so
  `BioDev003Module` builds one from a real `Device` with a straight field copy.
- **`Gateways/ICertificationGate.cs`** is the narrow seam BIO-DEV-003 uses to ask "is this
  combination certified?" without referencing `DeviceCertificationService` — the same
  "gateway, not a direct reference" shape `BIO-MOB-003`'s own `IDeviceTenantResolutionGateway`
  established for an identical problem (a separate-project feature needing something from an
  inline Biolog.Api feature). `BioDev003Module.CertificationGateAdapter` (private, constructed
  via a manual factory lambda in `RegisterServices`, exactly like
  `BioMob003Module.DeviceTenantResolutionGatewayAdapter`) is the real bridge.

## No vendor SDK type leaks into domain contracts (AC04)

Four vendor simulator types live under `VendorSdks/` — `AcmeFingerprintSdkClient`,
`NovaVisionTerminalClient`, `OrbitReaderSdk`, `PrintWorksDriver` — each with a deliberately
vendor-specific shape (its own method/record names), because this environment has no real
vendor SDK dependency to integrate with (synthetic-data-only, no proprietary hardware): these
are deterministic, in-process simulators, the same "the framework is real, the hardware
integration itself is necessarily simulated" pattern `InMemoryDeviceRepository` already
established for storage. Health/capability results vary deterministically by a marker in the
device's `Serial` (`"OFFLINE"`/`"DEGRADED"`/`"NOLIVENESS"`), not randomly — reproducible for
tests and demos, matching this codebase's "SIMULATION mode must be deterministic" convention.

Every `VendorSdks/*` type is declared `internal`, and referenced only from its own single
adapter class in `Adapters/*.cs` — nowhere else in this project, and (via
`InternalsVisibleTo`) visible to `Biolog.Api`/the test project but still never exposed on any
of THEIR public members either, since neither one references a `VendorSdks` type at all. This
is enforced two ways, not just documented:

1. **The compiler.** A public method cannot declare an internal type as a parameter or return
   type without a build error ("Inconsistent accessibility") — the boundary cannot silently
   regress without breaking the build.
2. **`VendorSdkIsolationTests.cs`**, a reflection test that (a) confirms every `VendorSdks` type
   is non-public, and (b) walks every public method on every public type in the assembly and
   asserts none of them declares a `VendorSdks` type as a parameter or return type (unwrapping
   `Task<T>` first) — a machine-checked version of the same guarantee, so a future refactor that
   widened a vendor type's visibility would fail this test even before anything tried to expose
   it on a real public signature.

## AC03: the gate runs before the registry, not just before the vendor call

`DeviceAdapterService.GateThenInvokeAsync` calls `ICertificationGate.CheckAsync` first and
throws `UnsupportedDeviceCombinationException` immediately if it fails — `IDeviceAdapterRegistry.Resolve`
is never even called for an uncertified combination.
`DeviceAdapterServiceTests.AC03_the_certification_gate_is_checked_before_the_registry_is_even_consulted`
proves this directly with a registry double that throws if `Resolve()` is ever reached: the
test passes only because the gate's own exception is what propagates, never the double's.

Two "not usable" outcomes are kept structurally distinct, not merged into one generic failure:

- **`UnsupportedDeviceCombinationException`** — the certification matrix says `Unsupported`, or
  has no record at all for this Vendor/Model/Firmware/Sdk (`ICertificationGate` fails closed on
  "never certified" the same way every tenant-plane read/write in this codebase fails closed on
  a missing resolution, ADR-0002 Decision 2's discipline applied here to a device combination).
  Maps to `409 device_combination_unsupported` with `certification_status` (the exact matrix
  status word, or `"NoCertificationRecord"`) and `certification_detail` (the test evidence
  reference, when one exists).
- **`DeviceAdapterNotAvailableException`** — the combination IS certified, but no
  `IDeviceAdapter` implementation is registered for its vendor (a genuine implementation gap,
  distinct from an administratively-uncertified one). Maps to `503 device_adapter_unavailable`.

`Deprecated`/`Experimental`/`Supported`/`Certified` are all treated as supported by the gate —
only `Unsupported` (and no-record) is this AC's actual gate; a deprecated device still gets a
real capability/health answer, which is a deliberate reading of AC03's own wording
("unsupported ... surface certification status"), not an oversight.

## AC05: web surface state

`/devices` (BIO-UX-010, wired to `DeviceService` via `web/features/devices/`) is **left in its
current, unchanged HYBRID/SIMULATION state** by this story, for two reasons specific to this
story's actual write scope (`feature-path-ownership.json`: BIO-DEV-003 owns
`web/features/bio-dev-003/**`, not `web/features/devices/**`, which belongs to BIO-UX-010):

1. **"Register device"**'s form collects only a device name — `DeviceRegistrationRequest`
   requires Vendor/Model/Firmware/Sdk/Site/Network, fields the current approved UI does not
   expose. Wiring this to the real endpoint would need a form redesign, which neither this
   story nor its owned paths may do without Damilola's recorded approval (UI-FIRST constraint).
2. **"View connectivity status"** renders a tenant-wide device table sourced from
   `web/lib/synthetic-data.ts`'s client-side generated dataset, not a per-request fetch. Making
   it live would need a "list devices for this tenant" endpoint, which does not exist on
   `DeviceRegistryModule` today (`POST /`, `GET /{id}`, `PATCH /{id}/status` only) — adding one
   would mean extending BIO-DEV-001's own already-accepted HTTP contract, outside this story's
   declared write scope to do unilaterally.

Both existing buttons remain fully functional exactly as before (deterministic simulated
success/failure/offline outcomes) — no visible primary action is dead, and nothing on the
approved page changed, so BIO-UX-010's own passing end-to-end coverage is unaffected.

What this story DOES deliver, genuinely live and ready for a future story to adopt: real,
working `GET /api/v1/devices/{id}/capabilities` and `GET /api/v1/devices/{id}/health`
endpoints, backed by the real adapter framework and the real certification matrix — see
`DeviceAdapterEndpointsTests.cs` for full end-to-end proof against the real host. Connecting
`/devices` to them is flagged to god/Chinedu as a natural follow-on for whichever story adds a
per-device detail view (a UI change) and/or a tenant-scoped device list endpoint (a
BIO-DEV-001 extension) — both explicitly out of BIO-DEV-003's own scope and write permissions.

## Local development / verification

```sh
dotnet build api/features/bio-dev-003/Biolog.Features.DeviceAdapters.csproj
dotnet test tests/features/bio-dev-003/Biolog.Features.DeviceAdapters.Tests.csproj
dotnet build api/Biolog.Api/Biolog.Api.csproj
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj --filter DeviceAdapterEndpointsTests
bash tests/features/bio-dev-003/BIO-DEV-003.test.sh
```

## Scope boundaries

- No changes to `api/Biolog.Api/Features/DeviceRegistry/**` or
  `api/Biolog.Api/Features/DeviceCertification/**` (BIO-DEV-001/BIO-DEV-002's own owned,
  already-accepted files) — read-only usage of `DeviceService.GetAsync` and
  `DeviceCertificationService.GetEffectiveAsync` only.
- No changes to `web/features/devices/**` or `web/app/devices/**` (BIO-UX-010's owned files) —
  see "AC05: web surface state" above for why, and what is flagged as a follow-on instead.
- Capture/verify/match/print/encode *operations* themselves (not discovery) belong to the
  `BIO-BIO-*`/`BIO-CRD-*` epics, already shipped or in flight — this story reports what a
  combination declares it supports, never performs the operation.
