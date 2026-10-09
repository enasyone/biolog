import { createMockAdapter } from '../../lib/module-port';
import { runEvidenceScenario, type EvidenceStepId, type EvidenceStepResult } from './evidence';
import type { DocumentsPort } from './ports';

/**
 * Document Verify mock adapter (BIO-UX-005-AC05, extended BIO-UX-009-AC01/AC04).
 *
 * `primaryAction` reuses the generic three-outcome scenario cycle
 * (unchanged from BIO-UX-005) for any caller that only knows the shared
 * shell shape. The eight step methods each get their OWN call counter and
 * deterministic scenario table (`evidence.ts`'s `SCENARIOS`) so every step
 * cycles independently — running MRZ five times does not disturb OCR's own
 * first-call scenario, matching `createMockAdapter`'s own per-instance
 * counter reasoning.
 *
 * `buildDocumentsMockPort` is a factory, not a singleton export, because
 * `feature-mode.ts`'s `MODULE_PORT_KEYS.DocumentVerifyService` now has TWO
 * independent port keys (`primaryAction`, `authenticity` — BIO-DOC-009-AC06).
 * `module-registry.ts`'s own test (`bio-ux-005-module-registry.test.ts`,
 * "resolves a distinct ModulePort instance for every key") requires each
 * key's mock adapter to be its own instance with its own call counters, the
 * same reason `web/features/reporting/mock.ts` already builds two full
 * `createMockAdapter` instances rather than reusing one.
 */
const SIMULATED_LATENCY_MS = 250;

function buildDocumentsMockPort(): DocumentsPort {
  const base = createMockAdapter('DocumentVerifyService', {
    scenarios: [
      { outcome: 'success', message: 'Document Verify: simulated action completed.' },
      { outcome: 'failure', message: 'Document Verify: simulated action failed — try again.' },
      { outcome: 'offline', message: 'Document Verify: device or network appears offline (simulated).' },
    ],
  });

  /** One independent, deterministic call counter per step (see this file's own header comment). */
  function makeStepAction(step: EvidenceStepId): () => Promise<EvidenceStepResult> {
    let callCount = 0;
    return async () => {
      callCount += 1;
      await new Promise((resolve) => setTimeout(resolve, SIMULATED_LATENCY_MS));
      return runEvidenceScenario(step, callCount);
    };
  }

  return {
    ...base,
    capture: makeStepAction('capture'),
    runMrz: makeStepAction('mrz'),
    runBarcode: makeStepAction('barcode'),
    runOcr: makeStepAction('ocr'),
    identifyDocument: makeStepAction('document-identification'),
    runNfc: makeStepAction('nfc'),
    checkAuthenticity: makeStepAction('authenticity'),
    compareSelfie: makeStepAction('selfie-comparison'),
  };
}

export const documentsMockPort: DocumentsPort = buildDocumentsMockPort();

/** Backs the independent `authenticity` port key (BIO-DOC-009-AC06) — its own call counters,
 * never sharing state with {@link documentsMockPort}. */
export const documentsAuthenticityMockPort: DocumentsPort = buildDocumentsMockPort();

/** Backs the independent `documentIdentification` port key (BIO-DOC-010-AC05) — its own call
 * counters, never sharing state with {@link documentsMockPort}/{@link documentsAuthenticityMockPort}. */
export const documentsDocumentIdentificationMockPort: DocumentsPort = buildDocumentsMockPort();
