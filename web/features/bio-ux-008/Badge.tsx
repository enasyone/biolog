import type { Badge as BadgeValue } from './formatters';

/**
 * Uses the canonical `.biolog-status-badge` classes (design-system
 * consolidation, 2026-09-23) instead of this feature's own CSS-module
 * badge, which paired colorSuccess/-warning/-error text directly on its
 * own -bg tint and failed WCAG AA at this size (see .biolog-status-badge's
 * comment in globals.css) — a live defect since this shipped, caught only
 * once the same pattern was found reinvented five more times elsewhere.
 */
export function Badge({ tone, text }: BadgeValue) {
  return (
    <span
      className={`biolog-status-badge biolog-status-badge--${tone}`}
      role="status"
      data-testid="outcome-badge"
    >
      {text}
    </span>
  );
}
