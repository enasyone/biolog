import { createMockAdapter } from '../../lib/module-port';
import type { TransportPort } from './ports';

/**
 * Transport mock adapter (BIO-UX-005-AC05): deterministically cycles success,
 * failure and offline outcomes so every state is reproducible for local
 * testing and demos, not just the happy path.
 */
export const transportMockPort: TransportPort = createMockAdapter('TransportService', {
  scenarios: [
    { outcome: 'success', message: 'Transport: boarding/alighting log loaded.' },
    { outcome: 'failure', message: 'Transport: log failed to load — try again.' },
    { outcome: 'offline', message: 'Transport: device or network appears offline (simulated).' },
  ],
});

export const transportAssignRouteMockPort: TransportPort = createMockAdapter('TransportService', {
  scenarios: [
    { outcome: 'success', message: 'Transport: route assigned.' },
    { outcome: 'failure', message: 'Transport: route could not be assigned — try again.' },
    { outcome: 'offline', message: 'Transport: device or network appears offline (simulated).' },
  ],
});
