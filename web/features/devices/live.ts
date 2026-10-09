import { createLiveAdapter, type PrimaryActionResult } from '../../lib/module-port';
import type { DeviceRegistrationPayload, DeviceRegistrationPort, DevicesPort } from './ports';

/**
 * Devices live adapter (BIO-UX-005-AC04). `registerDevice` calls the real
 * `POST /api/v1/devices` endpoint BIO-DEV-003 already built and wired
 * (`DeviceRegistrationRequest`/`DeviceRegistryModule.cs`) — genuinely live,
 * not a stub, per BIO-UX-010-DEVICE-FORM-FOLLOWUP's recorded UI-first
 * approval (2026-09-27). `viewConnectivityStatus` is untouched and stays a
 * stub (see its own module's doc: a tenant-scoped device-list endpoint
 * doesn't exist yet, so nothing exists here to connect to).
 */
export const devicesLivePort: DeviceRegistrationPort = {
  ...createLiveAdapter('DeviceService', '/api/v1/devices/primary-action'),
  async registerDevice(payload: DeviceRegistrationPayload): Promise<PrimaryActionResult> {
    try {
      const response = await fetch('/api/v1/devices', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          serial: payload.serial,
          vendor: payload.vendor,
          model: payload.model,
          firmware: payload.firmware,
          sdk: payload.sdk,
          site: payload.site,
          network: {
            ipAddress: payload.network.ipAddress || null,
            macAddress: payload.network.macAddress || null,
            hostname: payload.network.hostname || null,
          },
          credentialReference: payload.credentialReference || null,
        }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Live request failed: HTTP ${response.status}` };
      }
      const device = (await response.json()) as { serial?: string };
      return { outcome: 'success', message: `Device ${device.serial ?? payload.serial} registered.` };
    } catch {
      return {
        outcome: 'offline',
        message: 'No live service reachable at /api/v1/devices yet — this module has not been connected.',
      };
    }
  },
};

export const devicesViewConnectivityLivePort: DevicesPort = createLiveAdapter(
  'DeviceService',
  '/api/v1/devices/view-connectivity-status',
);
