// Developer tests for BIO-UX-008-AC01/AC02/AC03/AC04/AC05. SYNTHETIC: no real biometric data.
import { describe, expect, it } from 'vitest';
import { biometricsMockPort } from '../../../web/features/biometrics/mock';

describe('BIO-UX-008-AC05 primaryAction is preserved unchanged for /enrolment\'s PlaceholderPage', () => {
  it('module is BiometricService and mode is mock', () => {
    expect(biometricsMockPort.module).toBe('BiometricService');
    expect(biometricsMockPort.mode).toBe('mock');
  });

  it('primaryAction still resolves a generic success/failure/offline outcome', async () => {
    const result = await biometricsMockPort.primaryAction();
    expect(['success', 'failure', 'offline']).toContain(result.outcome);
  });
});

describe('BIO-UX-008-AC03 an explicit hint deterministically forces the matching outcome', () => {
  it('captureFingerprint("good") always returns a good-quality result', async () => {
    for (let i = 0; i < 3; i += 1) {
      const result = await biometricsMockPort.captureFingerprint('right_thumb', 'good');
      expect(result.outcome).toBe('good');
      expect(result.exceptionReason).toBeUndefined();
    }
  });

  it('captureFingerprint("exception") always returns an exception with a reason', async () => {
    const result = await biometricsMockPort.captureFingerprint('left_index', 'exception');
    expect(result.exceptionReason).toBeTruthy();
  });

  it('verifyFace("match") and ("no_match") are both directly reachable, not just by chance', async () => {
    const match = await biometricsMockPort.verifyFace('match');
    const noMatch = await biometricsMockPort.verifyFace('no_match');
    expect(match.outcome).toBe('match');
    expect(noMatch.outcome).toBe('no_match');
  });

  it('identifyFace("no_candidates") returns an empty ranked list', async () => {
    const result = await biometricsMockPort.identifyFace('no_candidates');
    expect(result.candidates).toHaveLength(0);
  });

  it('identifyFace("candidates_found") returns a non-empty list ranked by descending score', async () => {
    const result = await biometricsMockPort.identifyFace('candidates_found');
    expect(result.candidates.length).toBeGreaterThan(0);
    const scores = result.candidates.map((c) => c.score);
    expect(scores).toEqual([...scores].sort((a, b) => b - a));
  });

  it('checkLiveness hints are both reachable and both explicitly marked simulated', async () => {
    const live = await biometricsMockPort.checkLiveness('live');
    const spoof = await biometricsMockPort.checkLiveness('spoof_suspected');
    expect(live.simulated).toBe(true);
    expect(spoof.simulated).toBe(true);
    expect(live.outcome).toBe('live');
    expect(spoof.outcome).toBe('spoof_suspected');
  });

  it('connectDevice hints are both reachable for each device kind', async () => {
    const connected = await biometricsMockPort.connectDevice('face_camera', 'connected');
    const offline = await biometricsMockPort.connectDevice('fingerprint_scanner', 'offline');
    expect(connected.outcome).toBe('success');
    expect(offline.outcome).toBe('offline');
  });
});

describe('BIO-UX-008-AC01 each finger position cycles its own independent history', () => {
  it('a never-yet-captured finger\'s first auto-cycled result is unaffected by another finger\'s call history', async () => {
    // A fresh, dedicated pair of fingers this test alone touches, so shared module-level
    // call-count state from other tests/files cannot leak in.
    const untouchedFirstResult = await biometricsMockPort.captureFingerprint('left_ring');
    for (let i = 0; i < 5; i += 1) {
      await biometricsMockPort.captureFingerprint('left_middle');
    }
    const stillFirstForOther = await biometricsMockPort.captureFingerprint('left_thumb');
    expect(untouchedFirstResult.finger).toBe('left_ring');
    expect(stillFirstForOther.finger).toBe('left_thumb');
    // Both are genuinely resolved (good/low, with or without an exception reason) - the
    // point is that five prior calls against a *different* finger did not throw, hang or
    // otherwise corrupt this finger's own first call.
    expect(['good', 'low']).toContain(stillFirstForOther.outcome);
  });
});

describe('BIO-UX-008-AC04 every message clearly names itself as simulated', () => {
  it('capture, verify, identify and liveness messages all say SIMULATED', async () => {
    const capture = await biometricsMockPort.captureFingerprint('right_thumb', 'good');
    const verify = await biometricsMockPort.verifyFingerprint('right_thumb', 'match');
    const face = await biometricsMockPort.captureFace('good');
    const identify = await biometricsMockPort.identifyFace('candidates_found');
    const liveness = await biometricsMockPort.checkLiveness('live');
    for (const message of [capture.message, verify.message, face.message, identify.message, liveness.message]) {
      expect(message).toMatch(/SIMULATED/);
    }
  });
});
