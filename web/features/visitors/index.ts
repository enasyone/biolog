import { resolvePort } from '../../lib/feature-mode';
import { visitorsCheckOutMockPort, visitorsMockPort, visitorsViewHistoryMockPort } from './mock';
import { visitorsCheckOutLivePort, visitorsLivePort, visitorsViewHistoryLivePort } from './live';
import type { VisitorsPort } from './ports';

export type { VisitorsPort } from './ports';

/**
 * The Visitors module's three resolved ports (BIO-UX-010-AC01, AC04, AC05).
 * Pages/components import these, never `./mock` or `./live` directly —
 * switching `NEXT_PUBLIC_FEATURE_MODE_VISITORSERVICE` (all three ports) or
 * a per-port override (BIO-UX-010's own `feature-mode.ts` additions) from
 * SIMULATION to LIVE changes what these resolve to without any importer
 * changing.
 */
export const visitorsPort: VisitorsPort = resolvePort(visitorsMockPort, visitorsLivePort, 'primaryAction');

export const visitorsCheckOutPort: VisitorsPort = resolvePort(
  visitorsCheckOutMockPort,
  visitorsCheckOutLivePort,
  'checkOutVisitor',
);

export const visitorsViewHistoryPort: VisitorsPort = resolvePort(
  visitorsViewHistoryMockPort,
  visitorsViewHistoryLivePort,
  'viewVisitHistory',
);
