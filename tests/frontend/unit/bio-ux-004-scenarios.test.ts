// Developer tests for BIO-UX-004-AC02.
// SYNTHETIC: no real tenant/user/biometric data.
import { describe, expect, it } from 'vitest';
import { DEFAULT_SEED, generateDataset } from '../../../web/lib/synthetic-data/dataset';
import { SCENARIO_IDS } from '../../../web/lib/synthetic-data/types';

const AC02_NAMED_SCENARIOS = [
  'normal',
  'empty',
  'error',
  'offline',
  'low-quality-biometric',
  'duplicate-identity',
  'lost-card',
  'document-mismatch',
] as const;

describe('BIO-UX-004-AC02 scenario presets exist exactly as named', () => {
  it('SCENARIO_IDS starts with AC02\'s 8 scenarios verbatim, in order', () => {
    // BIO-UX-010-AC02 appends 3 more scenarios ('degraded-device', 'denied-access',
    // 'notification') to this same shared union — see bio-ux-010-scenarios.test.ts for
    // those. This test keeps asserting BIO-UX-004's own 8 are unchanged and still first.
    expect(SCENARIO_IDS.slice(0, AC02_NAMED_SCENARIOS.length)).toEqual(AC02_NAMED_SCENARIOS);
  });

  it('every scenario id generates without throwing', () => {
    for (const scenario of SCENARIO_IDS) {
      expect(() => generateDataset(DEFAULT_SEED, scenario)).not.toThrow();
    }
  });
});

describe('BIO-UX-004-AC02 each scenario produces its documented, testable effect', () => {
  it('empty: every domain collection is empty', () => {
    const dataset = generateDataset(DEFAULT_SEED, 'empty');
    expect(dataset.people).toHaveLength(0);
    expect(dataset.credentials).toHaveLength(0);
    expect(dataset.photos).toHaveLength(0);
    expect(dataset.devices).toHaveLength(0);
    expect(dataset.accessEvents).toHaveLength(0);
    expect(dataset.attendanceRecords).toHaveLength(0);
    expect(dataset.visitors).toHaveLength(0);
    expect(dataset.transportEvents).toHaveLength(0);
    expect(dataset.documents).toHaveLength(0);
    // Tenants remain — "empty" means empty operational data, not a tenant-less app shell.
    expect(dataset.tenants.length).toBeGreaterThan(0);
  });

  it('error: simulateError flag is set, records are otherwise untouched', () => {
    const normal = generateDataset(DEFAULT_SEED, 'normal');
    const error = generateDataset(DEFAULT_SEED, 'error');
    expect(error.flags.simulateError).toBe(true);
    expect(error.people).toEqual(normal.people);
  });

  it('offline: simulateOffline flag is set and every device reports offline', () => {
    const offline = generateDataset(DEFAULT_SEED, 'offline');
    expect(offline.flags.simulateOffline).toBe(true);
    expect(offline.devices.length).toBeGreaterThan(0);
    for (const device of offline.devices) expect(device.connectivity).toBe('offline');
  });

  it('low-quality-biometric: at least one photo is below the quality threshold', () => {
    const dataset = generateDataset(DEFAULT_SEED, 'low-quality-biometric');
    expect(dataset.photos.some((p) => p.belowQualityThreshold)).toBe(true);
    expect(dataset.photos.some((p) => p.qualityScore < 50)).toBe(true);
  });

  it('duplicate-identity: at least two people share a duplicateGroupId', () => {
    const dataset = generateDataset(DEFAULT_SEED, 'duplicate-identity');
    const grouped = dataset.people.filter((p) => p.duplicateGroupId);
    expect(grouped.length).toBeGreaterThanOrEqual(2);
    expect(new Set(grouped.map((p) => p.duplicateGroupId)).size).toBe(1);
  });

  it('lost-card: at least one credential has status lost', () => {
    const dataset = generateDataset(DEFAULT_SEED, 'lost-card');
    expect(dataset.credentials.some((c) => c.status === 'lost')).toBe(true);
  });

  it('document-mismatch: at least one document has matchResult mismatch', () => {
    const dataset = generateDataset(DEFAULT_SEED, 'document-mismatch');
    expect(dataset.documents.some((d) => d.matchResult === 'mismatch')).toBe(true);
  });
});

describe('BIO-UX-004-AC02 scenario application is deterministic', () => {
  it('the same seed+scenario pair produces a deep-equal dataset across two calls', () => {
    for (const scenario of SCENARIO_IDS) {
      const a = generateDataset(DEFAULT_SEED, scenario);
      const b = generateDataset(DEFAULT_SEED, scenario);
      expect(a).toEqual(b);
    }
  });
});
