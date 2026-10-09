import { createLiveAdapter } from '../../lib/module-port';
import type { AccessPort } from './ports';

/**
 * Access live adapters (BIO-UX-010-AC05). Each calls its own real backend
 * endpoint this domain's story wires up; until then both genuinely fail
 * closed to `offline` (see `createLiveAdapter`'s own comment) — the correct
 * behaviour for an unconnected LIVE mode, not a bug.
 */
export const accessLivePort: AccessPort = createLiveAdapter(
  'AccessService',
  '/api/v1/access/primary-action',
);

export const accessViewAccessEventsLivePort: AccessPort = createLiveAdapter(
  'AccessService',
  '/api/v1/access/view-access-events',
);
