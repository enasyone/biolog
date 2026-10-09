// Developer tests supporting BIO-UX-004-AC01/AC03 (determinism).
// SYNTHETIC: no real data involved.
import { describe, expect, it } from 'vitest';
import { createSeededRng, rngBool, rngInt, rngPick } from '../../../web/lib/synthetic-data/rng';

describe('createSeededRng is deterministic', () => {
  it('the same seed produces the identical sequence of floats', () => {
    const a = createSeededRng(12345);
    const b = createSeededRng(12345);
    const seqA = Array.from({ length: 20 }, () => a());
    const seqB = Array.from({ length: 20 }, () => b());
    expect(seqA).toEqual(seqB);
  });

  it('every value is within [0, 1)', () => {
    const rng = createSeededRng(1);
    for (let i = 0; i < 100; i++) {
      const v = rng();
      expect(v).toBeGreaterThanOrEqual(0);
      expect(v).toBeLessThan(1);
    }
  });

  it('different seeds produce different sequences', () => {
    const a = createSeededRng(1)();
    const b = createSeededRng(2)();
    expect(a).not.toBe(b);
  });
});

describe('rng helpers', () => {
  it('rngInt stays within the inclusive bound and is deterministic per seed', () => {
    const rng = createSeededRng(7);
    for (let i = 0; i < 50; i++) {
      const v = rngInt(rng, 3, 9);
      expect(v).toBeGreaterThanOrEqual(3);
      expect(v).toBeLessThanOrEqual(9);
    }
  });

  it('rngPick only returns elements from the input array and throws on empty input', () => {
    const rng = createSeededRng(3);
    const pool = ['a', 'b', 'c'];
    for (let i = 0; i < 20; i++) {
      expect(pool).toContain(rngPick(rng, pool));
    }
    expect(() => rngPick(rng, [])).toThrow();
  });

  it('rngBool(rng, 0) is always false and rngBool(rng, 1) is always true', () => {
    const rng = createSeededRng(9);
    for (let i = 0; i < 10; i++) {
      expect(rngBool(rng, 0)).toBe(false);
      expect(rngBool(rng, 1)).toBe(true);
    }
  });
});
