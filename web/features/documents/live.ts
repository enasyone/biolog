import { createLiveAdapter } from '../../lib/module-port';
import { isEvidenceCheckState, type EvidenceProvenance, type EvidenceStepId, type EvidenceStepResult } from './evidence';
import type { DocumentsPort } from './ports';

/**
 * Document Verify live adapter (BIO-UX-005-AC04, extended BIO-UX-009-AC05
 * "each page exposes stable ports for later live engines").
 *
 * BIO-OPS-005 (Kemi's ruling): every one of the eight steps now has a real, reachable
 * `Biolog.Api` endpoint under `POST /api/v1/documents/*`, mapped by the Document Verify
 * coordinator (`api/features/bio-ops-005`, `Modules/BioOps005Module.cs`) and reached through
 * `next.config.mjs`'s own `/api/v1/documents/*` rewrite to the API host - the same route family
 * `authenticity` (BIO-DOC-009) and `document-identification` (BIO-DOC-010) already used. `mrz`,
 * `barcode`, `ocr`, `nfc` and `capture` are no longer unconnected placeholders either: the
 * coordinator composes BIO-DOC-002/003/004/007's own real engines against a deterministic
 * synthetic document (see that story's own evidence for the disclosed raster/NFC-hardware
 * boundary). `selfie-comparison` stays a deliberate, permanent simulation
 * (`provenance.source: 'synthetic-fixture'` below) - BIO-UX-009-AC04 names "no real biometric
 * data" as this capability's own approved design, not a gap.
 *
 * `makeLiveStepAction` still fails closed to `offline` on any network/parse error (see
 * `createLiveAdapter`'s own comment) - the correct behaviour for a genuinely unreachable host,
 * not a bug. The request includes `credentials: 'same-origin'` so the `document_session_id`
 * cookie `capture`'s own response sets is sent back on every later step automatically.
 */
function buildDocumentsLivePort(): DocumentsPort {
  const base = createLiveAdapter('DocumentVerifyService', '/api/v1/documents/primary-action');
  return {
    ...base,
    capture: makeLiveStepAction('capture'),
    runMrz: makeLiveStepAction('mrz'),
    runBarcode: makeLiveStepAction('barcode'),
    runOcr: makeLiveStepAction('ocr'),
    identifyDocument: makeLiveStepAction('document-identification'),
    runNfc: makeLiveStepAction('nfc'),
    checkAuthenticity: makeLiveStepAction('authenticity'),
    compareSelfie: makeLiveStepAction('selfie-comparison'),
  };
}

const LIVE_ENDPOINTS: Readonly<Record<EvidenceStepId, string>> = Object.freeze({
  capture: '/api/v1/documents/capture',
  mrz: '/api/v1/documents/mrz',
  barcode: '/api/v1/documents/barcode',
  ocr: '/api/v1/documents/ocr',
  'document-identification': '/api/v1/documents/identify',
  nfc: '/api/v1/documents/nfc',
  authenticity: '/api/v1/documents/authenticity',
  'selfie-comparison': '/api/v1/documents/selfie-comparison',
});

/**
 * BIO-OPS-005 (Kemi's ruling, point 7): `confidence` is REQUIRED (dropped the `?`) and
 * `provenance` is REQUIRED - a live response that omits either is a backend bug, not a shape this
 * adapter silently tolerates by falling back to a default. `message`/`state` stay optional on the
 * WIRE body (a sensible fallback already existed for both), but `confidence`/`provenance` do not,
 * matching `EvidenceScenario`'s own now-required fields.
 */
interface LiveStepBody {
  message?: string;
  state?: string;
  confidence: number;
  provenance: EvidenceProvenance;
}

function makeLiveStepAction(step: EvidenceStepId): () => Promise<EvidenceStepResult> {
  const endpointPath = LIVE_ENDPOINTS[step];
  return async () => {
    try {
      const response = await fetch(endpointPath, { method: 'POST', credentials: 'same-origin' });
      if (!response.ok) {
        return {
          step,
          outcome: 'failure',
          state: 'failed',
          message: `Live request failed: HTTP ${response.status}`,
          confidence: 0,
          provenance: { source: 'unavailable' },
        };
      }
      const body = (await response.json()) as LiveStepBody;
      return {
        step,
        outcome: 'success',
        state: isEvidenceCheckState(body.state) ? body.state : 'read',
        message: body.message ?? 'Live action completed.',
        confidence: body.confidence,
        provenance: body.provenance,
      };
    } catch {
      return {
        step,
        outcome: 'offline',
        state: 'not-checked',
        message: `No live service reachable at ${endpointPath} yet — this module has not been connected.`,
        confidence: 0,
        provenance: { source: 'unavailable' },
      };
    }
  };
}

export const documentsLivePort: DocumentsPort = buildDocumentsLivePort();

/** Backs the independent `authenticity` port key (BIO-DOC-009-AC06) — a distinct instance from
 * {@link documentsLivePort}, matching `documentsAuthenticityMockPort`'s own reasoning. */
export const documentsAuthenticityLivePort: DocumentsPort = buildDocumentsLivePort();

/** Backs the independent `documentIdentification` port key (BIO-DOC-010-AC05) — a distinct
 * instance from {@link documentsLivePort}/{@link documentsAuthenticityLivePort}. */
export const documentsDocumentIdentificationLivePort: DocumentsPort = buildDocumentsLivePort();
