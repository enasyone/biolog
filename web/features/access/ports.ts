import type { ModulePort } from '../../lib/module-port';

/**
 * Access module ports (BIO-UX-010-AC01, AC03, AC05).
 *
 * `route-manifest.ts`'s `/access-control` entry names two actions —
 * `primaryAction` ("Create policy") and `viewAccessEvents` ("View access
 * events") — following the same two-port pattern `features/reporting`
 * established (BIO-UX-005-AC02). Both stay `ModulePort`-shaped (a nullary
 * call returning a `success`/`failure`/`offline` outcome): the port supplies
 * the simulated network-outcome for a command/query, while the actual
 * policy-builder input (assurance level, reason code) and the access-event
 * list rendered on `/access-control` are page-level state sourced from
 * `web/lib/synthetic-data` — see `docs/features/bio-ux-010/README.md`
 * "Why ports stay nullary" for the full reasoning.
 */
export type AccessPort = ModulePort;
