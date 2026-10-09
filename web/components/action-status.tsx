import type { PrimaryActionResult } from '../lib/module-port';

/**
 * Shared action-result state shape and renderer (BIO-UX-010-AC05), matching
 * `placeholder-page.tsx`'s own `ActionState`/markup exactly — the UI-first
 * rule (see `docs/features/bio-ux-010/README.md`) requires preserving the
 * approved loading/success/failure/offline interaction pattern already
 * shipped by BIO-UX-005, not inventing a new one per domain page.
 */
export type ActionState =
  | { status: 'idle' }
  | { status: 'loading' }
  | ({ status: 'done' } & PrimaryActionResult);

export function ActionStatus({ action }: { action: ActionState }) {
  if (action.status === 'loading') {
    return (
      <p role="status" className="biolog-placeholder-status biolog-placeholder-status--loading">
        Loading…
      </p>
    );
  }
  if (action.status === 'done') {
    return (
      <p role="status" className={`biolog-placeholder-status biolog-placeholder-status--${action.outcome}`}>
        {action.message}
      </p>
    );
  }
  return null;
}
