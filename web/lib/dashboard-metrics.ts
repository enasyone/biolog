/**
 * Dashboard metric/activity computation (BIO-UX-011-AC03, AC04).
 *
 * Pure functions over BIO-UX-004's deterministic synthetic dataset — no
 * `Math.random()`, no `Date.now()`, no invented numbers. Extracted from
 * `app/page.tsx` so the aggregation logic is unit-testable independent of
 * rendering, and filtered by `tenantId` so switching the header's tenant
 * selector changes every value here (AC04).
 */
import type { SyntheticDataset } from './synthetic-data';

export interface DashboardMetrics {
  peopleEnrolled: number;
  activeCredentials: number;
  devicesOnline: number;
  devicesTotal: number;
  duplicateReviewsPending: number;
}

export function computeDashboardMetrics(dataset: SyntheticDataset, tenantId: string): DashboardMetrics {
  const people = dataset.people.filter((p) => p.tenantId === tenantId);
  const credentials = dataset.credentials.filter((c) => c.tenantId === tenantId);
  const devices = dataset.devices.filter((d) => d.tenantId === tenantId);
  return {
    peopleEnrolled: people.length,
    activeCredentials: credentials.filter((c) => c.status === 'active').length,
    devicesOnline: devices.filter((d) => d.connectivity === 'online').length,
    devicesTotal: devices.length,
    duplicateReviewsPending: people.filter((p) => p.duplicateGroupId).length,
  };
}

export interface ActivityRow {
  id: string;
  label: string;
  detail: string;
  occurredAt: string;
}

/** Most-recent-first, capped at `limit` (default 6) — merges access + transport events. */
export function computeRecentActivity(
  dataset: SyntheticDataset,
  tenantId: string,
  limit = 6,
): readonly ActivityRow[] {
  const access: ActivityRow[] = dataset.accessEvents
    .filter((e) => e.tenantId === tenantId)
    .map((e) => ({
      id: e.id,
      label: e.decision === 'granted' ? 'Access granted' : 'Access denied',
      detail: dataset.devices.find((d) => d.id === e.deviceId)?.name ?? 'Unknown device',
      occurredAt: e.occurredAt,
    }));
  const transport: ActivityRow[] = dataset.transportEvents
    .filter((e) => e.tenantId === tenantId)
    .map((e) => ({
      id: e.id,
      label: e.eventType === 'boarding' ? 'Boarded transport' : 'Alighted transport',
      detail: e.routeName,
      occurredAt: e.occurredAt,
    }));
  return [...access, ...transport].sort((a, b) => b.occurredAt.localeCompare(a.occurredAt)).slice(0, limit);
}
