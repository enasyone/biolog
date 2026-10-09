import { createMockAdapter } from '../../lib/module-port';
import type {
  PhotoQualityCapture,
  PhotoQualityDecisionResult,
  PhotoQualityListResult,
  PhotoQualitySubmitResult,
  PhotosPort,
} from './ports';

/**
 * Photos mock adapter (BIO-UX-005-AC05, extended BIO-PHO-003-AC05). `primaryAction` keeps the
 * original deterministic success/failure/offline cycle for `/enrolment`'s own wizard step. The
 * methods below deterministically cycle print-quality/biometric-suitability pass/fail
 * combinations (BIO-PHO-003-AC01: every combination must be reachable, not just "both pass"),
 * keyed by a per-port, in-memory call counter - the same "no Math.random(), reproducible for
 * tests and demos" discipline `createMockAdapter` itself documents.
 */
const SCENARIOS: readonly { print: boolean; biometric: boolean; printReason: string; biometricReason: string }[] = [
  { print: true, biometric: true, printReason: '', biometricReason: '' },
  { print: true, biometric: false, printReason: '', biometricReason: 'lighting_check_failed: recapture under even, diffuse lighting (SIMULATED).' },
  { print: false, biometric: true, printReason: 'embedded DPI 150 is below the required minimum 300 (SIMULATED).', biometricReason: '' },
  { print: false, biometric: false, printReason: 'photo is 400x500px, below the required minimum 1050x1350px (SIMULATED).', biometricReason: 'no_face_detected: position your face in the frame and recapture (SIMULATED).' },
];

let callCount = 0;
const capturesByPerson = new Map<string, PhotoQualityCapture[]>();
let nextMasterVersion = 1;

function captureFor(personId: string, sequenceNumber: number): PhotoQualityCapture | undefined {
  return capturesByPerson.get(personId)?.find((c) => c.sequenceNumber === sequenceNumber);
}

async function submitCapture(personId: string, file: File): Promise<PhotoQualitySubmitResult> {
  await new Promise((resolve) => setTimeout(resolve, 250));
  const scenario = SCENARIOS[callCount % SCENARIOS.length]!;
  callCount += 1;

  const existing = capturesByPerson.get(personId) ?? [];
  const capture: PhotoQualityCapture = {
    personId,
    sequenceNumber: existing.length + 1,
    format: file.type.includes('png') ? 'png' : 'jpeg',
    widthPx: scenario.print ? 1050 : 400,
    heightPx: scenario.print ? 1350 : 500,
    nativeDpi: scenario.print ? 300 : 150,
    printQuality: { passed: scenario.print, reasons: scenario.printReason ? [scenario.printReason] : [] },
    biometricSuitability: { passed: scenario.biometric, reasons: scenario.biometricReason ? [scenario.biometricReason] : [] },
    status: 'Pending',
    decisionReason: null,
    promotedMasterVersion: null,
    version: 1,
    submittedAt: new Date().toISOString(),
    decidedAt: null,
  };

  capturesByPerson.set(personId, [...existing, capture]);
  return { outcome: 'success', message: 'Photo submitted for review (SIMULATED).', capture };
}

async function listCaptures(personId: string): Promise<PhotoQualityListResult> {
  await new Promise((resolve) => setTimeout(resolve, 150));
  return { outcome: 'success', message: 'Captures loaded (SIMULATED).', captures: capturesByPerson.get(personId) ?? [] };
}

async function decide(action: 'approve' | 'reject', personId: string, sequenceNumber: number, expectedVersion: number, reason?: string): Promise<PhotoQualityDecisionResult> {
  await new Promise((resolve) => setTimeout(resolve, 150));
  const current = captureFor(personId, sequenceNumber);
  if (!current) {
    return { outcome: 'failure', message: `Decision failed: capture not found: ${personId}/${sequenceNumber} (SIMULATED).` };
  }
  if (current.status !== 'Pending') {
    return { outcome: 'failure', message: `Decision failed: only a PENDING capture may be approved or rejected: ${personId}/${sequenceNumber} is ${current.status} (SIMULATED).` };
  }
  if (current.version !== expectedVersion) {
    return { outcome: 'failure', message: 'Decision failed: the capture was modified since the version this request expected (SIMULATED).' };
  }

  const updated: PhotoQualityCapture = {
    ...current,
    status: action === 'approve' ? 'Approved' : 'Rejected',
    decisionReason: action === 'reject' ? (reason ?? null) : null,
    promotedMasterVersion: action === 'approve' ? nextMasterVersion++ : null,
    version: current.version + 1,
    decidedAt: new Date().toISOString(),
  };

  const list = capturesByPerson.get(personId) ?? [];
  capturesByPerson.set(personId, list.map((c) => (c.sequenceNumber === sequenceNumber ? updated : c)));
  return { outcome: 'success', message: `Capture ${action === 'approve' ? 'approved' : 'rejected'} (SIMULATED).`, capture: updated };
}

export const photosMockPort: PhotosPort = {
  ...createMockAdapter('PhotoService', {
    scenarios: [
      { outcome: 'success', message: 'Photos: simulated action completed.' },
      { outcome: 'failure', message: 'Photos: simulated action failed — try again.' },
      { outcome: 'offline', message: 'Photos: device or network appears offline (simulated).' },
    ],
  }),
  submitCapture,
  listCaptures,
  approveCapture: (personId, sequenceNumber, expectedVersion) => decide('approve', personId, sequenceNumber, expectedVersion),
  rejectCapture: (personId, sequenceNumber, reason, expectedVersion) => decide('reject', personId, sequenceNumber, expectedVersion, reason),
};
