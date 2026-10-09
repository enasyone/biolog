// Developer tests for BIO-UX-004-AC03.
// SYNTHETIC: no real tenant/user/biometric data.
import { describe, expect, it } from 'vitest';
import { SyntheticDataStore } from '../../../web/lib/synthetic-data/store';
import { generateDataset } from '../../../web/lib/synthetic-data/dataset';

describe('BIO-UX-004-AC03 reset-to-seed restores a known state', () => {
  it('a mutated store returns to the exact original dataset after resetToSeed()', () => {
    const store = new SyntheticDataStore(42, 'normal');
    const original = store.getDataset();
    const originalSnapshot = JSON.parse(JSON.stringify(original));

    store.mutate((dataset) => ({ ...dataset, people: [] }));
    expect(store.getDataset().people).toHaveLength(0);
    expect(store.getDataset()).not.toEqual(original);

    store.resetToSeed();
    expect(store.getDataset()).toEqual(originalSnapshot);
  });

  it('resetToSeed() after multiple mutations still restores the known state', () => {
    const store = new SyntheticDataStore(7, 'normal');
    const originalSnapshot = JSON.parse(JSON.stringify(store.getDataset()));

    store.mutate((d) => ({ ...d, devices: [] }));
    store.mutate((d) => ({ ...d, visitors: [] }));
    store.mutate((d) => ({ ...d, flags: { ...d.flags, simulateError: true } }));

    store.resetToSeed();
    expect(store.getDataset()).toEqual(originalSnapshot);
  });

  it('setScenario() regenerates deterministically from the same seed', () => {
    const store = new SyntheticDataStore(99, 'normal');
    store.setScenario('empty');
    expect(store.getScenario()).toBe('empty');
    expect(store.getDataset()).toEqual(generateDataset(99, 'empty'));

    store.setScenario('normal');
    expect(store.getDataset()).toEqual(generateDataset(99, 'normal'));
  });

  it('a fresh store matches generateDataset() with the same seed and scenario', () => {
    const store = new SyntheticDataStore(123, 'lost-card');
    expect(store.getDataset()).toEqual(generateDataset(123, 'lost-card'));
  });
});
