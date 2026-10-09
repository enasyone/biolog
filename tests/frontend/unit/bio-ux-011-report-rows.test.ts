// Developer tests for BIO-UX-011-AC01 (Reports).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { generateDataset } from '../../../web/lib/synthetic-data';
import { computeReportRows, REPORT_LABELS, REPORT_TYPES } from '../../../web/lib/report-rows';

describe('BIO-UX-011-AC01 report rows are deterministic and tenant-scoped', () => {
  it('every report type has a label', () => {
    for (const type of REPORT_TYPES) {
      expect(REPORT_LABELS[type].length).toBeGreaterThan(0);
    }
  });

  it('every report type returns a two-column table with at least one row for a real tenant', () => {
    const dataset = generateDataset();
    for (const type of REPORT_TYPES) {
      const table = computeReportRows(type, dataset, 'alpha');
      expect(table.columns).toHaveLength(2);
      expect(table.rows.length).toBeGreaterThan(0);
    }
  });

  it('device-health lists only alpha-tenant devices', () => {
    const dataset = generateDataset();
    const alphaDeviceNames = new Set(
      dataset.devices.filter((d) => d.tenantId === 'alpha').map((d) => d.name),
    );
    const table = computeReportRows('device-health', dataset, 'alpha');
    for (const [deviceName] of table.rows) {
      expect(alphaDeviceNames.has(deviceName)).toBe(true);
    }
  });

  it('is deterministic: same inputs produce the exact same table', () => {
    const dataset = generateDataset();
    const first = computeReportRows('enrolment-summary', dataset, 'alpha');
    const second = computeReportRows('enrolment-summary', dataset, 'alpha');
    expect(first).toEqual(second);
  });

  it('an unknown tenant produces an empty (not erroring) table', () => {
    const dataset = generateDataset();
    const table = computeReportRows('enrolment-summary', dataset, 'no-such-tenant');
    expect(table.rows).toEqual([]);
  });
});
