/**
 * Public entry point composing generation + scenario application
 * (BIO-UX-004-AC01, AC02).
 */
import { buildBaseDataset } from './generators';
import { applyScenario } from './scenarios';
import type { ScenarioId, SyntheticDataset } from './types';

/** An arbitrary but fixed seed — stable across runs/releases, not derived from the current time. */
export const DEFAULT_SEED = 20260101;

/** Deterministic: the same `(seed, scenario)` pair always produces a deep-equal dataset (AC01, AC03). */
export function generateDataset(seed: number = DEFAULT_SEED, scenario: ScenarioId = 'normal'): SyntheticDataset {
  const base = buildBaseDataset(seed);
  return applyScenario(base, scenario);
}
