'use client';

import type { ModuleActionState } from '../lib/use-module-action';

/** Renders a `useModuleAction` state as the same loading/outcome markup `PlaceholderPage` uses. */
export function ModuleActionStatus({ state }: { state: ModuleActionState }) {
  if (state.status === 'loading') {
    return (
      <p role="status" className="biolog-placeholder-status biolog-placeholder-status--loading">
        Loading…
      </p>
    );
  }
  if (state.status === 'done') {
    return (
      <p role="status" className={`biolog-placeholder-status biolog-placeholder-status--${state.outcome}`}>
        {state.message}
      </p>
    );
  }
  return null;
}
