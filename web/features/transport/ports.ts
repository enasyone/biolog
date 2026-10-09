import type { ModulePort } from '../../lib/module-port';

/**
 * Transport module ports (BIO-UX-010-AC01, AC04, AC05).
 *
 * `route-manifest.ts`'s `/transport` entry names two actions —
 * `primaryAction` ("View boarding/alighting log") and `assignRoute`
 * ("Assign route") — following the same multi-port pattern
 * `features/reporting` established (BIO-UX-005-AC02). Both stay
 * `ModulePort`-shaped; the actual boarding/alighting log rendered on
 * `/transport` is page-level state sourced from `web/lib/synthetic-data`.
 */
export type TransportPort = ModulePort;
