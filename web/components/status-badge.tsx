import type { ReactNode } from 'react';

/**
 * Canonical status-badge tone set (design-system consolidation, 2026-09-23).
 * Every feature's domain-specific state (granted/denied, active/suspended,
 * online/offline, consistent/failed, etc.) maps onto one of these five
 * shared visual tones rather than each feature inventing its own badge —
 * see .biolog-status-badge's comment in globals.css for why (a WCAG AA
 * contrast defect independently found and fixed six times across the
 * W01.5 wave before this component existed).
 */
export type StatusBadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'error';

export function StatusBadge({ tone, children }: { tone: StatusBadgeTone; children: ReactNode }) {
  return <span className={`biolog-status-badge biolog-status-badge--${tone}`}>{children}</span>;
}
