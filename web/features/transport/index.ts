import { resolvePort } from '../../lib/feature-mode';
import { transportAssignRouteMockPort, transportMockPort } from './mock';
import { transportAssignRouteLivePort, transportLivePort } from './live';
import type { TransportPort } from './ports';

export type { TransportPort } from './ports';

/**
 * The Transport module's two resolved ports (BIO-UX-010-AC01, AC04, AC05).
 * Pages/components import these, never `./mock` or `./live` directly.
 */
export const transportPort: TransportPort = resolvePort(transportMockPort, transportLivePort, 'primaryAction');

export const transportAssignRoutePort: TransportPort = resolvePort(
  transportAssignRouteMockPort,
  transportAssignRouteLivePort,
  'assignRoute',
);
