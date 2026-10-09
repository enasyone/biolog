// Developer tests for BIO-UX-004-AC01.
// SYNTHETIC: no real tenant/user/biometric data.
import { describe, expect, it } from 'vitest';
import { DEFAULT_SEED, generateDataset } from '../../../web/lib/synthetic-data/dataset';

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

describe('BIO-UX-004-AC01 seeded synthetic records are generated for every named domain', () => {
  const dataset = generateDataset(DEFAULT_SEED, 'normal');

  it('produces at least one record for every domain named in AC01', () => {
    for (const domain of DOMAINS) {
      expect(dataset[domain].length, `${domain} is empty in the normal scenario`).toBeGreaterThan(0);
    }
  });

  it('generates more than one tenant', () => {
    expect(dataset.tenants.length).toBeGreaterThanOrEqual(2);
  });

  it('every credential/photo/attendance/document record references a real generated person', () => {
    const personIds = new Set(dataset.people.map((p) => p.id));
    for (const c of dataset.credentials) expect(personIds.has(c.personId)).toBe(true);
    for (const p of dataset.photos) expect(personIds.has(p.personId)).toBe(true);
    for (const a of dataset.attendanceRecords) expect(personIds.has(a.personId)).toBe(true);
    for (const d of dataset.documents) expect(personIds.has(d.personId)).toBe(true);
  });

  it('every access/transport event references a real generated person, and access events a real device', () => {
    const personIds = new Set(dataset.people.map((p) => p.id));
    const deviceIds = new Set(dataset.devices.map((d) => d.id));
    for (const e of dataset.accessEvents) {
      expect(personIds.has(e.personId)).toBe(true);
      expect(deviceIds.has(e.deviceId)).toBe(true);
    }
    for (const t of dataset.transportEvents) expect(personIds.has(t.personId)).toBe(true);
  });

  it('every visitor references a real generated host person', () => {
    const personIds = new Set(dataset.people.map((p) => p.id));
    for (const v of dataset.visitors) expect(personIds.has(v.hostPersonId)).toBe(true);
  });

  it('record ids are unique within each domain', () => {
    for (const domain of DOMAINS) {
      const ids = dataset[domain].map((r: { id: string }) => r.id);
      expect(new Set(ids).size, `${domain} has duplicate ids`).toBe(ids.length);
    }
  });
});

describe('BIO-UX-004-AC01 generation is deterministic', () => {
  it('the same seed produces a deep-equal dataset across two independent calls', () => {
    const a = generateDataset(DEFAULT_SEED, 'normal');
    const b = generateDataset(DEFAULT_SEED, 'normal');
    expect(a).toEqual(b);
  });

  it('a different seed produces a different dataset', () => {
    const a = generateDataset(1, 'normal');
    const b = generateDataset(2, 'normal');
    expect(a).not.toEqual(b);
  });
});
