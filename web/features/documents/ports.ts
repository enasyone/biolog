import type { ModulePort } from '../../lib/module-port';
import type { EvidenceStepResult } from './evidence';

/**
 * Document Verify module port (BIO-UX-005-AC01, extended BIO-UX-009-AC01/AC05).
 *
 * `primaryAction` is kept unchanged (inherited from `ModulePort`) for any
 * generic caller that only knows about the shared shell shape (e.g. a
 * future Human Test Console). The real Document Verify journey this story
 * builds calls the eight named methods below instead, one per AC01/AC04
 * interactive area — every one of them returns an `EvidenceStepResult`
 * (a superset of `PrimaryActionResult`, so `DocumentsPort` still structurally
 * satisfies `ModulePort`), giving the UI the actual evidence STATE
 * (AC02's "read, structurally valid, consistent, authentic, failed,
 * not-checked, inconclusive" vocabulary) rather than just a generic
 * success/failure/offline outcome text.
 *
 * `mock.ts`/`live.ts` in this directory are the only files a later story
 * needs to touch to change what these calls actually do (BIO-UX-005-AC04) —
 * no page component depends on which adapter is resolved.
 */
export interface DocumentsPort extends ModulePort {
  capture(): Promise<EvidenceStepResult>;
  runMrz(): Promise<EvidenceStepResult>;
  runBarcode(): Promise<EvidenceStepResult>;
  runOcr(): Promise<EvidenceStepResult>;
  identifyDocument(): Promise<EvidenceStepResult>;
  runNfc(): Promise<EvidenceStepResult>;
  checkAuthenticity(): Promise<EvidenceStepResult>;
  compareSelfie(): Promise<EvidenceStepResult>;
}
