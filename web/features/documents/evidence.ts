/**
 * Document Verify evidence model (BIO-UX-009-AC01, AC02, AC03).
 *
 * `route-manifest.ts` names one route family, `/document-verify`, for the
 * whole journey; AC01 names seven distinct interactive areas within it
 * (capture/upload, MRZ, barcode, OCR, document identification, NFC,
 * authenticity) and AC04 adds an eighth (portrait-to-live-selfie
 * comparison). This module is the single source of truth for that journey's
 * steps and the evidence-state vocabulary AC02 names verbatim: "read,
 * structurally valid, consistent, authentic, failed, not-checked and
 * inconclusive" — every one of those seven states is reachable by at least
 * one step below, not just decorative labels (see `SCENARIOS`).
 *
 * Kept deliberately framework-agnostic (no React import) so it is testable
 * in plain `vitest` without a DOM, and reusable by both the mock and the
 * (future) live adapter.
 */
import type { PrimaryActionOutcome } from '../../lib/module-port';

export type EvidenceCheckState =
  | 'not-checked'
  | 'read'
  | 'structurally-valid'
  | 'consistent'
  | 'authentic'
  | 'failed'
  | 'inconclusive';

export const EVIDENCE_CHECK_STATES: readonly EvidenceCheckState[] = Object.freeze([
  'not-checked',
  'read',
  'structurally-valid',
  'consistent',
  'authentic',
  'failed',
  'inconclusive',
]);

export function isEvidenceCheckState(value: unknown): value is EvidenceCheckState {
  return typeof value === 'string' && (EVIDENCE_CHECK_STATES as readonly string[]).includes(value);
}

export type EvidenceStepId =
  | 'capture'
  | 'mrz'
  | 'barcode'
  | 'ocr'
  | 'document-identification'
  | 'nfc'
  | 'authenticity'
  | 'selfie-comparison';

export interface EvidenceStepMeta {
  id: EvidenceStepId;
  label: string;
  description: string;
  /** True when this step needs a successful `capture` before it can run (AC01: capture gates the rest). */
  requiresCapture: boolean;
}

/** Ordered per AC01's own listing, with `selfie-comparison` (AC04) appended last. */
export const EVIDENCE_STEPS: readonly EvidenceStepMeta[] = Object.freeze([
  Object.freeze({
    id: 'capture',
    label: 'Capture / Upload',
    description: 'Capture or upload the physical document image.',
    requiresCapture: false,
  }),
  Object.freeze({
    id: 'mrz',
    label: 'MRZ',
    description: 'Read and validate the Machine Readable Zone, if present.',
    requiresCapture: true,
  }),
  Object.freeze({
    id: 'barcode',
    label: 'Barcode',
    description: 'Decode and validate any 1D/2D barcode on the document.',
    requiresCapture: true,
  }),
  Object.freeze({
    id: 'ocr',
    label: 'OCR',
    description: 'Extract printed fields via optical character recognition.',
    requiresCapture: true,
  }),
  Object.freeze({
    id: 'document-identification',
    label: 'Document Identification',
    description: 'Classify the document type from the captured evidence.',
    requiresCapture: true,
  }),
  Object.freeze({
    id: 'nfc',
    label: 'NFC',
    description: 'Read the embedded chip and evaluate passive authentication.',
    requiresCapture: true,
  }),
  Object.freeze({
    id: 'authenticity',
    label: 'Authenticity / Evidence Comparison',
    description: 'Cross-compare every source above and flag conflicts.',
    requiresCapture: true,
  }),
  Object.freeze({
    id: 'selfie-comparison',
    label: 'Portrait vs. Live Selfie',
    description: 'Compare the document portrait against a simulated live selfie — no real biometric data (AC04).',
    requiresCapture: true,
  }),
]);

/**
 * BIO-OPS-005 (Kemi's ruling, point 7): the exact six provenance sources a step result can name -
 * `synthetic-fixture` for a step that is permanently/deliberately simulated (capture itself, and
 * `selfie-comparison` per BIO-UX-009-AC04's own "no real biometric data" design), `unavailable`
 * for NFC's own honest "not performed, no hardware transport exists" disclosure.
 */
export type EvidenceProvenanceSource = 'mrz' | 'barcode' | 'ocr' | 'nfc' | 'synthetic-fixture' | 'unavailable';

export interface EvidenceProvenance {
  source: EvidenceProvenanceSource;
  /** BIO-DOC-001's own DocumentEvidence.SequenceNumber - no new canonical id invented. */
  evidenceSequenceNumber?: number;
  /** BIO-DOC-001's own DocumentFieldEvidence.SequenceNumber values, one per field this step
   * actually recorded. */
  fieldEvidenceSequenceNumbers?: number[];
}

