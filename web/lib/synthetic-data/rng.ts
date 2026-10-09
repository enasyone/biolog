/**
 * Deterministic PRNG for synthetic data generation (BIO-UX-004-AC01, AC03).
 *
 * mulberry32: a small, well-known deterministic generator — the same numeric
 * seed always produces the same sequence of floats in [0, 1), on any engine,
 * with no external entropy source. That determinism is the whole point: a
 * human tester (or a Playwright run) can reset to a seed and see the exact
 * same synthetic BIOLOG dataset every time (AC03), not a fresh random one.
 */
export type Rng = () => number;

export function createSeededRng(seed: number): Rng {
  let state = seed >>> 0;
  return function next(): number {
    state |= 0;
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Integer in `[min, max]` inclusive. */
export function rngInt(rng: Rng, min: number, max: number): number {
  return min + Math.floor(rng() * (max - min + 1));
}

/** Picks one element deterministically; throws on an empty array (a generator bug, not runtime input). */
export function rngPick<T>(rng: Rng, items: readonly T[]): T {
  if (items.length === 0) throw new Error('rngPick: items must not be empty');
  return items[rngInt(rng, 0, items.length - 1)]!;
}

/** True with probability `p` (0..1). */
export function rngBool(rng: Rng, p: number): boolean {
  return rng() < p;
}
