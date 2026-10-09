import { createMockAdapter } from '../../lib/module-port';
import type {
  BiometricDeletionResult,
  BiometricDeviceKind,
  BiometricRecordKind,
  BiometricsPort,
  CaptureScenarioHint,
  DeletionOutcomeWire,
  DeviceConnectionResult,
  DeviceScenarioHint,
  FaceCaptureResult,
  FaceCaptureScenarioHint,
  FaceIdentificationCandidate,
  FaceIdentificationResult,
  FingerPosition,
  FingerprintCaptureResult,
  IdentificationScenarioHint,
  LivenessResult,
  LivenessScenarioHint,
  RetentionCategory,
  RetentionPolicyResult,
  VerificationResult,
  VerificationScenarioHint,
} from './ports';

const SIMULATED_LATENCY_MS = 250;

/**
 * Same small deterministic pseudo-random cycle `../../lib/module-port.ts`'s
 * own `createMockAdapter` uses internally (not exported there) — repeated
 * runs/tests stay stable, no `Math.random()` (BIO-UX-005-AC05 "reproducible").
 */
function deterministicIndex(seed: string, length: number): number {
  let hash = 0;
  for (let i = 0; i < seed.length; i += 1) {
    hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
  }
  return hash % length;
}

/**
 * Per-seed call counter so e.g. each finger's own capture history cycles
 * independently of every other finger's (BIO-UX-008-AC01: ten independent
 * tenprint slots, not one shared counter that makes one finger's outcome
 * depend on capture order across unrelated fingers).
 */
const callCounts = new Map<string, number>();

function nextCallCount(seed: string): number {
  const count = (callCounts.get(seed) ?? 0) + 1;
  callCounts.set(seed, count);
  return count;
}

/** Picks a scenario deterministically by call count, or the caller's explicit hint when given (BIO-UX-008-AC03 "selectable"). */
function pickScenario<T extends string>(seed: string, order: readonly T[], hint?: T): T {
  if (hint !== undefined) return hint;
  const count = nextCallCount(seed);
  return order[deterministicIndex(`${seed}:${count}`, order.length)]!;
}

async function settle<T>(value: T): Promise<T> {
  await new Promise((resolve) => setTimeout(resolve, SIMULATED_LATENCY_MS));
  return value;
}

const CAPTURE_ORDER: readonly CaptureScenarioHint[] = ['good', 'good', 'low', 'exception'];
const FACE_CAPTURE_ORDER: readonly FaceCaptureScenarioHint[] = ['good', 'good', 'low'];
const VERIFY_ORDER: readonly VerificationScenarioHint[] = ['match', 'match', 'no_match'];
const IDENTIFY_ORDER: readonly IdentificationScenarioHint[] = ['candidates_found', 'candidates_found', 'no_candidates'];
const LIVENESS_ORDER: readonly LivenessScenarioHint[] = ['live', 'live', 'live', 'spoof_suspected'];
const DEVICE_ORDER: readonly DeviceScenarioHint[] = ['connected', 'connected', 'connected', 'offline'];
const DELETION_ORDER: readonly DeletionOutcomeWire[] = ['Deleted', 'Deleted', 'NotFound'];

const EXCEPTION_REASONS: readonly string[] = Object.freeze([
  'Finger reported bandaged — capture withheld (simulated exception).',
  'Sensor could not detect a finger present — capture withheld (simulated exception).',
]);

const CANDIDATE_NAMES: readonly string[] = Object.freeze([
  'Candidate A. Okafor',
  'Candidate B. Eze',
  'Candidate C. Bello',
]);

async function connectDevice(kind: BiometricDeviceKind, hint?: DeviceScenarioHint): Promise<DeviceConnectionResult> {
  const outcome = pickScenario(`connect:${kind}`, DEVICE_ORDER, hint);
  const deviceLabel = kind === 'fingerprint_scanner' ? 'Fingerprint scanner' : 'Face camera';
  return settle(
    outcome === 'connected'
      ? { outcome: 'success', message: `${deviceLabel} connected (SIMULATED — no real device attached).` }
      : { outcome: 'offline', message: `${deviceLabel} appears offline (SIMULATED).` },
  );
}

async function captureFingerprint(finger: FingerPosition, hint?: CaptureScenarioHint): Promise<FingerprintCaptureResult> {
  const outcome = pickScenario(`capture:${finger}`, CAPTURE_ORDER, hint);
  if (outcome === 'exception') {
    const reason = EXCEPTION_REASONS[deterministicIndex(finger, EXCEPTION_REASONS.length)]!;
    return settle({ finger, score: 0, outcome: 'low', exceptionReason: reason, message: `Capture exception (SIMULATED): ${reason}` });
  }
  const score = outcome === 'good' ? 84 : 39;
  return settle({
    finger,
    score,
    outcome,
    message:
      outcome === 'good'
        ? `Capture accepted — SIMULATED quality ${score}/100.`
        : `Capture accepted with LOW SIMULATED quality (${score}/100) — retake recommended.`,
  });
}

