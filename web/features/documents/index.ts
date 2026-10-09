import { resolvePort } from '../../lib/feature-mode';
import { documentsAuthenticityMockPort, documentsDocumentIdentificationMockPort, documentsMockPort } from './mock';
import { documentsAuthenticityLivePort, documentsDocumentIdentificationLivePort, documentsLivePort } from './live';
import type { DocumentsPort } from './ports';

export type { DocumentsPort } from './ports';
export * from './evidence';

/**
 * The Document Verify module's two independently-resolved ports
 * (BIO-UX-005-AC01/AC02/AC04, extended BIO-DOC-009-AC06). Pages/components
 * import `documentsPort`, never `./mock` or `./live` directly.
 *
 * `documentsPrimaryPort` resolves via the `primaryAction` key (the other
 * seven steps, still SIMULATION-only — no backend exists for them yet).
 * `documentsAuthenticityPort` resolves independently via the `authenticity`
 * key: switching `NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_AUTHENTICITY`
 * to LIVE (leaving the module's own whole-module default alone) connects
 * ONLY the authenticity step to BIO-DOC-009's real backend — the SAME
 * independent-per-port mechanism `ReportingService` already established
 * (`feature-mode.ts`'s `MODULE_PORT_KEYS` comment), now extended to this
 * module because BIO-DOC-009 is the first of its eight steps with a real
 * endpoint to connect.
 *
 * `documentsDocumentIdentificationPort` resolves independently via the
 * `documentIdentification` key (BIO-DOC-010-AC05): switching
 * `NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_DOCUMENTIDENTIFICATION` to
 * LIVE connects only the `identifyDocument` step to BIO-DOC-010's real
 * governed-pack backend — kept independent from `primaryAction` precisely
 * because `identifyDocument` is gated on `capture` having already succeeded
 * (`DocumentVerifyApp.tsx`'s own `requiresCapture` rule): if this step
 * shared `primaryAction`'s mode, switching it LIVE would also switch
 * `capture` LIVE, and `capture` has no backend of its own yet, so it would
 * always fail and permanently block `identifyDocument` from ever running
 * (see `feature-mode.ts`'s own `MODULE_PORT_KEYS` comment).
 *
 * `documentsPort` merges all three into the single object
 * `DocumentVerifyApp.tsx` calls every step through (that page's own design
 * predates this split and still calls one object per step, unlike
 * Reporting's two separate UI call sites) — every method resolves from
 * `documentsPrimaryPort` except `checkAuthenticity` (always
 * `documentsAuthenticityPort`) and `identifyDocument` (always
 * `documentsDocumentIdentificationPort`), regardless of the module's
 * whole-module default.
 */
export const documentsPrimaryPort: DocumentsPort = resolvePort(documentsMockPort, documentsLivePort, 'primaryAction');
export const documentsAuthenticityPort: DocumentsPort = resolvePort(
  documentsAuthenticityMockPort,
  documentsAuthenticityLivePort,
  'authenticity',
);
export const documentsDocumentIdentificationPort: DocumentsPort = resolvePort(
  documentsDocumentIdentificationMockPort,
  documentsDocumentIdentificationLivePort,
  'documentIdentification',
);

export const documentsPort: DocumentsPort = {
  ...documentsPrimaryPort,
  checkAuthenticity: documentsAuthenticityPort.checkAuthenticity,
  identifyDocument: documentsDocumentIdentificationPort.identifyDocument,
};
