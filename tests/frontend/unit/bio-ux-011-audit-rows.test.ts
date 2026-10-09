// Developer tests for BIO-UX-011-AC01, AC04 (Audit).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { generateDataset } from '../../../web/lib/synthetic-data';
import { computeAuditRows } from '../../../web/lib/audit-rows';

describe('BIO-UX-011-AC01 audit rows are deterministic and tenant-scoped', () => {
  it('every row has a decision of granted or denied, an actor and a device', () => {
    const dataset = generateDataset();
    const rows = computeAuditRows(dataset, 'alpha');
    for (const row of rows) {
      expect(['granted', 'denied']).toContain(row.decision);
      expect(row.actor.length).toBeGreaterThan(0);
      expect(row.device.length).toBeGreaterThan(0);
    }
  });

  it('is sorted most-recent-first', () => {
    const dataset = generateDataset();
    const rows = computeAuditRows(dataset, 'alpha');
    for (let i = 1; i < rows.length; i += 1) {
      expect(rows[i - 1]!.occurredAt >= rows[i]!.occurredAt).toBe(true);
    }
  });

  it('respects the row limit', () => {
    const dataset = generateDataset();
    const rows = computeAuditRows(dataset, 'alpha', 2);
    expect(rows.length).toBeLessThanOrEqual(2);
  });

  it('an unknown tenant returns no rows, not another tenant\'s data', () => {
    const dataset = generateDataset();
    const rows = computeAuditRows(dataset, 'no-such-tenant');
    expect(rows).toEqual([]);
  });

  it('is deterministic across calls', () => {
    const dataset = generateDataset();
    expect(computeAuditRows(dataset, 'alpha')).toEqual(computeAuditRows(dataset, 'alpha'));
  });
});
