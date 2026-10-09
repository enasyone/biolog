// Developer tests for BIO-UX-008-AC04 (every result clearly labelled SIMULATED). SYNTHETIC.
import { describe, expect, it } from 'vitest';
import { exceptionBadge, livenessBadge, matchBadge, qualityBadge, verificationBadge } from '../../../web/features/bio-ux-008/formatters';

describe('BIO-UX-008-AC04 every rendered badge is unambiguously labelled SIMULATED', () => {
  it('good quality is success-toned and says SIMULATED', () => {
    const badge = qualityBadge('good', 88);
    expect(badge.tone).toBe('success');
    expect(badge.text).toMatch(/SIMULATED/);
    expect(badge.text).toContain('88');
  });

  it('low quality is warning-toned, not success', () => {
    const badge = qualityBadge('low', 40);
    expect(badge.tone).toBe('warning');
  });

  it('an exception is error-toned and names the reason', () => {
    const badge = exceptionBadge('bandaged finger');
    expect(badge.tone).toBe('error');
    expect(badge.text).toContain('bandaged finger');
    expect(badge.text).toMatch(/SIMULATED/);
  });

  it('a match is success-toned', () => {
    expect(matchBadge('match', 91).tone).toBe('success');
  });

  it('a no-match is error-toned, never mistaken for a pass', () => {
    expect(matchBadge('no_match', 20).tone).toBe('error');
  });

  it('verificationBadge derives the same tone from a VerificationResult', () => {
    expect(verificationBadge({ outcome: 'match', score: 91, message: 'x' }).tone).toBe('success');
    expect(verificationBadge({ outcome: 'no_match', score: 10, message: 'x' }).tone).toBe('error');
  });

  it('liveness "live" and "spoof_suspected" both say placeholder, never implying a real engine ran', () => {
    const live = livenessBadge({ outcome: 'live', simulated: true, message: 'x' });
    const spoof = livenessBadge({ outcome: 'spoof_suspected', simulated: true, message: 'x' });
    expect(live.text).toMatch(/placeholder/i);
    expect(spoof.text).toMatch(/placeholder/i);
    expect(live.tone).not.toBe(spoof.tone);
  });
});