export interface EvidenceScenario {
  outcome: PrimaryActionOutcome;
  state: EvidenceCheckState;
  message: string;
  /** BIO-OPS-005 (Kemi's ruling, point 7): REQUIRED - every engine result carries a genuine
   * confidence now, never silently reduced to state/message alone (Seyi's SY-OPS005-01 finding). */
  confidence: number;
  /** BIO-OPS-005 (Kemi's ruling, point 7): REQUIRED provenance - which engine (or honest
   * non-engine disclosure) produced this result. */
  provenance: EvidenceProvenance;
}

export interface EvidenceStepResult extends EvidenceScenario {
  step: EvidenceStepId;
}

/**
 * Small, deterministic pseudo-random cycle — the same algorithm
 * `module-port.ts`'s own `createMockAdapter` uses internally, reimplemented
 * here (rather than imported) because it is a private, unexported helper
 * there, and this module needs its own per-step scenario tables (richer
 * than `createMockAdapter`'s generic success/failure/offline three-tuple)
 * to express AC02's seven-state vocabulary — not a reason to widen that
 * shared framework file's public surface for every other domain.
 */
function deterministicIndex(seed: string, length: number): number {
  let hash = 0;
  for (let i = 0; i < seed.length; i += 1) {
    hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
  }
  return hash % length;
}

/**
 * Deterministic scenario tables, one per step (BIO-UX-005-AC05 "reproduces
 * approved success, failure, loading, offline and boundary behaviours",
 * applied to this domain's own richer state vocabulary). Every step reaches
 * `failed` and `not-checked` (via an `offline` outcome); the specific
 * "positive" state differs per step because the seven states are not
 * interchangeable — a barcode is "structurally valid" (checksum), NFC is
 * "authentic" (passive authentication), document identification and
 * authenticity are "consistent" (agreement across sources).
 *
 * `ocr` and `document-identification`/`authenticity`/`nfc`/`selfie-comparison`
 * each include one `inconclusive` scenario — BIO-UX-009-AC03's "low-confidence
 * OCR and conflicting-source scenarios route to simulated human review" is
 * modelled uniformly as "any step landing in `inconclusive`" (see
 * `requiresHumanReview` below), so every one of those steps can exercise
 * that routing, not just OCR.
 */
