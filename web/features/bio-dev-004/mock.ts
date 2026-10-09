import { createMockAdapter } from '../../lib/module-port';
import type {
  DeviceOperationsSummary,
  GetRemoteJobCapabilitiesPort,
  ListDevicesPort,
  PollDeviceHealthPort,
  RemoteJobTypeId,
  RunRemoteJobPort,
} from './ports';

/**
 * Deterministic in-memory stand-in for the real `/api/v1/devices/*` BIO-DEV-004 endpoints -
 * reproducible for local testing and demos, same shape as every other mock adapter in this
 * epic. A fixed synthetic device roster stands in for the real tenant device list; polling
 * updates that roster's own `health`/`lastSeenAt` in place, so a second "View devices" after
 * "Poll health" shows the update, exactly like the real backend.
 *
 * `mock-dev-nfc-1` always reports `Offline` health (AC04's own "unsupported remote actions
 * fail explicitly" is instead proven by `mock-dev-uncertified-1`, which always fails the poll
 * outright) - and its own job-type table (NfcUhfReader -> Reboot only) is a deliberate,
 * disclosed deterministic fixture standing in for a device category with narrower vendor
 * support, not an arbitrary restriction (mirrors `BioDev004Module.SupportedJobsByCategory`).
 */
const JOB_TYPES_BY_DEVICE: Record<string, readonly RemoteJobTypeId[]> = {
  'mock-dev-fp-1': ['Reboot', 'UpdateFirmware'],
  'mock-dev-face-1': ['Reboot', 'UpdateFirmware', 'PushConfiguration'],
  'mock-dev-nfc-1': ['Reboot'],
};

const devices = new Map<string, DeviceOperationsSummary>([
  ['mock-dev-fp-1', { id: 'mock-dev-fp-1', vendor: 'Acme', model: 'FS-100', status: 'Active', health: 'Online', healthDetail: null, lastSeenAt: null, version: 1 }],
  ['mock-dev-face-1', { id: 'mock-dev-face-1', vendor: 'NovaVision', model: 'FT-300', status: 'Active', health: 'Degraded', healthDetail: 'elevated latency (SIMULATION)', lastSeenAt: null, version: 1 }],
  ['mock-dev-nfc-1', { id: 'mock-dev-nfc-1', vendor: 'OrbitId', model: 'RD-200', status: 'Active', health: 'Offline', healthDetail: 'not reachable (SIMULATION)', lastSeenAt: null, version: 1 }],
  ['mock-dev-uncertified-1', { id: 'mock-dev-uncertified-1', vendor: 'Acme', model: 'FS-LEGACY', status: 'Active', health: null, healthDetail: null, lastSeenAt: null, version: 1 }],
]);

const listBase = createMockAdapter('DeviceService');
export const listDevicesMockPort: ListDevicesPort = {
  ...listBase,
  async primaryAction() {
    return { outcome: 'success', message: 'Devices loaded (SIMULATION).', devices: [...devices.values()] };
  },
};

const pollBase = createMockAdapter('DeviceService');
export const pollDeviceHealthMockPort: PollDeviceHealthPort = {
  ...pollBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No device was provided.' };
    if (input.deviceId === 'mock-dev-uncertified-1') {
      // AC04: an uncertified combination fails explicitly, never a fake reading.
      return { outcome: 'failure', message: 'Devices: this vendor/model/firmware/SDK combination is not certified (SIMULATION).' };
    }
    const current = devices.get(input.deviceId);
    if (!current) return { outcome: 'failure', message: 'Unknown device (SIMULATION).' };
    const updated: DeviceOperationsSummary = {
      ...current,
      lastSeenAt: new Date().toISOString(),
      version: current.version + 1,
    };
    devices.set(input.deviceId, updated);
    return { outcome: 'success', message: `Health polled: ${updated.health ?? 'Unknown'} (SIMULATION).`, device: updated };
  },
};

const capabilitiesBase = createMockAdapter('DeviceService');
export const getRemoteJobCapabilitiesMockPort: GetRemoteJobCapabilitiesPort = {
  ...capabilitiesBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No device was provided.' };
    if (input.deviceId === 'mock-dev-uncertified-1') {
      return { outcome: 'failure', message: 'Devices: this vendor/model/firmware/SDK combination is not certified (SIMULATION).' };
    }
    return {
      outcome: 'success',
      message: 'Remote job capabilities loaded (SIMULATION).',
      supportedJobTypes: JOB_TYPES_BY_DEVICE[input.deviceId] ?? [],
    };
  },
};

const runJobBase = createMockAdapter('DeviceService');
export const runRemoteJobMockPort: RunRemoteJobPort = {
  ...runJobBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No job was provided.' };
    const supported = JOB_TYPES_BY_DEVICE[input.deviceId] ?? [];
    if (!supported.includes(input.jobType)) {
      // AC02/AC04: capability-gated, fails explicitly rather than silently no-op'ing.
      return { outcome: 'failure', message: `Devices: '${input.jobType}' is not supported by this device (SIMULATION).` };
    }
    return { outcome: 'success', message: `${input.jobType} completed (SIMULATION).`, jobOutcome: 'Succeeded' };
  },
};
