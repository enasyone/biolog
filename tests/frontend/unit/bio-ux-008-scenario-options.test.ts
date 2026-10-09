// Developer tests for BIO-UX-008-AC03. SYNTHETIC: no real biometric data.
import { describe, expect, it } from 'vitest';
import {
  DEVICE_OPTIONS,
  FACE_CAPTURE_OPTIONS,
  FINGERPRINT_CAPTURE_OPTIONS,
  IDENTIFICATION_OPTIONS,
  LIVENESS_OPTIONS,
  VERIFICATION_OPTIONS,
  hasEveryNamedOutcome,
} from '../../../web/features/bio-ux-008/scenarioOptions';

describe('BIO-UX-008-AC03 every named simulated outcome is explicitly selectable', () => {
  it('fingerprint capture offers good, low and exception', () => {
    expect(hasEveryNamedOutcome(FINGERPRINT_CAPTURE_OPTIONS, ['good', 'low', 'exception'])).toBe(true);
  });

  it('face capture offers good and low (no capture-exception branch in AC02)', () => {
    expect(hasEveryNamedOutcome(FACE_CAPTURE_OPTIONS, ['good', 'low'])).toBe(true);
  });

  it('verification offers match and no_match', () => {
    expect(hasEveryNamedOutcome(VERIFICATION_OPTIONS, ['match', 'no_match'])).toBe(true);
  });

  it('identification offers candidates_found and no_candidates', () => {
    expect(hasEveryNamedOutcome(IDENTIFICATION_OPTIONS, ['candidates_found', 'no_candidates'])).toBe(true);
  });

  it('liveness offers live and spoof_suspected', () => {
    expect(hasEveryNamedOutcome(LIVENESS_OPTIONS, ['live', 'spoof_suspected'])).toBe(true);
  });

  it('device connection offers connected and offline', () => {
    expect(hasEveryNamedOutcome(DEVICE_OPTIONS, ['connected', 'offline'])).toBe(true);
  });

  it('boundary: hasEveryNamedOutcome fails when a named outcome is missing', () => {
    expect(hasEveryNamedOutcome(VERIFICATION_OPTIONS, ['match', 'no_match', 'partial' as never])).toBe(false);
  });
});