// Each `provenance.source` below needs `as const` because `Object.freeze<T>`'s own generic
// inference widens an unannotated string literal to `string`, independent of any outer
// annotation - and `satisfies` (not an explicit `Readonly<Record<...>>` annotation) here so
// that widening doesn't also happen one level up, at this object's own property values.
const SCENARIOS = Object.freeze({
  capture: Object.freeze([
    Object.freeze({ outcome: 'success', state: 'read', message: 'Document captured — image quality acceptable.', confidence: 1, provenance: { source: 'synthetic-fixture' as const } }),
    Object.freeze({ outcome: 'failure', state: 'failed', message: 'Capture failed — image too blurred to process.', confidence: 0, provenance: { source: 'synthetic-fixture' as const } }),
    Object.freeze({ outcome: 'offline', state: 'not-checked', message: 'Capture device unreachable (simulated offline).', confidence: 0, provenance: { source: 'synthetic-fixture' as const } }),
  ]),
  mrz: Object.freeze([
    Object.freeze({ outcome: 'success', state: 'structurally-valid', message: 'MRZ read and check digits valid.', confidence: 0.98, provenance: { source: 'mrz' as const } }),
    Object.freeze({ outcome: 'success', state: 'read', message: 'MRZ read; structural validation still pending.', confidence: 0.7, provenance: { source: 'mrz' as const } }),
    Object.freeze({ outcome: 'failure', state: 'failed', message: 'MRZ check digit mismatch — likely a misread character.', confidence: 0, provenance: { source: 'mrz' as const } }),
    Object.freeze({ outcome: 'offline', state: 'not-checked', message: 'MRZ reader unreachable (simulated offline).', confidence: 0, provenance: { source: 'mrz' as const } }),
  ]),
  barcode: Object.freeze([
    Object.freeze({ outcome: 'success', state: 'structurally-valid', message: 'Barcode decoded and structurally valid.', confidence: 0.96, provenance: { source: 'barcode' as const } }),
    Object.freeze({ outcome: 'failure', state: 'failed', message: 'Barcode checksum failed.', confidence: 0, provenance: { source: 'barcode' as const } }),
    Object.freeze({ outcome: 'offline', state: 'not-checked', message: 'Barcode scanner unreachable (simulated offline).', confidence: 0, provenance: { source: 'barcode' as const } }),
  ]),
  ocr: Object.freeze([
    Object.freeze({ outcome: 'success', state: 'read', message: 'OCR text extracted at high confidence.', confidence: 0.94, provenance: { source: 'ocr' as const } }),
    Object.freeze({
      outcome: 'success',
      state: 'inconclusive',
      message: 'OCR confidence is below the review threshold — routed to human review.',
      confidence: 0.42,
      provenance: { source: 'ocr' as const },
    }),
    Object.freeze({ outcome: 'failure', state: 'failed', message: 'OCR failed — no legible text region found.', confidence: 0, provenance: { source: 'ocr' as const } }),
    Object.freeze({ outcome: 'offline', state: 'not-checked', message: 'OCR engine unreachable (simulated offline).', confidence: 0, provenance: { source: 'ocr' as const } }),
  ]),
  'document-identification': Object.freeze([
    Object.freeze({ outcome: 'success', state: 'consistent', message: 'Identified as National ID — consistent with capture.', confidence: 1, provenance: { source: 'ocr' as const } }),
    Object.freeze({
      outcome: 'success',
      state: 'inconclusive',
      message: 'Two candidate document types scored closely — routed to human review.',
      confidence: 0.5,
      provenance: { source: 'ocr' as const },
    }),
    Object.freeze({ outcome: 'failure', state: 'failed', message: 'No known document template matched.', confidence: 1, provenance: { source: 'ocr' as const } }),
    Object.freeze({ outcome: 'offline', state: 'not-checked', message: 'Classifier unreachable (simulated offline).', confidence: 0, provenance: { source: 'ocr' as const } }),
  ]),
  nfc: Object.freeze([
    Object.freeze({ outcome: 'success', state: 'authentic', message: 'Chip read and passive authentication passed.', confidence: 1, provenance: { source: 'nfc' as const } }),
    Object.freeze({
      outcome: 'success',
      state: 'inconclusive',
      message: 'Chip communication succeeded but authentication was not attempted — routed to human review.',
      confidence: 0.5,
      provenance: { source: 'nfc' as const },
    }),
    Object.freeze({ outcome: 'failure', state: 'failed', message: 'Chip communication or passive authentication failed.', confidence: 1, provenance: { source: 'nfc' as const } }),
    Object.freeze({ outcome: 'success', state: 'not-checked', message: 'NFC passive authentication was not performed — no physical chip transport is available.', confidence: 0, provenance: { source: 'unavailable' as const } }),
  ]),
  authenticity: Object.freeze([
    Object.freeze({ outcome: 'success', state: 'consistent', message: 'All sources agree — no conflicts found.', confidence: 1, provenance: { source: 'ocr' as const } }),
    Object.freeze({
      outcome: 'success',
      state: 'inconclusive',
      message: 'Conflicting values across sources — routed to human review.',
      confidence: 0.5,
      provenance: { source: 'ocr' as const },
    }),
    Object.freeze({ outcome: 'failure', state: 'failed', message: 'A source conflict could not be resolved automatically.', confidence: 1, provenance: { source: 'ocr' as const } }),
    Object.freeze({ outcome: 'offline', state: 'not-checked', message: 'Comparison engine unreachable (simulated offline).', confidence: 0, provenance: { source: 'ocr' as const } }),
  ]),
  'selfie-comparison': Object.freeze([
    Object.freeze({ outcome: 'success', state: 'authentic', message: 'Live selfie matches the document portrait.', confidence: 1, provenance: { source: 'synthetic-fixture' as const } }),
    Object.freeze({
      outcome: 'success',
      state: 'inconclusive',
      message: 'Match confidence is borderline — routed to human review.',
      confidence: 0.5,
      provenance: { source: 'synthetic-fixture' as const },
    }),
    Object.freeze({ outcome: 'failure', state: 'failed', message: 'Live selfie does not match the document portrait.', confidence: 1, provenance: { source: 'synthetic-fixture' as const } }),
    Object.freeze({ outcome: 'offline', state: 'not-checked', message: 'Camera unreachable (simulated offline).', confidence: 0, provenance: { source: 'synthetic-fixture' as const } }),
  ]),
}) satisfies Readonly<Record<EvidenceStepId, readonly EvidenceScenario[]>>;

/**
 * Runs one deterministic scenario for `step`, keyed by `callCount` (the
 * step's own 1-based call number) — the same call count always produces the
 * same scenario, for the same reason `createMockAdapter` is deterministic
 * (BIO-UX-005-AC05/AC06: reproducible, auditable demos and tests).
 */
export function runEvidenceScenario(step: EvidenceStepId, callCount: number): EvidenceStepResult {
  const scenarios = SCENARIOS[step];
  const index = deterministicIndex(`document-verify:${step}:${callCount}`, scenarios.length);
  const scenario = scenarios[index]!;
  return { step, ...scenario };
}

/**
 * BIO-UX-009-AC03: "Low-confidence OCR and conflicting-source scenarios
 * route to simulated human review." Modelled uniformly as any step result
 * landing in the `inconclusive` state — see `SCENARIOS`'s own comment for
 * why more than just `ocr` can reach it.
 */
export function requiresHumanReview(result: Pick<EvidenceStepResult, 'state'>): boolean {
  return result.state === 'inconclusive';
}

export function evidenceStepMeta(step: EvidenceStepId): EvidenceStepMeta {
  const meta = EVIDENCE_STEPS.find((entry) => entry.id === step);
  if (!meta) {
    throw new Error(`Unknown Document Verify evidence step: ${step}`);
  }
  return meta;
}
