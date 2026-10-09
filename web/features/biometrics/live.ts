import { createLiveAdapter } from '../../lib/module-port';
import type {
  BiometricDeletionResult,
  BiometricDeviceKind,
  BiometricRecordKind,
  BiometricsPort,
  CaptureScenarioHint,
  DeviceConnectionResult,
  DeviceScenarioHint,
  FaceCaptureResult,
  FaceCaptureScenarioHint,
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

/**
 * Live adapters (BIO-UX-005-AC04, extended BIO-UX-008-AC05). Each genuinely
 * POSTs to the real backend endpoint a future domain story wires up; none
 * exists yet, so every call here genuinely throws today — the same honest
 * "correct behaviour for an unconnected LIVE mode, not a bug" `createLiveAdapter`
 * already documents for `primaryAction`. A domain-specific result (e.g.
 * `VerificationResult.outcome: 'match' | 'no_match'`) has no true "offline"
 * member to smuggle a network failure into, so an integration failure here
 * is a genuine caller/integration error (thrown), never force-fit into a
 * routine business outcome — the same evidence/decision discipline this
 * author's own BIO-BIO backend stories use (typed outcome vs. exception).
 */
async function callLive<T>(endpointPath: string, body: unknown): Promise<T> {
  let response: Response;
  try {
    response = await fetch(endpointPath, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  } catch {
    throw new Error(`No live service reachable at ${endpointPath} yet — this action has not been connected.`);
  }
  if (!response.ok) {
    throw new Error(`Live request to ${endpointPath} failed: HTTP ${response.status}`);
  }
  return (await response.json()) as T;
}

async function connectDevice(kind: BiometricDeviceKind, hint?: DeviceScenarioHint): Promise<DeviceConnectionResult> {
  return callLive('/api/v1/biometrics/connect-device', { kind, hint });
}

async function captureFingerprint(finger: FingerPosition, hint?: CaptureScenarioHint): Promise<FingerprintCaptureResult> {
  return callLive('/api/v1/biometrics/fingerprint/capture', { finger, hint });
}

async function verifyFingerprint(finger: FingerPosition, hint?: VerificationScenarioHint): Promise<VerificationResult> {
  return callLive('/api/v1/biometrics/fingerprint/verify', { finger, hint });
}

async function captureFace(hint?: FaceCaptureScenarioHint): Promise<FaceCaptureResult> {
  return callLive('/api/v1/biometrics/face/capture', { hint });
}

async function checkLiveness(hint?: LivenessScenarioHint): Promise<LivenessResult> {
  return callLive('/api/v1/biometrics/face/liveness', { hint });
}

async function verifyFace(hint?: VerificationScenarioHint): Promise<VerificationResult> {
  return callLive('/api/v1/biometrics/face/verify', { hint });
}

async function identifyFace(hint?: IdentificationScenarioHint): Promise<FaceIdentificationResult> {
  return callLive('/api/v1/biometrics/face/identify', { hint });
}

/** BIO-BIO-009-AC01/AC05: genuinely POSTs to the real retention-policy endpoint this story's
 * backend ships (`Biolog.Api.Modules.BioBio009Module`). */
async function manageRetentionPolicy(category: RetentionCategory, retentionDays: number): Promise<RetentionPolicyResult> {
  return callLive('/api/v1/biometrics/retention/policy', { category, retentionDays });
}

/** BIO-BIO-009-AC02/AC05: genuinely POSTs to the real audited-deletion endpoint. */
async function requestBiometricDeletion(kind: BiometricRecordKind, recordId: string, reason: string): Promise<BiometricDeletionResult> {
  return callLive('/api/v1/biometrics/retention/delete', { kind, recordId, reason });
}

const generic = createLiveAdapter('BiometricService', '/api/v1/biometrics/primary-action');

export const biometricsLivePort: BiometricsPort = {
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
