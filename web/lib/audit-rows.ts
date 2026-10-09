/** Audit trail row computation (BIO-UX-011-AC01, AC04) — see `dashboard-metrics.ts`'s header note. */
import type { SyntheticDataset } from './synthetic-data';

export interface AuditRow {
  id: string;
  occurredAt: string;
  actor: string;
  device: string;
  decision: 'granted' | 'denied';
}

export function computeAuditRows(dataset: SyntheticDataset, tenantId: string, limit = 10): readonly AuditRow[] {
  return dataset.accessEvents
    .filter((e) => e.tenantId === tenantId)
    .map((event) => {
      const person = dataset.people.find((p) => p.id === event.personId);
      const device = dataset.devices.find((d) => d.id === event.deviceId);
      return {
        id: event.id,
        occurredAt: event.occurredAt,
        actor: person?.displayName ?? 'Unknown (SYNTHETIC)',
        device: device?.name ?? 'Unknown device',
        decision: event.decision,
      };
    })
    .sort((a, b) => b.occurredAt.localeCompare(a.occurredAt))
    .slice(0, limit);
}
