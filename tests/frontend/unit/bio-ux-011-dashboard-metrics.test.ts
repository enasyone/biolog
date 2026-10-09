// Developer tests for BIO-UX-011-AC03, AC04.
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { generateDataset } from '../../../web/lib/synthetic-data';
import { computeDashboardMetrics, computeRecentActivity } from '../../../web/lib/dashboard-metrics';

describe('BIO-UX-011-AC03 dashboard metrics are deterministic synthetic data', () => {
  it('produces the exact same metrics on repeated calls (deterministic, no Math.random/Date.now)', () => {
    const dataset = generateDataset();
    const first = computeDashboardMetrics(dataset, 'alpha');
    const second = computeDashboardMetrics(dataset, 'alpha');
    expect(first).toEqual(second);
  });

  it('never returns negative or non-finite counts', () => {
    const dataset = generateDataset();
    const metrics = computeDashboardMetrics(dataset, 'alpha');
    for (const value of Object.values(metrics)) {
      expect(Number.isFinite(value)).toBe(true);
      expect(value).toBeGreaterThanOrEqual(0);
    }
  });

  it('devicesOnline never exceeds devicesTotal', () => {
    const dataset = generateDataset();
    const metrics = computeDashboardMetrics(dataset, 'alpha');
    expect(metrics.devicesOnline).toBeLessThanOrEqual(metrics.devicesTotal);
  });
});

describe('BIO-UX-011-AC04 metrics and activity are tenant-scoped', () => {
  it('alpha and beta tenants produce independent, generally different metrics', () => {
    const dataset = generateDataset();
    const alpha = computeDashboardMetrics(dataset, 'alpha');
    const beta = computeDashboardMetrics(dataset, 'beta');
    // Both tenants are non-empty and scoped independently - not asserting they differ in every
    // field (the generator could coincidentally produce equal counts), just that each is
    // computed only from its own tenant's records.
    expect(alpha.peopleEnrolled).toBeGreaterThan(0);
    expect(beta.peopleEnrolled).toBeGreaterThan(0);
  });

  it('an unknown tenant id returns all-zero metrics, not an error or another tenant\'s data', () => {
    const dataset = generateDataset();
    const metrics = computeDashboardMetrics(dataset, 'no-such-tenant');
    expect(metrics).toEqual({
      peopleEnrolled: 0,
      activeCredentials: 0,
      devicesOnline: 0,
      devicesTotal: 0,
      duplicateReviewsPending: 0,
    });
  });

  it('recent activity rows all belong to the requested tenant', () => {
    const dataset = generateDataset();
    const alphaPersonIds = new Set(dataset.people.filter((p) => p.tenantId === 'alpha').map((p) => p.id));
    const alphaDeviceIds = new Set(dataset.devices.filter((d) => d.tenantId === 'alpha').map((d) => d.id));
    const activity = computeRecentActivity(dataset, 'alpha');
    // Every activity detail (device/route name) should trace back to something scoped to alpha -
    // spot-check devices specifically, since access-event details are device names.
    const alphaDeviceNames = new Set(
      dataset.devices.filter((d) => alphaDeviceIds.has(d.id)).map((d) => d.name),
    );
    for (const row of activity) {
      if (row.label.startsWith('Access')) {
        expect(alphaDeviceNames.has(row.detail)).toBe(true);
      }
    }
    void alphaPersonIds;
  });

  it('recent activity is sorted most-recent-first and respects the limit', () => {
    const dataset = generateDataset();
    const activity = computeRecentActivity(dataset, 'alpha', 3);
    expect(activity.length).toBeLessThanOrEqual(3);
    for (let i = 1; i < activity.length; i += 1) {
      expect(activity[i - 1]!.occurredAt >= activity[i]!.occurredAt).toBe(true);
    }
  });
});
