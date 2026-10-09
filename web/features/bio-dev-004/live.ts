import type {
  DeviceOperationsSummary,
  GetRemoteJobCapabilitiesPort,
  ListDevicesPort,
  PollDeviceHealthPort,
  RemoteJobTypeId,
  RunRemoteJobPort,
} from './ports';

/**
 * Live adapters for BIO-DEV-004's real `/api/v1/devices/*` backend (`BioDev004Module.cs`) -
 * genuinely connected, not a stub. No tenant header is sent; this host resolves tenancy from
 * the request's own Host channel (ADR-0002 Decision 1.3, `TenantContextMiddleware`), the same
 * convention every other live adapter in this epic already follows.
 *
 * SY-DEV004-01 (Seyi, round 1, Critical), fixed: the real backend now requires a resolved,
 * provenance-verified actor who holds this story's own RBAC permission on every route (list,
 * poll, capabilities, run job) - this adapter's own static, unauthenticated `ACTOR_HEADERS` is
 * the same disclosed boundary BIO-TRN-003's/BIO-INT-004's own live adapters already established
 * (see `docs/features/bio-dev-004/README.md` "AC05 - web wiring" for why: the default browser
 * Playwright suite proves the mapped workflow against SIMULATION mode only, while this story's
 * real HTTP endpoints - RBAC/provenance enforcement included - are proven directly against the
 * real backend by `tests/dotnet/Biolog.IntegrationTests/BioDev004EndpointsTests.cs`). The actor
 * is NEVER carried in the request body any more (that was the forgeable-audit-actor half of
 * SY-DEV004-01) - only in these headers, which `web/proxy.ts` strips and replaces with a
 * verified identity for any real signed-session browser request (see
 * `web/lib/actor-provenance-routes.ts`'s own new `DeviceOperations` entry).
 */
const ACTOR_HEADERS = { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' } as const;

interface DeviceWire {
  id: string;
  vendor: string;
  model: string;
  status: string;
  health: DeviceOperationsSummary['health'];
  healthDetail: string | null;
  lastSeenAt: string | null;
  version: number;
}

function summaryFromWire(wire: DeviceWire): DeviceOperationsSummary {
  return {
    id: wire.id,
    vendor: wire.vendor,
    model: wire.model,
    status: wire.status,
    health: wire.health,
    healthDetail: wire.healthDetail,
    lastSeenAt: wire.lastSeenAt,
    version: wire.version,
  };
}

async function problemMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as { title?: string; detail?: string };
    return body.detail ?? body.title ?? fallback;
  } catch {
    return fallback;
  }
}

export const listDevicesLivePort: ListDevicesPort = {
  module: 'DeviceService',
  mode: 'live',
  async primaryAction() {
    try {
      const response = await fetch('/api/v1/devices', { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not load devices: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as DeviceWire[];
      return { outcome: 'success', message: 'Devices loaded.', devices: wire.map(summaryFromWire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Devices service - device or network appears offline.' };
    }
  },
};

export const pollDeviceHealthLivePort: PollDeviceHealthPort = {
  module: 'DeviceService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No device was provided.' };
    try {
      const response = await fetch(`/api/v1/devices/${input.deviceId}/poll-health`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ correlationId: null }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not poll device health: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as DeviceWire;
      return { outcome: 'success', message: `Health polled: ${wire.health ?? 'Unknown'}.`, device: summaryFromWire(wire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Devices service - device or network appears offline.' };
    }
  },
};

export const getRemoteJobCapabilitiesLivePort: GetRemoteJobCapabilitiesPort = {
  module: 'DeviceService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No device was provided.' };
    try {
      const response = await fetch(`/api/v1/devices/${input.deviceId}/remote-job-capabilities`, { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not load remote job capabilities: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const supportedJobTypes = (await response.json()) as RemoteJobTypeId[];
      return { outcome: 'success', message: 'Remote job capabilities loaded.', supportedJobTypes };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Devices service - device or network appears offline.' };
    }
  },
};

export const runRemoteJobLivePort: RunRemoteJobPort = {
  module: 'DeviceService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No job was provided.' };
    try {
      const response = await fetch(`/api/v1/devices/${input.deviceId}/remote-jobs`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ jobType: input.jobType, correlationId: null }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not run remote job: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as { outcome: 'Succeeded' | 'Failed'; detail?: string | null };
      return {
        outcome: 'success',
        message: body.outcome === 'Succeeded' ? `${input.jobType} completed.` : `${input.jobType} failed: ${body.detail ?? 'no detail'}.`,
        jobOutcome: body.outcome,
      };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Devices service - device or network appears offline.' };
    }
  },
};
