import { createMockAdapter } from '../../lib/module-port';
import type { DeviceRegistrationPort, DevicesPort } from './ports';

/**
 * Devices mock adapter (BIO-UX-005-AC05): deterministically cycles success,
 * failure and offline outcomes so every state is reproducible for local
 * testing and demos, not just the happy path. `registerDevice`'s payload is
 * accepted for shape-parity with the live adapter but does not affect the
 * outcome — same as every other mock adapter in this codebase, the scenario
 * script alone decides success/failure/offline, not the input.
 */
export const devicesMockPort: DeviceRegistrationPort = {
  ...createMockAdapter('DeviceService', {
    scenarios: [
      { outcome: 'success', message: 'Devices: device registered.' },
      { outcome: 'failure', message: 'Devices: registration failed — try again.' },
      { outcome: 'offline', message: 'Devices: device or network appears offline (simulated).' },
    ],
  }),
  registerDevice(_payload) {
    return this.primaryAction();
  },
};

export const devicesViewConnectivityMockPort: DevicesPort = createMockAdapter('DeviceService', {
  scenarios: [
    { outcome: 'success', message: 'Devices: connectivity status loaded.' },
    { outcome: 'failure', message: 'Devices: connectivity status failed to load — try again.' },
    { outcome: 'offline', message: 'Devices: device or network appears offline (simulated).' },
  ],
});
