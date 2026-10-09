import { createLiveAdapter } from '../../lib/module-port';
import type {
  PhotoQualityCapture,
  PhotoQualityDecisionResult,
  PhotoQualityListResult,
  PhotoQualitySubmitResult,
  PhotosPort,
  QualityVerdict,
} from './ports';

const BASE_PATH = '/api/v1/photo-quality/captures';

/**
 * No authentication story has landed in the frontend yet (BIO-INT-001 is W12) - the same
 * placeholder actor every other live port in this epic sends (BIO-PHO-003 defines no
 * permission/provenance gate at all - see api/features/bio-pho-003/Api/ActorResolver.cs's own
 * header - so, unlike BIO-RPT-004, there is no further disclosed consequence here).
 */
const ACTOR_HEADERS = { 'X-Actor-Id': 'web-ui' } as const;

async function problemMessage(response: Response, fallback: string): Promise<string> {
  try {
    const problem = (await response.json()) as { message?: string };
    return problem.message ?? fallback;
  } catch {
    return fallback;
  }
}

function mapVerdict(raw: Record<string, unknown>): QualityVerdict {
  return { passed: Boolean(raw.passed), reasons: (raw.reasons as string[] | undefined) ?? [] };
}

function mapCapture(raw: Record<string, unknown>): PhotoQualityCapture {
  return {
    personId: String(raw.person_id),
    sequenceNumber: Number(raw.sequence_number),
    format: String(raw.format),
    widthPx: Number(raw.width_px),
    heightPx: Number(raw.height_px),
    nativeDpi: raw.native_dpi === null || raw.native_dpi === undefined ? null : Number(raw.native_dpi),
    printQuality: mapVerdict(raw.print_quality as Record<string, unknown>),
    biometricSuitability: mapVerdict(raw.biometric_suitability as Record<string, unknown>),
    status: raw.status as PhotoQualityCapture['status'],
    decisionReason: raw.decision_reason === undefined ? null : (raw.decision_reason as string | null),
    promotedMasterVersion: raw.promoted_master_version === null || raw.promoted_master_version === undefined ? null : Number(raw.promoted_master_version),
    version: Number(raw.version),
    submittedAt: String(raw.submitted_at),
    decidedAt: raw.decided_at === undefined ? null : (raw.decided_at as string | null),
  };
}

async function submitCapture(personId: string, file: File): Promise<PhotoQualitySubmitResult> {
  try {
    const form = new FormData();
    form.set('person_id', personId);
    form.set('file', file);
    const response = await fetch(BASE_PATH, { method: 'POST', headers: ACTOR_HEADERS, body: form });
    if (!response.ok) {
      return { outcome: 'failure', message: `Photo submission failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
    }
    const body = (await response.json()) as Record<string, unknown>;
    return { outcome: 'success', message: 'Photo submitted for review (LIVE).', capture: mapCapture(body) };
  } catch {
    return { outcome: 'offline', message: `No live photo-quality service reachable at ${BASE_PATH} yet.` };
  }
}

async function listCaptures(personId: string): Promise<PhotoQualityListResult> {
  try {
    const response = await fetch(`${BASE_PATH}/${encodeURIComponent(personId)}`, { headers: ACTOR_HEADERS });
    if (!response.ok) {
      return { outcome: 'failure', message: `Listing captures failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
    }
    const body = (await response.json()) as { data: Record<string, unknown>[] };
    return { outcome: 'success', message: 'Captures loaded (LIVE).', captures: body.data.map(mapCapture) };
  } catch {
    return { outcome: 'offline', message: `No live photo-quality service reachable at ${BASE_PATH} yet.` };
  }
}

async function decide(
  action: 'approve' | 'reject', personId: string, sequenceNumber: number, expectedVersion: number, reason?: string,
): Promise<PhotoQualityDecisionResult> {
  try {
    const response = await fetch(`${BASE_PATH}/${encodeURIComponent(personId)}/${sequenceNumber}/${action}`, {
      method: 'POST',
      headers: { ...ACTOR_HEADERS, 'Content-Type': 'application/json' },
      body: JSON.stringify(reason === undefined ? { expected_version: expectedVersion } : { reason, expected_version: expectedVersion }),
    });
    if (!response.ok) {
      return { outcome: 'failure', message: `Decision failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
    }
    const body = (await response.json()) as Record<string, unknown>;
    return { outcome: 'success', message: `Capture ${action === 'approve' ? 'approved' : 'rejected'} (LIVE).`, capture: mapCapture(body) };
  } catch {
    return { outcome: 'offline', message: `No live photo-quality service reachable at ${BASE_PATH} yet.` };
  }
}

/**
 * BIO-PHO-003-AC05's real surface. `primaryAction` keeps the original generic shape
 * (`createLiveAdapter`'s own documented fail-closed-to-offline behaviour) for
 * `/enrolment`'s own wizard step - see `ports.ts`'s header for why that step cannot pass a real
 * `personId` yet.
 */
export const photosLivePort: PhotosPort = {
  ...createLiveAdapter('PhotoService', `${BASE_PATH}/primary-action`),
  submitCapture,
  listCaptures,
  approveCapture: (personId, sequenceNumber, expectedVersion) => decide('approve', personId, sequenceNumber, expectedVersion),
  rejectCapture: (personId, sequenceNumber, reason, expectedVersion) => decide('reject', personId, sequenceNumber, expectedVersion, reason),
};