async function verifyFingerprint(finger: FingerPosition, hint?: VerificationScenarioHint): Promise<VerificationResult> {
  const outcome = pickScenario(`verify:${finger}`, VERIFY_ORDER, hint);
  const score = outcome === 'match' ? 91 : 22;
  return settle({
    outcome,
    score,
    message:
      outcome === 'match'
        ? `MATCH (SIMULATED) — similarity ${score}/100 against the enrolled reference.`
        : `NO MATCH (SIMULATED) — similarity ${score}/100, below the match threshold.`,
  });
}

async function captureFace(hint?: FaceCaptureScenarioHint): Promise<FaceCaptureResult> {
  const outcome = pickScenario('capture:face', FACE_CAPTURE_ORDER, hint);
  const score = outcome === 'good' ? 88 : 44;
  return settle({
    score,
    outcome,
    message:
      outcome === 'good'
        ? `Capture accepted — SIMULATED quality ${score}/100.`
        : `Capture accepted with LOW SIMULATED quality (${score}/100) — retake recommended.`,
  });
}

async function checkLiveness(hint?: LivenessScenarioHint): Promise<LivenessResult> {
  const outcome = pickScenario('liveness', LIVENESS_ORDER, hint);
  return settle({
    outcome,
    simulated: true,
    message:
      outcome === 'live'
        ? 'Liveness placeholder (SIMULATED): treated as a live subject — no real liveness engine runs yet.'
        : 'Liveness placeholder (SIMULATED): treated as a possible spoof — no real liveness engine runs yet.',
  });
}

async function verifyFace(hint?: VerificationScenarioHint): Promise<VerificationResult> {
  const outcome = pickScenario('verify:face', VERIFY_ORDER, hint);
  const score = outcome === 'match' ? 93 : 18;
  return settle({
    outcome,
    score,
    message:
      outcome === 'match'
        ? `MATCH (SIMULATED) — similarity ${score}/100 against the enrolled reference.`
        : `NO MATCH (SIMULATED) — similarity ${score}/100, below the match threshold.`,
  });
}

async function identifyFace(hint?: IdentificationScenarioHint): Promise<FaceIdentificationResult> {
  const outcome = pickScenario('identify:face', IDENTIFY_ORDER, hint);
  if (outcome === 'no_candidates') {
    return settle({ candidates: [], message: 'No SIMULATED candidates found in the searched population.' });
  }
  const candidates: FaceIdentificationCandidate[] = CANDIDATE_NAMES.map((displayName, index) => ({
    candidateId: `synthetic-candidate-${index + 1}`,
    displayName,
    score: 89 - index * 14,
  }));
  return settle({ candidates, message: `${candidates.length} SIMULATED candidate(s) ranked by similarity.` });
}

/** BIO-BIO-009-AC01: SIMULATED - applies the requested value deterministically, no real policy
 * store behind this adapter. */
async function manageRetentionPolicy(category: RetentionCategory, retentionDays: number): Promise<RetentionPolicyResult> {
  return settle({ category, retentionDays });
}

/** BIO-BIO-009-AC02: SIMULATED - cycles Deleted/NotFound deterministically per record id, same
 * `pickScenario` discipline as every other mock method here (AC03 "selectable" equivalent). */
async function requestBiometricDeletion(kind: BiometricRecordKind, recordId: string, reason: string): Promise<BiometricDeletionResult> {
  void reason;
  const outcome = pickScenario(`delete:${kind}:${recordId}`, DELETION_ORDER);
  return settle({ outcome, galleryPropagated: outcome === 'Deleted' ? true : null });
}

const generic = createMockAdapter('BiometricService', {
  scenarios: [
    { outcome: 'success', message: 'Biometrics: simulated action completed.' },
    { outcome: 'failure', message: 'Biometrics: simulated action failed — try again.' },
    { outcome: 'offline', message: 'Biometrics: device or network appears offline (simulated).' },
  ],
});

/**
 * Biometrics mock adapter (BIO-UX-005-AC05, extended BIO-UX-008-AC03/AC04).
 * Every method below is deterministic per its own seed and clearly labels
 * every message "SIMULATED" so no result can be mistaken for a real capture
 * (BIO-UX-008-AC04). `hint` makes every named outcome reachable on demand
 * for review rather than only by chance (AC03 "selectable").
 */
export const biometricsMockPort: BiometricsPort = {
  module: generic.module,
  mode: generic.mode,
  primaryAction: generic.primaryAction,
  connectDevice,
  captureFingerprint,
  verifyFingerprint,
  captureFace,
  checkLiveness,
  verifyFace,
  identifyFace,
  manageRetentionPolicy,
  requestBiometricDeletion,
};
