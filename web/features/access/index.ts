import { resolvePort } from '../../lib/feature-mode';
import { accessMockPort, accessViewAccessEventsMockPort } from './mock';
import { accessLivePort, accessViewAccessEventsLivePort } from './live';
import type { AccessPort } from './ports';

export type { AccessPort } from './ports';

/**
 * The Access module's two resolved ports (BIO-UX-010-AC01, AC05).
 * Pages/components import these, never `./mock` or `./live` directly —
 * switching `NEXT_PUBLIC_FEATURE_MODE_ACCESSSERVICE` (both ports) or
 * `NEXT_PUBLIC_FEATURE_MODE_ACCESSSERVICE_VIEWACCESSEVENTS` (this port only)
 * from SIMULATION to LIVE changes what these resolve to without any importer
 * changing.
 */
export const accessPort: AccessPort = resolvePort(accessMockPort, accessLivePort, 'primaryAction');

export const accessViewAccessEventsPort: AccessPort = resolvePort(
  accessViewAccessEventsMockPort,
  accessViewAccessEventsLivePort,
  'viewAccessEvents',
);
