import type { ModulePort, PrimaryActionOutcome } from '../../lib/module-port';

/**
 * Biometrics module port (BIO-UX-005-AC01, extended by BIO-UX-008).
 *
 * `mock.ts`/`live.ts` in this directory were the only files this interface's
 * own header comment (see git history) invited a domain story to touch to
 * add real methods — this is that story. `primaryAction` (inherited from
 * `ModulePort`) is kept working and unchanged: `/enrolment`'s still-shell
 * `PlaceholderPage` (BIO-UX-006's own scope, not this story's) resolves
 * `BiometricService` generically through it via `MODULE_PORTS`, and this
 * story must not break that route. The seven methods below back the real
 * `/fingerprints` and `/facial-recognition` screens this story ships
 * instead. All seven resolve together as one object via the same
 * `NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE` switch `primaryAction`
 * already used (BIO-UX-008-AC05) — a domain story is free to split any one
 * of them onto its own independently-switchable port later the way
 * Reporting's `viewAuditTrail` did, but nothing in this story's five ACs
 * asks for that yet, and `web/lib/feature-mode.ts`/`module-registry.ts`
 * are BIO-UX-005-owned shared contracts this story does not touch.
 */

/** The two simulated capture devices this module's screens connect (BIO-UX-008-AC03). */
export type BiometricDeviceKind = 'fingerprint_scanner' | 'face_camera';

export interface DeviceConnectionResult {
  outcome: PrimaryActionOutcome;
  message: string;
}

/** The ten standard tenprint finger positions (BIO-UX-008-AC01). */
export type FingerPosition =
  | 'right_thumb'
  | 'right_index'
  | 'right_middle'
  | 'right_ring'
  | 'right_little'
  | 'left_thumb'
  | 'left_index'
  | 'left_middle'
  | 'left_ring'
  | 'left_little';

export const TENPRINT_FINGER_POSITIONS: readonly FingerPosition[] = Object.freeze([
  'right_thumb',
  'right_index',
  'right_middle',
  'right_ring',
  'right_little',
  'left_thumb',
  'left_index',
  'left_middle',
  'left_ring',
  'left_little',
]);

export const FINGER_LABELS: Readonly<Record<FingerPosition, string>> = Object.freeze({
  right_thumb: 'Right thumb',
  right_index: 'Right index',
  right_middle: 'Right middle',
  right_ring: 'Right ring',
  right_little: 'Right little',
  left_thumb: 'Left thumb',
  left_index: 'Left index',
  left_middle: 'Left middle',
  left_ring: 'Left ring',
  left_little: 'Left little',
});

/**
 * A quality/capture outcome hint an operator can pick explicitly
 * (BIO-UX-008-AC03: "quality scores ... and low-quality outcomes are
 * selectable") rather than only ever reachable by chance through repeated
 * clicks — every named branch this story's screens render is reproducible
 * on demand for review. `undefined` (not passed) keeps the deterministic
 * cycling behaviour every other BIOLOG mock adapter already uses.
 */
export type CaptureScenarioHint = 'good' | 'low' | 'exception';
/** Face capture has no "exception" branch in this story's AC02 — only fingerprint capture does (AC01). */
export type FaceCaptureScenarioHint = 'good' | 'low';
export type VerificationScenarioHint = 'match' | 'no_match';
export type IdentificationScenarioHint = 'candidates_found' | 'no_candidates';
export type LivenessScenarioHint = 'live' | 'spoof_suspected';
export type DeviceScenarioHint = 'connected' | 'offline';

export type QualityOutcome = 'good' | 'low';

export interface CaptureQualityResult {
  /** 0-100 synthetic score — never derived from a real sensor reading (BIO-UX-008-AC04). */
  score: number;
  outcome: QualityOutcome;
  message: string;
}

export interface FingerprintCaptureResult extends CaptureQualityResult {
  finger: FingerPosition;
  /**
   * Present only for a genuine capture EXCEPTION (e.g. a bandaged or
   * missing finger) — a distinct outcome from a routine low-quality
   * retake, per this story's own AC01 wording ("quality, exception and
   * verification screens").
   */
  exceptionReason?: string;
}

export type MatchOutcome = 'match' | 'no_match';

export interface VerificationResult {
  outcome: MatchOutcome;
  score: number;
  message: string;
}

export type FaceCaptureResult = CaptureQualityResult;

export type LivenessOutcome = 'live' | 'spoof_suspected';

export interface LivenessResult {
  outcome: LivenessOutcome;
  message: string;
  /**
   * Always `true` (BIO-UX-008-AC02, AC04): this checks a scripted
   * placeholder script, never a real liveness engine — the story's own
   * user story calls this out by name as a "placeholder state".
   */
  simulated: true;
}

export interface FaceIdentificationCandidate {
  candidateId: string;
  displayName: string;
  /** 0-100 synthetic similarity score, ranked descending (mirrors BIO-BIO-007's own shape). */
  score: number;
}

export interface FaceIdentificationResult {
  candidates: readonly FaceIdentificationCandidate[];
  message: string;
}

/**
 * BIO-BIO-009-AC01/AC03 wire categories - the exact strings
 * `Biolog.Api.Modules.BioBio009Module.TryParseCategory` accepts.
 */
export type RetentionCategory = 'fingerprint_template' | 'face_template' | 'raw_image';

export interface RetentionPolicyResult {
  category: string;
  retentionDays: number;
}

/**
 * BIO-BIO-009-AC02/AC04 wire record kinds - the exact strings
 * `Biolog.Api.Modules.BioBio009Module.TryParseKind` accepts.
 */
export type BiometricRecordKind = 'fingerprint_session' | 'face_reference';

/** Mirrors `Biolog.Features.BiometricRetention.Domain.DeletionOutcome`'s three members exactly. */
export type DeletionOutcomeWire = 'Deleted' | 'NotFound' | 'DeletedPendingGalleryCleanup';

export interface BiometricDeletionResult {
  outcome: DeletionOutcomeWire;
  galleryPropagated: boolean | null;
}

export interface BiometricsPort extends ModulePort {
  connectDevice(kind: BiometricDeviceKind, hint?: DeviceScenarioHint): Promise<DeviceConnectionResult>;
  captureFingerprint(finger: FingerPosition, hint?: CaptureScenarioHint): Promise<FingerprintCaptureResult>;
  verifyFingerprint(finger: FingerPosition, hint?: VerificationScenarioHint): Promise<VerificationResult>;
  captureFace(hint?: FaceCaptureScenarioHint): Promise<FaceCaptureResult>;
  checkLiveness(hint?: LivenessScenarioHint): Promise<LivenessResult>;
  verifyFace(hint?: VerificationScenarioHint): Promise<VerificationResult>;
  identifyFace(hint?: IdentificationScenarioHint): Promise<FaceIdentificationResult>;
  /** BIO-BIO-009-AC01/AC05: connects to the real `POST /api/v1/biometrics/retention/policy`. */
  manageRetentionPolicy(category: RetentionCategory, retentionDays: number): Promise<RetentionPolicyResult>;
  /** BIO-BIO-009-AC02/AC05: connects to the real `POST /api/v1/biometrics/retention/delete`. */
  requestBiometricDeletion(kind: BiometricRecordKind, recordId: string, reason: string): Promise<BiometricDeletionResult>;
}
