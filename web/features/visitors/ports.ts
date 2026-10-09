import type { ModulePort } from '../../lib/module-port';

/**
 * Visitor module ports (BIO-UX-010-AC01, AC04, AC05).
 *
 * `route-manifest.ts`'s `/visitors` entry names three actions —
 * `primaryAction` ("Check in visitor"), `checkOutVisitor` ("Check out
 * visitor") and `viewVisitHistory` ("View visit history") — its one route
 * with two secondary actions, following `features/reporting`'s multi-port
 * pattern (BIO-UX-005-AC02) extended to three. All three stay
 * `ModulePort`-shaped; the actual visitor list/status rendered on
 * `/visitors` is page-level state sourced from `web/lib/synthetic-data`.
 *
 * Note: this module's `ServicePort` is `VisitorService`, not `VisitorsService` —
 * matches `route-manifest.ts`/`module-port.ts` exactly.
 */
export type VisitorsPort = ModulePort;
