import { createLiveAdapter } from '../../lib/module-port';
import type { TransportPort } from './ports';

/**
 * Transport live adapter (BIO-UX-005-AC04). Calls the real backend endpoint
 * this domain's story wires up; until then this genuinely fails closed to
 * `offline` (see `createLiveAdapter`'s own comment) — the correct behaviour
 * for an unconnected LIVE mode, not a bug.
 */
export const transportLivePort: TransportPort = createLiveAdapter(
  'TransportService',
  '/api/v1/transport/primary-action',
);

export const transportAssignRouteLivePort: TransportPort = createLiveAdapter(
  'TransportService',
  '/api/v1/transport/assign-route',
);
