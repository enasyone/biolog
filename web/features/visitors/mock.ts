import { createMockAdapter } from '../../lib/module-port';
import type { VisitorsPort } from './ports';

/**
 * Visitors mock adapter (BIO-UX-005-AC05): deterministically cycles success,
 * failure and offline outcomes so every state is reproducible for local
 * testing and demos, not just the happy path.
 */
export const visitorsMockPort: VisitorsPort = createMockAdapter('VisitorService', {
  scenarios: [
    { outcome: 'success', message: 'Visitors: visitor checked in.' },
    { outcome: 'failure', message: 'Visitors: check-in failed — try again.' },
    { outcome: 'offline', message: 'Visitors: device or network appears offline (simulated).' },
  ],
});

export const visitorsCheckOutMockPort: VisitorsPort = createMockAdapter('VisitorService', {
  scenarios: [
    { outcome: 'success', message: 'Visitors: visitor checked out.' },
    { outcome: 'failure', message: 'Visitors: check-out failed — try again.' },
    { outcome: 'offline', message: 'Visitors: device or network appears offline (simulated).' },
  ],
});

export const visitorsViewHistoryMockPort: VisitorsPort = createMockAdapter('VisitorService', {
  scenarios: [
    { outcome: 'success', message: 'Visitors: visit history loaded.' },
    { outcome: 'failure', message: 'Visitors: visit history failed to load — try again.' },
    { outcome: 'offline', message: 'Visitors: device or network appears offline (simulated).' },
  ],
});
