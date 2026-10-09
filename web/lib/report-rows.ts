/** Report result-table computation (BIO-UX-011-AC01) — see `dashboard-metrics.ts`'s header note. */
import type { SyntheticDataset } from './synthetic-data';

export type ReportType = 'enrolment-summary' | 'card-production' | 'attendance-summary' | 'device-health';

export const REPORT_TYPES: readonly ReportType[] = Object.freeze([
  'enrolment-summary',
  'card-production',
  'attendance-summary',
  'device-health',
]);

export const REPORT_LABELS: Readonly<Record<ReportType, string>> = Object.freeze({
  'enrolment-summary': 'Enrolment Summary',
  'card-production': 'Card Production',
  'attendance-summary': 'Attendance Summary',
  'device-health': 'Device Health',
});

export interface ReportTable {
  columns: readonly [string, string];
  rows: ReadonlyArray<readonly [string, string]>;
}

export function computeReportRows(type: ReportType, dataset: SyntheticDataset, tenantId: string): ReportTable {
  switch (type) {
    case 'enrolment-summary': {
      const people = dataset.people.filter((p) => p.tenantId === tenantId);
      const byCategory = new Map<string, number>();
      for (const person of people) {
        byCategory.set(person.category, (byCategory.get(person.category) ?? 0) + 1);
      }
      return {
        columns: ['Category', 'Count'],
        rows: [...byCategory.entries()].map(([category, count]) => [category, String(count)] as const),
      };
    }
    case 'card-production': {
      const credentials = dataset.credentials.filter((c) => c.tenantId === tenantId);
      const byStatus = new Map<string, number>();
      for (const credential of credentials) {
        byStatus.set(credential.status, (byStatus.get(credential.status) ?? 0) + 1);
      }
      return {
        columns: ['Status', 'Count'],
        rows: [...byStatus.entries()].map(([status, count]) => [status, String(count)] as const),
      };
    }
    case 'attendance-summary': {
      const records = dataset.attendanceRecords.filter((r) => r.tenantId === tenantId);
      const byStatus = new Map<string, number>();
      for (const record of records) {
        byStatus.set(record.status, (byStatus.get(record.status) ?? 0) + 1);
      }
      return {
        columns: ['Status', 'Count'],
        rows: [...byStatus.entries()].map(([status, count]) => [status, String(count)] as const),
      };
    }
    case 'device-health': {
      const devices = dataset.devices.filter((d) => d.tenantId === tenantId);
      return {
        columns: ['Device', 'Connectivity'],
        rows: devices.map((device) => [device.name, device.connectivity] as const),
      };
    }
    default:
      return { columns: ['', ''], rows: [] };
  }
}
