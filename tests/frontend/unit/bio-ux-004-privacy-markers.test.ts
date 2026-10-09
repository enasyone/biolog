// Developer tests for BIO-UX-004-AC04, AC05.
// SYNTHETIC: no real tenant/user/biometric data.
import { describe, expect, it } from 'vitest';
import { generateDataset } from '../../../web/lib/synthetic-data/dataset';
import { SYNTHETIC_MARKER } from '../../../web/lib/synthetic-data/names';
import { SCENARIO_IDS } from '../../../web/lib/synthetic-data/types';

const DOMAINS = [
  'tenants',
  'people',
  'credentials',
  'photos',
  'devices',
  'accessEvents',
  'attendanceRecords',
  'visitors',
  'transportEvents',
  'documents',
] as const;

/** Field names that would signal real, sensitive identity data if they ever appeared. */
const FORBIDDEN_FIELD_NAME_FRAGMENTS = [
  'password',
  'secret',
  'ssn',
  'nin', // Nigerian National Identification Number
  'bvn', // Bank Verification Number
  'biometrictemplate',
  'templatebytes',
  'imagedata',
];

function collectFieldNames(value: unknown, into: Set<string>): void {
  if (Array.isArray(value)) {
    for (const item of value) collectFieldNames(item, into);
    return;
  }
  if (value && typeof value === 'object') {
    for (const [key, v] of Object.entries(value)) {
      into.add(key.toLowerCase());
      collectFieldNames(v, into);
    }
  }
}

describe('BIO-UX-004-AC04 no production PII, biometric templates, secrets or tenant data', () => {
  it('every record in every domain carries the machine-readable isSynthetic marker', () => {
    const dataset = generateDataset();
    for (const domain of DOMAINS) {
      for (const record of dataset[domain] as readonly { isSynthetic: boolean }[]) {
        expect(record.isSynthetic, `${domain} record missing isSynthetic`).toBe(true);
      }
    }
  });

  it('no field name across the dataset resembles a real secret, national id, or biometric template', () => {
    const dataset = generateDataset();
    const fieldNames = new Set<string>();
    collectFieldNames(dataset, fieldNames);
    for (const forbidden of FORBIDDEN_FIELD_NAME_FRAGMENTS) {
      const match = [...fieldNames].find((name) => name.includes(forbidden));
      expect(match, `dataset has a field name resembling "${forbidden}": ${match}`).toBeUndefined();
    }
  });

  it('photo records hold only a quality score and a placeholder colour, never image bytes', () => {
    const dataset = generateDataset();
    for (const photo of dataset.photos) {
      expect(Object.keys(photo).sort()).toEqual(
        ['belowQualityThreshold', 'id', 'isSynthetic', 'personId', 'placeholderColor', 'qualityScore', 'tenantId'].sort(),
      );
      expect(typeof photo.placeholderColor).toBe('string');
      expect(photo.placeholderColor.startsWith('hsl(')).toBe(true);
    }
  });
});

describe('BIO-UX-004-AC05 synthetic Nigerian names and institutions are clearly marked as test data', () => {
  it('every tenant name carries the (SYNTHETIC) marker', () => {
    const dataset = generateDataset();
    for (const tenant of dataset.tenants) {
      expect(tenant.name.includes(SYNTHETIC_MARKER)).toBe(true);
    }
  });

  it('every person displayName and visitor fullName carries the (SYNTHETIC) marker', () => {
    const dataset = generateDataset();
    for (const person of dataset.people) {
      expect(person.displayName.includes(SYNTHETIC_MARKER)).toBe(true);
    }
    for (const visitor of dataset.visitors) {
      expect(visitor.fullName.includes(SYNTHETIC_MARKER)).toBe(true);
    }
  });

  it('device names carry the (SYNTHETIC) marker', () => {
    const dataset = generateDataset();
    for (const device of dataset.devices) {
      expect(device.name.includes(SYNTHETIC_MARKER)).toBe(true);
    }
  });

  it('holds for every scenario, not just normal', () => {
    for (const scenario of SCENARIO_IDS) {
      const dataset = generateDataset(20260101, scenario);
      for (const tenant of dataset.tenants) expect(tenant.name.includes(SYNTHETIC_MARKER)).toBe(true);
      for (const person of dataset.people) expect(person.displayName.includes(SYNTHETIC_MARKER)).toBe(true);
    }
  });
});
