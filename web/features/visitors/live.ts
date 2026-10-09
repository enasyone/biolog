import { createLiveAdapter } from '../../lib/module-port';
import type { VisitorsPort } from './ports';

/**
 * Visitors live adapter (BIO-UX-005-AC04). Calls the real backend endpoint
 * this domain's story wires up; until then this genuinely fails closed to
 * `offline` (see `createLiveAdapter`'s own comment) — the correct behaviour
 * for an unconnected LIVE mode, not a bug.
 */
export const visitorsLivePort: VisitorsPort = createLiveAdapter(
  'VisitorService',
  '/api/v1/visitors/primary-action',
);

export const visitorsCheckOutLivePort: VisitorsPort = createLiveAdapter(
  'VisitorService',
  '/api/v1/visitors/check-out-visitor',
);

export const visitorsViewHistoryLivePort: VisitorsPort = createLiveAdapter(
  'VisitorService',
  '/api/v1/visitors/view-visit-history',
);
