import { createMockAdapter } from '../../lib/module-port';
import type { AccessPort } from './ports';

/**
 * Access mock adapters (BIO-UX-010-AC01, AC03, AC05): deterministically
 * cycle success, failure and offline outcomes so every state is reproducible
 * for local testing and demos, not just the happy path. Two independent
 * adapters, each with their own call counter, back this module's two ports.
 */
export const accessMockPort: AccessPort = createMockAdapter('AccessService', {
  scenarios: [
    { outcome: 'success', message: 'Access: policy created.' },
    { outcome: 'failure', message: 'Access: policy could not be saved — try again.' },
    { outcome: 'offline', message: 'Access: device or network appears offline (simulated).' },
  ],
});

export const accessViewAccessEventsMockPort: AccessPort = createMockAdapter('AccessService', {
  scenarios: [
    { outcome: 'success', message: 'Access: events loaded.' },
    { outcome: 'failure', message: 'Access: events failed to load — try again.' },
    { outcome: 'offline', message: 'Access: device or network appears offline (simulated).' },
  ],
});
