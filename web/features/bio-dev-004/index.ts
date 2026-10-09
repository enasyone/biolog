import { resolvePort } from '../../lib/feature-mode';
import {
  getRemoteJobCapabilitiesMockPort,
  listDevicesMockPort,
  pollDeviceHealthMockPort,
  runRemoteJobMockPort,
} from './mock';
import {
  getRemoteJobCapabilitiesLivePort,
  listDevicesLivePort,
  pollDeviceHealthLivePort,
  runRemoteJobLivePort,
} from './live';

export type {
  DeviceOperationsSummary,
  GetRemoteJobCapabilitiesInput,
  GetRemoteJobCapabilitiesResult,
  ListDevicesResult,
  PollDeviceHealthInput,
  PollDeviceHealthResult,
  RemoteJobTypeId,
  RunRemoteJobInput,
  RunRemoteJobResult,
} from './ports';
export { KNOWN_REMOTE_JOB_TYPES } from './ports';

/**
 * BIO-DEV-004's resolved ports. Pages/components import these, never `./mock` or `./live`
 * directly (same discipline as every other module in this epic). Each is independently
 * mode-switchable via its own `NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE_<KEY>` override
 * (`web/lib/feature-mode.ts`'s own `PER_PORT_ENV_DEFAULTS`), falling back to the module-wide
 * `NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE` default.
 */
export const listDevicesPort = resolvePort(listDevicesMockPort, listDevicesLivePort, 'listDevices');
export const pollDeviceHealthPort = resolvePort(pollDeviceHealthMockPort, pollDeviceHealthLivePort, 'pollDeviceHealth');
export const getRemoteJobCapabilitiesPort = resolvePort(
  getRemoteJobCapabilitiesMockPort,
  getRemoteJobCapabilitiesLivePort,
  'getRemoteJobCapabilities',
);
export const runRemoteJobPort = resolvePort(runRemoteJobMockPort, runRemoteJobLivePort, 'runRemoteJob');
