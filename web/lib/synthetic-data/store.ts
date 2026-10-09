/**
 * A minimal mutable holder for a generated dataset, with a real reset-to-seed
 * action (BIO-UX-004-AC03).
 *
 * Generation itself is pure (`generateDataset`); this class exists only
 * because a future mock adapter (BIO-UX-005) or the Human Test Console
 * (BIO-UX-012) needs somewhere to apply in-session mutations (e.g. a
 * simulated "Issue card" primary action updating a credential's status) and
 * a way to discard them. No such mutating caller exists yet — wiring this
 * store into the shell/layout is BIO-UX-005/012's integration point, not
 * decided unilaterally here (shared-shell changes need Damilola's
 * approval — see `docs/features/bio-ux-004/README.md`).
 */
import { DEFAULT_SEED, generateDataset } from './dataset';
import type { ScenarioId, SyntheticDataset } from './types';

export class SyntheticDataStore {
  private readonly seed: number;
  private scenario: ScenarioId;
  private dataset: SyntheticDataset;

  constructor(seed: number = DEFAULT_SEED, scenario: ScenarioId = 'normal') {
    this.seed = seed;
    this.scenario = scenario;
    this.dataset = generateDataset(seed, scenario);
  }

  getDataset(): SyntheticDataset {
    return this.dataset;
  }

  getScenario(): ScenarioId {
    return this.scenario;
  }

  /** Switches scenario and regenerates from the seed under it — any prior in-session mutation is discarded. */
  setScenario(scenario: ScenarioId): void {
    this.scenario = scenario;
    this.dataset = generateDataset(this.seed, scenario);
  }

  /** Applies an in-session mutation on top of the current dataset (e.g. a simulated primary action). */
  mutate(updater: (dataset: SyntheticDataset) => SyntheticDataset): void {
    this.dataset = updater(this.dataset);
  }

  /** BIO-UX-004-AC03: discards every mutation, restoring the exact dataset this seed+scenario produces. */
  resetToSeed(): void {
    this.dataset = generateDataset(this.seed, this.scenario);
  }
}
