# BIO-DEV-004 — Implement device health and configuration jobs

**Owner:** Emeka (Platform & Infrastructure Engineer)
**Wave:** W12 / W12.5, conflict group `device-health`
**Dependency:** BIO-DEV-003 (vendor adapter framework — capability discovery, on-demand health
check), which itself depends on BIO-DEV-001 (device registry) and BIO-DEV-002 (certification
matrix).

## User story

As device operations, I need health polling/heartbeat and controlled configuration jobs where
vendor APIs permit.

## Acceptance criteria

- **AC01** — Last-seen/status/health are updated.
- **AC02** — Remote jobs are capability-gated.
- **AC03** — Firmware/config changes require audit.
- **AC04** — Unsupported remote actions fail explicitly.
- **AC05** — The approved web surface (`/devices`) is connected through `DeviceService` to this
  story's live implementation, and the mapped end-to-end human workflow passes without an
  unapproved UI redesign.

## Architecture

### Why `Device.cs` (BIO-DEV-001) was extended directly, not duplicated into a side-store

`Features.DeviceRegistry.Device` already carried a reserved, never-populated `LastSeenAt` field
since BIO-DEV-001 — the natural signal that a future story (this one) was meant to populate it
for real, not build a second, parallel "last seen" concept the real `/devices` list would never
see without an extra join. This story adds `Health`/`HealthDetail`/`HealthCheckedAt` alongside it
and a new `DeviceService.RecordHeartbeatAsync`/`IDeviceRepository.ListAsync`, mirroring
`ChangeStatusAsync`'s own audit-before-commit, per-device-locked, `CancellationToken.None`-after-
the-audit-write discipline exactly (see `DeviceService.cs`'s own doc comments) — the identical
false-success/concurrent-writer/cancellation hazards `ChangeStatusAsync` already closed apply
identically to a heartbeat write, so the identical fix shape is reused rather than re-derived.

This is a narrow, additive extension of BIO-DEV-001's own host-level code (never a separate
feature project itself — it predates that convention, same as BIO-DEV-002/003), made by the same
owner (Emeka) who built BIO-DEV-001/002/003, for the exact field that story's own code already
disclosed was reserved for a later story. `DeviceHealthStatus` is reused directly from
BIO-DEV-003's own project rather than invented a second time — `Biolog.Api.csproj` already has a
`ProjectReference` to it (`BioDev003Module` already uses it), so no new project boundary is
crossed.

### Project isolation for the new domain (`api/features/bio-dev-004/`)

`Biolog.Features.DeviceOperations` follows the exact same isolation discipline BIO-DEV-003's own
csproj established for the identical problem shape: no `ProjectReference` to `Biolog.Api.csproj`,
and none to BIO-DEV-003's own project either, even though both are in-host types this project
could reference directly. Every cross-feature read/write goes through this project's own narrow
gateway interfaces (`Gateways/**`), bridged by `api/Biolog.Api/Modules/BioDev004Module.cs` — the
one place that can see `DeviceService` (BIO-DEV-001), `DeviceCertificationService` (BIO-DEV-002)
and `DeviceAdapterService`/`IDeviceAdapterRegistry` (BIO-DEV-003) all at once.

### RBAC/actor-provenance gateway (SY-DEV004-01, Seyi round 1, Critical — added, not omitted)

Round 1 deliberately omitted an `IPermissionGateway`/`ActorResolver`/`ActorProvenanceGate` gate
(the pattern BIO-TRN-003/BIO-INT-004/BIO-ATT-003 each duplicate into their own feature project),
reasoning that this story's direct dependency chain (BIO-DEV-001/002/003) has none either.
Seyi's review correctly rejected that reasoning: "following BIO-DEV-001/002/003 is not
sufficient justification... their documented missing authorization is inherited debt, not an
authorization grant, and BIO-DEV-004 materially expands the surface from registry/diagnostic
operations to remote reboot/configuration/firmware control."

