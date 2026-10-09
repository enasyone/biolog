# BIO-UX-010-DEVICE-FORM-FOLLOWUP — device registration form redesign + live wiring

Follow-up flagged by Emeka off BIO-DEV-003 (`docs/features/bio-dev-003/README.md`'s AC05 web
surface state note, 2026-09-24): `/devices`' "Register device" form collected only a device name
via an unwired `useState`, calling `devicesPort.primaryAction()` with no payload at all — but the
real `DeviceRegistrationRequest` (`api/Biolog.Api/Features/DeviceRegistry/
DeviceRegistrationRequest.cs`) needs `Serial`/`Vendor`/`Model`/`Firmware`/`Sdk`/`Site` (required) +
`Network{IpAddress,MacAddress,Hostname}` (optional) + `CredentialReference` (optional). The real
`POST /api/v1/devices` endpoint already existed and worked
(`api/Biolog.Api/Modules/DeviceRegistryModule.cs:57`) but nothing on the web side could reach it
with real data.

**Recorded UI-first approval**: proposed the field list/layout/wiring plan to god/human before
touching any code (hive outbox `bio-ux-010-device-form-followup-proposal.json`, 2026-09-27);
approved as-is the same day, field-for-field verified against the backend contract before
approval.

## Form fields added (`web/features/devices/devices-page.tsx`)

Reuses `.biolog-op-form`/`.biolog-op-field` — the same pattern already on this page and on Access
Control/Enrolment — no new CSS. Serial/Model/Firmware/SDK are required text inputs; Vendor and Site
are required `<select>`s; Network (IP address/MAC address/Hostname) is grouped under a bare
`<fieldset>`/`<legend>` as three optional text inputs; Credential reference is one optional text
input. All required fields ship with plausible non-empty defaults (`DEV-0001`/`FS-100`/`1.0.0`),
the same convention `access-page.tsx`'s pre-filled "Main Gate" Zone already established — keeps the
form submittable as-is and avoids breaking `bio-ux-010-operational-journeys.spec.ts`'s
"no visible primary action is dead" test, which clicks Register device with no manual data entry.

**Vendor options come from BIO-DEV-003's actual 4 certified adapters** (`Acme`, `NovaVision`,
`OrbitID`, `PrintWorks` — exact casing from
`api/features/bio-dev-003/Biolog.Features.DeviceAdapters/Adapters/*.cs`'s vendor-match strings,
including `OrbitID`'s capital D), not invented brand names — a registered device's vendor now lines
up with real certification/adapter behaviour. Site has no existing concept anywhere in the app to
reuse, so a small synthetic constant list was added (`DEVICE_SITES`:
HQ/Warehouse A/Remote Office/Distribution Center). Both new types/constants live in
`web/lib/synthetic-data/types.ts` next to `SyntheticDevice`, barrel-exported automatically.

## Port wiring (`web/features/devices/ports.ts`, `mock.ts`, `live.ts`, `index.ts`)

`module-port.ts`'s own header comment explicitly sanctions this: "a domain story extends its own
`ports.ts` interface with real methods without touching this file or breaking any other module."
Added a new `DeviceRegistrationPort extends ModulePort` interface (scoped separately from the
plain `DevicesPort` still used by `viewConnectivityStatus`, which has nothing to do with
registration) with a typed `registerDevice(payload: DeviceRegistrationPayload): Promise<PrimaryActionResult>`
method:

- **Mock** (`devicesMockPort`): delegates to the existing deterministic `primaryAction()` scenario
  cycle — the payload doesn't affect the outcome, same as every other mock adapter in this
  codebase; the scenario script alone decides success/failure/offline.
- **Live** (`devicesLivePort`): genuinely POSTs the payload (camelCase field names, matching this
  codebase's existing convention for real request bodies — see `features/identity/live.ts`'s
  `firstName`/`lastName`) to `/api/v1/devices`, the real endpoint BIO-DEV-003 already built. Not a
  stub — this is a genuinely-wired LIVE path, gated behind `NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE`
  same as every other module (defaults to SIMULATION).

`viewConnectivityStatus`'s port and mode resolution are completely untouched — `DeviceService`'s
`MODULE_PORT_KEYS` entry didn't need a new key since `registerDevice` isn't a new *port*, it's a
new *method* on the existing `primaryAction`-keyed port pair, resolved exactly as before.

"View connectivity status" itself is deliberately left as-is: BIO-DEV-003's own doc already flags
that it needs a new tenant-scoped device-list endpoint that doesn't exist yet on
`DeviceRegistryModule` — not something this follow-up is scoped to add.

## Manual verification

Ran a throwaway dev server and drove the form directly (not just Playwright): default values
submit cleanly, the mock's deterministic per-instance cycle (offline → success → failure → offline
on repeated clicks) behaves identically to every other operational page, and a successful
registration appends `"Acme FS-100 (DEV-0001) registered (SYNTHETIC)."` to the history list.
Screenshotted the rendered form — matches the app's visual language, no ad-hoc CSS needed.

## Verification

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — 34 routes, all prerendered
corepack pnpm --dir tests/frontend test:unit   # 413/413 pass, unchanged
corepack pnpm --dir tests/frontend test:e2e    # 413/413 pass, 10 skipped, zero regressions
                                                # (includes /devices' axe-accessibility and
                                                # operational-journeys checks)
corepack pnpm --dir tests/frontend test:visual # 14/14 pass, byte-identical — /devices is not
                                                # one of BIO-UX-014's 14 baselined pages and no
                                                # shared CSS was touched, so no regen needed
```
