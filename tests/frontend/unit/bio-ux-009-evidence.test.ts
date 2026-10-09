// Developer tests for BIO-UX-009-AC01, AC02, AC03. SYNTHETIC: no real document/biometric data.
import { describe, expect, it } from 'vitest';
import {
  EVIDENCE_CHECK_STATES,
  EVIDENCE_STEPS,
  isEvidenceCheckState,
  requiresHumanReview,
  runEvidenceScenario,
  type EvidenceCheckState,
  type EvidenceStepId,
} from '../../../web/features/documents/evidence';

describe('BIO-UX-009-AC02 every named evidence state is reachable by at least one step', () => {
  it('every state in AC02\'s own vocabulary is used by at least one step\'s scenario table', () => {
    const seen = new Set<EvidenceCheckState>();
    for (const step of EVIDENCE_STEPS) {
      for (let callCount = 1; callCount <= 8; callCount += 1) {
        seen.add(runEvidenceScenario(step.id, callCount).state);
      }
    }
    for (const state of EVIDENCE_CHECK_STATES) {
      expect(seen.has(state), `state "${state}" was never reached by any step's scenario table`).toBe(true);
    }
  });

  it('isEvidenceCheckState accepts every real state and rejects an arbitrary string', () => {
    for (const state of EVIDENCE_CHECK_STATES) {
      expect(isEvidenceCheckState(state)).toBe(true);
    }
    expect(isEvidenceCheckState('not-a-real-state')).toBe(false);
    expect(isEvidenceCheckState(undefined)).toBe(false);
  });
});

describe('BIO-UX-009-AC05 runEvidenceScenario is deterministic (reproducible, auditable)', () => {
  it('the same (step, callCount) pair always returns the exact same scenario', () => {
    const a = runEvidenceScenario('ocr', 2);
    const b = runEvidenceScenario('ocr', 2);
    expect(a).toEqual(b);
  });

  it('every step reaches "failed" and "not-checked" somewhere in its own cycle', () => {
    for (const step of EVIDENCE_STEPS) {
      const states = new Set<EvidenceCheckState>();
      for (let callCount = 1; callCount <= 8; callCount += 1) {
        states.add(runEvidenceScenario(step.id, callCount).state);
      }
      expect(states.has('failed'), `${step.id} never reaches "failed"`).toBe(true);
      expect(states.has('not-checked'), `${step.id} never reaches "not-checked"`).toBe(true);
    }
  });

  it('different steps do not share the exact same scenario sequence (independent cycles)', () => {
    const capture = [1, 2, 3].map((n) => runEvidenceScenario('capture', n).state);
    const mrz = [1, 2, 3].map((n) => runEvidenceScenario('mrz', n).state);
    expect(capture).not.toEqual(mrz);
  });
});

describe('BIO-UX-009-AC03 low-confidence OCR and conflicting-source scenarios route to human review', () => {
  it('requiresHumanReview is true exactly for the "inconclusive" state', () => {
    expect(requiresHumanReview({ state: 'inconclusive' })).toBe(true);
    for (const state of EVIDENCE_CHECK_STATES) {
      if (state === 'inconclusive') continue;
      expect(requiresHumanReview({ state }), `state "${state}" should not require review`).toBe(false);
    }
  });

  it('OCR has a reachable low-confidence (inconclusive) scenario carrying a confidence value', () => {
    const inconclusive = [1, 2, 3, 4]
      .map((n) => runEvidenceScenario('ocr', n))
      .find((result) => result.state === 'inconclusive');
    expect(inconclusive).toBeDefined();
    expect(inconclusive?.confidence).toBeLessThan(0.5);
  });

  it('authenticity (the cross-source comparison step) has a reachable conflicting-source scenario', () => {
    const inconclusive = [1, 2, 3, 4]
      .map((n) => runEvidenceScenario('authenticity', n))
      .find((result) => result.state === 'inconclusive');
    expect(inconclusive).toBeDefined();
  });
});

describe('BIO-UX-009-AC01 capture gates the rest of the journey', () => {
  it('capture itself is never gated; every other step is', () => {
    const gated: EvidenceStepId[] = EVIDENCE_STEPS.filter((s) => s.requiresCapture).map((s) => s.id);
    const notGated: EvidenceStepId[] = EVIDENCE_STEPS.filter((s) => !s.requiresCapture).map((s) => s.id);
    expect(notGated).toEqual(['capture']);
    expect(gated).toContain('mrz');
    expect(gated).toContain('selfie-comparison');
  });
});