Every route under `/api/v1/devices` this story owns (`GET /`, `POST /{id}/poll-health`,
`GET /{id}/remote-job-capabilities`, `POST /{id}/remote-jobs`) now requires a resolved,
provenance-verified actor (`Api/ActorResolver.cs`/`Api/ActorProvenanceGate.cs`, ADR-0006
Decision 3 — ported verbatim from BIO-TRN-003's own identical gate) who holds one of two
least-privilege permissions, checked via BIO-IDN-006's own `PermissionService.ActorHasPermissionAsync`:

- `DeviceHealthPollingService.ReadPermission` (`device_operations.read`) — list devices, poll
  health, read remote-job capabilities. The "device read/health polling" half of Seyi's required
  split.
- `DeviceRemoteJobService.ExecutePermission` (`device_operations.execute`) — actually run a
  remote job. Strictly higher-privilege: an actor who can see what a device supports cannot
  necessarily run anything against it.

The audit actor (AC03) is now derived ONLY from the verified `X-Actor-Id`/`X-Actor-Provenance`
headers — the request body no longer carries `ActorType`/`ActorId` fields at all, closing the
"forgeable audit actor" half of the same finding (Seyi's own proof: "the integration test's
successful remote-job path demonstrates this exact unauthenticated contract: it sends only
tenant test context plus `{ ActorType, ActorId }` and receives 200").

### AC02 — "where vendor APIs permit": the remote-job capability table

`RemoteJobType` (`Reboot`/`PushConfiguration`/`UpdateFirmware`) is this story's own closed
vocabulary — no shared schema registers remote device job types yet, the same reasoning
BIO-DEV-003's own `DeviceCategory`/`DeviceOperation` already carries. None of BIO-DEV-003's four
simulated vendor SDKs expose a remote-admin call at all (`DiscoverCapabilitiesAsync`/
`CheckHealthAsync` only), so there is no vendor-SDK-derived capability signal to read. Instead,
`BioDev004Module.SupportedJobsByCategory` is a deliberately explicit, hand-maintained table keyed
by the device's resolved `DeviceCategory` (which a real vendor's SDK would in practice determine
per device line):

| Category | Supported jobs |
|---|---|
| FingerprintScanner | Reboot, UpdateFirmware |
| FaceTerminal | Reboot, UpdateFirmware, PushConfiguration |
| NfcUhfReader | Reboot |
| CardPrinter | Reboot, PushConfiguration |

A job is gated in this order: certification status (BIO-DEV-002, same fail-closed-on-`Unsupported`
-or-no-record rule BIO-DEV-003's own `ICertificationGate` already established) → adapter
resolution (BIO-DEV-003) → this table. Each gate failure is its own distinct, explicit exception/
HTTP problem (`device_combination_unsupported` / `device_adapter_unavailable` /
`remote_job_not_supported`) — AC04 is never satisfied by a generic failure.

### AC03 — audit-before-execution, reusing the real shared audit sink

`DeviceRemoteJobService.RunJobAsync` records a `device.remote_job_executed` audit event through
`IRemoteJobAuditGateway` (implemented by `BioDev004Module` against the real, shared
`Biolog.Api.Audit.IAuditSink` singleton — the SAME sink `DeviceService`'s own status-change audit
already writes to, not a second parallel store) **before** calling `IRemoteJobExecutor.ExecuteAsync`
— the identical "nothing is visible to anyone until its own audit event already durably exists"
discipline `DeviceService.ChangeStatusAsync` established for status changes, applied here to a
remote job. `CancellationToken.None` is used for the execution step for the same point-of-no-
return reason.

### What "executing a job" actually does

`IRemoteJobExecutor`'s default implementation (`SimulatedRemoteJobExecutor`) is a deterministic,
always-succeeds simulation. No vendor SDK anywhere in this codebase exposes a real
reboot/push-config/update-firmware call — this story's own point is the gating/audit discipline
around a remote job, not simulating real vendor command failures, the same disclosed-scope-
boundary judgment BIO-TRN-003 applied to external parent delivery. A `RemoteJobOutcome.Failed`
path exists in the domain (and is exercised by a dedicated test double) so a real future vendor
integration has somewhere to report a genuine failure without any service-layer change.

### What "polling" means here

A health poll (and the device list) is triggered on demand via an HTTP call, not run by a real
background worker — the same "triggered, not a true scheduled job" shape every other synthetic/
demo operational action in this codebase uses (e.g. BIO-TRN-003's own dispatch-on-demand panel
action). The web panel's "Poll health" button is the trigger; there is no cron-equivalent sweep
across every device in this story (out of scope — AC01 only requires that a poll updates
last-seen/status/health, not that it happens unprompted).

## AC05 — web wiring

A new "Device operations" panel (`web/features/bio-dev-004/DeviceOperationsPanel.tsx`) is mounted
additively onto `/devices` (`devices-page.tsx`), alongside the existing "Register device" and
"Connectivity status" sections — neither touched nor redesigned. `DeviceService` gains four more
port keys (`listDevices`/`pollDeviceHealth`/`getRemoteJobCapabilities`/`runRemoteJob`), independent
of the pre-existing `primaryAction`/`viewConnectivityStatus` keys, the same "new keys, new panel,
existing panel untouched" precedent BIO-TRN-003/BIO-INT-004 already established.

Proven two ways:
- SIMULATION-mode Playwright (`tests/frontend/playwright/bio-dev-004-device-operations.spec.ts`,
  6 tests): list → poll health → see last-seen/health update; a job type the device's own
  category doesn't support is disabled at the `<option>` level (never even selectable, let alone
  a dead "Run" button); a supported job runs and visibly succeeds; an uncertified device's poll
  fails explicitly with a genuinely distinct, accessible (`role="alert"`) state.
- A real `.NET` integration test (`tests/dotnet/Biolog.IntegrationTests/BioDev004EndpointsTests.cs`)
  against the real composed host: real device registration → real certification → real poll
  (persists health/last-seen through `DeviceService`) → real remote job (persists a real audit
  event through the real `IAuditSink`) → real rejection paths (uncertified combination,
  unsupported job type, cross-tenant isolation), reusing the same header-switched tenant test
  seam `DeviceRegistryTests`/`DeviceAdapterEndpointsTests` already established.

## Verification

`dotnet build` clean. 279/279 integration (265 original + 9 round-1 + 5 new SY-DEV004-01
authorization-boundary tests, no regression). 210/210 unit, no regression. 16/16 feature-unit
tests (13 round-1 + 3 new permission-gate tests). Module-reference/secret-scan/traceability all
PASS. `npx tsc --noEmit` clean. `npx vitest run` 550/550 — round 1 closed two pre-existing
stale-fixture failures found while adding `DeviceService`'s own new port keys:
`bio-ux-010-module-ports.test.ts` (updated for this story's own 4 new keys, the established
convention) and `bio-ux-005-feature-mode.test.ts`'s own hardcoded `MODULE_PORT_KEYS` literal for
**both** `DeviceService` (this story) **and** `TransportService` (a pre-existing gap left by
BIO-TRN-003's own merge, never introduced by this story). `npx playwright test
bio-dev-004-device-operations`: 6/6 passed (SIMULATION mode, unaffected by the round-2 backend
authorization change since the mock ports never called the real HTTP endpoints).

**Round 2 (SY-DEV004-01 fix) new tests**, all against the real composed host
(`BioDev004EndpointsTests.cs`): missing `X-Actor-Id` → 400 `actor_required`; missing
`X-Actor-Provenance` → 401 `actor_provenance_required`; a provenance proof computed for a
DIFFERENT actor id than the one asserted (the exact forged-attribution shape the finding named)
→ 401; an actor with no `ReadPermission` → 403 `permission_required` on both list and poll (not a
silently-empty/filtered result); an actor with `ReadPermission` but not `ExecutePermission` → can
read capabilities but is refused 403 running a job, with no audit event recorded. Every
pre-existing functional test now runs through an authorized caller
(`AuthorizedClientForTenant`) with a real resolved actor + valid provenance proof against a
test-controllable permission gateway granted by default — the same shape BIO-ATT-003's own
`WorkforceAttendanceEndpointsIntegrationTests` uses, chosen over seeding real BIO-IDN-006 RBAC
grants because this class's own per-test unique tenant ids (needed to prove certification/
cross-tenant isolation) would otherwise require a real grant seeded per tenant, re-proving
BIO-IDN-006's own `PermissionService` rather than this story's own gating/wiring — already
proven end-to-end once, for a different story, by `BioTrn003EndpointsTests`.

## Scope boundaries disclosed, not silently assumed

- No real background polling/scheduler exists in this codebase; health polling is triggered, not
  scheduled (see "What 'polling' means here" above).
- No vendor SDK anywhere exposes a real remote-admin call; job execution is a deterministic
  simulation (see "What 'executing a job' actually does" above).
- SY-DEV004-01 (Seyi, round 1): a device registered under BIO-DEV-001 via `POST /api/v1/devices`
  and the two read-only BIO-DEV-003 routes (`GET /{id}/capabilities`, `GET /{id}/health`) remain
  ungated, per those stories' own disclosed "inherited debt" scope — this story does not expand
  into fixing them, only adds the gate to its own four routes (see "RBAC/actor-provenance
  gateway" above).
