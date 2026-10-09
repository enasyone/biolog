import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';
import type { DeviceSite, DeviceVendor } from '../../lib/synthetic-data';

/**
 * Devices module ports (BIO-UX-010-AC01, AC02, AC05).
 *
 * `route-manifest.ts`'s `/devices` entry names two actions —
 * `primaryAction` ("Register device") and `viewConnectivityStatus` ("View
 * connectivity status") — following the same multi-port pattern
 * `features/reporting` established (BIO-UX-005-AC02). Both stay
 * `ModulePort`-shaped; the actual device list/connectivity state rendered on
 * `/devices` is page-level state sourced from `web/lib/synthetic-data`
 * (including the `online`/`offline`/`degraded` states BIO-UX-010-AC02 names).
 */
export type DevicesPort = ModulePort;

/** Mirrors `NetworkMetadata` (api/Biolog.Api/Features/DeviceRegistry/NetworkMetadata.cs) — all optional. */
export interface DeviceNetworkMetadata {
  ipAddress?: string;
  macAddress?: string;
  hostname?: string;
}

/**
 * Mirrors `DeviceRegistrationRequest`
 * (api/Biolog.Api/Features/DeviceRegistry/DeviceRegistrationRequest.cs)
 * field-for-field (BIO-UX-010-DEVICE-FORM-FOLLOWUP, recorded UI-first
 * approval 2026-09-27) — `Id`/`TenantId`/`Version` are server-assigned and
 * deliberately excluded here, exactly as that record's own doc comment says.
 */
export interface DeviceRegistrationPayload {
  serial: string;
  vendor: DeviceVendor;
  model: string;
  firmware: string;
  sdk: string;
  site: DeviceSite;
  network: DeviceNetworkMetadata;
  credentialReference?: string;
}

/**
 * `DevicesPort` extended with a real typed `registerDevice` method, per
 * `module-port.ts`'s own documented extension pattern ("a domain story
 * extends its own `ports.ts` interface with real methods without touching
 * this file or breaking any other module"). Scoped to its own interface
 * rather than added to `DevicesPort` itself so `viewConnectivityStatus`'s
 * port (which has nothing to do with registration) is unaffected.
 */
export interface DeviceRegistrationPort extends ModulePort {
  registerDevice(payload: DeviceRegistrationPayload): Promise<PrimaryActionResult>;
}
