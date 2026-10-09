'use client';

import { useState } from 'react';
import { MODULE_PORTS } from './module-registry';
import type { PrimaryActionResult } from './module-port';
import type { ServicePort } from './route-manifest';

/**
 * Shared client hook for calling a module port and observing its
 * loading/success/failure/offline result (BIO-UX-005-AC01, BIO-UX-011-AC05).
 *
 * A separate, small hook rather than reworking `PlaceholderPage`'s own
 * internal state machine — that component's rendered DOM (`.biolog-button-
 * primary`, `role="status"`, `.biolog-placeholder-status--<outcome>`) is
 * already pinned by BIO-UX-002/005's Playwright suites; this hook is for
 * BIO-UX-011's own new, real pages (Dashboard/Settings/Reports), which reuse
 * the same CSS classes for visual consistency (BIO-UX-011-AC06) without
 * touching that existing, tested component.
 */
export type ModuleActionState =
  | { status: 'idle' }
  | { status: 'loading' }
  | ({ status: 'done' } & PrimaryActionResult);

export function useModuleAction(module: ServicePort, portKey = 'primaryAction') {
  const [state, setState] = useState<ModuleActionState>({ status: 'idle' });

  async function run() {
    setState({ status: 'loading' });
    const port = MODULE_PORTS[module][portKey];
    if (!port) {
      throw new Error(`No port "${portKey}" registered for module "${module}".`);
    }
    const result = await port.primaryAction();
    setState({ status: 'done', ...result });
  }

  function reset() {
    setState({ status: 'idle' });
  }

  return { state, run, reset };
}
