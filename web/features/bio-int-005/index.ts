import { resolvePort } from '../../lib/feature-mode';
import { decideConflictMockPort, listConflictsMockPort, scanConflictsMockPort } from './mock';
import { decideConflictLivePort, listConflictsLivePort, scanConflictsLivePort } from './live';

export type {
  DecideConflictInput,
  DecideConflictResult,
  ImportConflictDecision,
  ImportConflictReviewStatusWire,
  ImportConflictReviewSummary,
  ImportFieldConflictSummary,
  ListConflictsResult,
  MappingScopedInput,
  ScanConflictsResult,
} from './ports';

/**
 * BIO-INT-005's resolved ports. Pages/components import these, never `./mock` or `./live`
 * directly (same discipline as every other module in this epic). Each is independently
 * mode-switchable via its own `NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE_<KEY>` override
 * (`web/lib/feature-mode.ts`'s own `PER_PORT_ENV_DEFAULTS`), falling back to the module-wide
 * `NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE` default.
 */
export const scanConflictsPort = resolvePort(scanConflictsMockPort, scanConflictsLivePort, 'scanConflicts');
export const listConflictsPort = resolvePort(listConflictsMockPort, listConflictsLivePort, 'listConflicts');
export const decideConflictPort = resolvePort(decideConflictMockPort, decideConflictLivePort, 'decideConflict');
