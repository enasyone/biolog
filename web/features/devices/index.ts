import { resolvePort } from '../../lib/feature-mode';
import { devicesMockPort, devicesViewConnectivityMockPort } from './mock';
import { devicesLivePort, devicesViewConnectivityLivePort } from './live';
import type { DeviceRegistrationPort, DevicesPort } from './ports';

export type { DeviceRegistrationPayload, DeviceNetworkMetadata, DeviceRegistrationPort, DevicesPort } from './ports';

/**
 * The Devices module's two resolved ports (BIO-UX-010-AC01, AC05). Pages/
 * components import these, never `./mock` or `./live` directly — switching
 * `NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE` from SIMULATION to LIVE changes what
 * these resolve to without any importer changing.
 */
export const devicesPort: DeviceRegistrationPort = resolvePort(devicesMockPort, devicesLivePort, 'primaryAction');

export const devicesViewConnectivityPort: DevicesPort = resolvePort(
  devicesViewConnectivityMockPort,
  devicesViewConnectivityLivePort,
  'viewConnectivityStatus',
);
