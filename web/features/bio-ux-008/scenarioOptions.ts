/**
 * Pure, testable option lists for every "selectable" simulated outcome this
 * story's screens expose (BIO-UX-008-AC03: "capture progress, quality
 * scores, match/no-match and low-quality outcomes are selectable"). Kept
 * separate from the `.tsx` components so the full outcome catalogue is
 * unit-testable in Node without a DOM (this repo's `tests/frontend/vitest.config.mjs`
 * runs `environment: "node"` — no `document`/`window` — the same reason
 * `web/features/bio-crd-002/cardDesignState.ts` etc. keep pure logic out of
 * their own `.tsx` files).
 */
import type {
  CaptureScenarioHint,
  DeviceScenarioHint,
  FaceCaptureScenarioHint,
  IdentificationScenarioHint,
  LivenessScenarioHint,
  VerificationScenarioHint,
} from '../biometrics/ports';

export interface ScenarioOption<T extends string> {
  value: T;
  label: string;
}

/** `undefined` means "let the deterministic cycle decide" — always offered first (BIO-UX-005-AC05's own reproducible-cycle default, kept reachable, not replaced by manual selection). */
export const AUTO_OPTION_VALUE = '' as const;

export const FINGERPRINT_CAPTURE_OPTIONS: readonly ScenarioOption<CaptureScenarioHint>[] = Object.freeze([
  { value: 'good', label: 'Good quality' },
  { value: 'low', label: 'Low quality' },
  { value: 'exception', label: 'Capture exception (e.g. bandaged finger)' },
]);

export const FACE_CAPTURE_OPTIONS: readonly ScenarioOption<FaceCaptureScenarioHint>[] = Object.freeze([
  { value: 'good', label: 'Good quality' },
  { value: 'low', label: 'Low quality' },
]);

export const VERIFICATION_OPTIONS: readonly ScenarioOption<VerificationScenarioHint>[] = Object.freeze([
  { value: 'match', label: 'Match' },
  { value: 'no_match', label: 'No match' },
]);

export const IDENTIFICATION_OPTIONS: readonly ScenarioOption<IdentificationScenarioHint>[] = Object.freeze([
  { value: 'candidates_found', label: 'Candidates found' },
  { value: 'no_candidates', label: 'No candidates found' },
]);

export const LIVENESS_OPTIONS: readonly ScenarioOption<LivenessScenarioHint>[] = Object.freeze([
  { value: 'live', label: 'Live' },
  { value: 'spoof_suspected', label: 'Spoof suspected' },
]);

export const DEVICE_OPTIONS: readonly ScenarioOption<DeviceScenarioHint>[] = Object.freeze([
  { value: 'connected', label: 'Connects successfully' },
  { value: 'offline', label: 'Appears offline' },
]);

/** Every option list above must offer at least its two/three named branches — never collapse to one (AC03 "selectable"). */
export function hasEveryNamedOutcome<T extends string>(options: readonly ScenarioOption<T>[], expected: readonly T[]): boolean {
  const values = new Set(options.map((option) => option.value));
  return expected.every((value) => values.has(value));
}
