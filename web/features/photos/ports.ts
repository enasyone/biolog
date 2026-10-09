import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * Photos module port (BIO-UX-005-AC01, extended BIO-PHO-003-AC05). `primaryAction` is the
 * original generic shape (BIO-UX-006's own enrolment wizard still calls it for its "Photo"
 * step - see that component's own header for why it stays generic there, a disclosed HYBRID
 * boundary: the wizard does not create a real person until its final "review" step, so no real
 * `personId` exists yet when the photo step itself runs). The methods below are this story's
 * own real surface, used by `/photographs`'s real review screen.
 */
export interface PhotosPort extends ModulePort {
  submitCapture(personId: string, file: File): Promise<PhotoQualitySubmitResult>;
  listCaptures(personId: string): Promise<PhotoQualityListResult>;
  approveCapture(personId: string, sequenceNumber: number, expectedVersion: number): Promise<PhotoQualityDecisionResult>;
  rejectCapture(personId: string, sequenceNumber: number, reason: string, expectedVersion: number): Promise<PhotoQualityDecisionResult>;
}

export type PhotoQualityCaptureStatus = 'Pending' | 'Rejected' | 'Approved';

export interface QualityVerdict {
  passed: boolean;
  reasons: readonly string[];
}

/**
 * BIO-PHO-003-AC01: two independent verdicts, always both present - either may pass while the
 * other fails. BIO-PHO-003-AC02: `reasons` is never hidden, even for a verdict that did not end
 * up being the reason a capture was eventually decided.
 */
export interface PhotoQualityCapture {
  personId: string;
  sequenceNumber: number;
  format: string;
  widthPx: number;
  heightPx: number;
  nativeDpi: number | null;
  printQuality: QualityVerdict;
  biometricSuitability: QualityVerdict;
  status: PhotoQualityCaptureStatus;
  decisionReason: string | null;
  promotedMasterVersion: number | null;
  version: number;
  submittedAt: string;
  decidedAt: string | null;
}

export interface PhotoQualitySubmitResult extends PrimaryActionResult {
  capture?: PhotoQualityCapture;
}

export interface PhotoQualityListResult extends PrimaryActionResult {
  captures?: readonly PhotoQualityCapture[];
}

export interface PhotoQualityDecisionResult extends PrimaryActionResult {
  capture?: PhotoQualityCapture;
}
