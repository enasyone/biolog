// Developer tests for BIO-UX-008-AC01. SYNTHETIC: no real fingerprint data.
import { describe, expect, it } from 'vitest';
import {
  countByStatus,
  createInitialTenprintSlots,
  isTenprintComplete,
  recordFingerCapture,
} from '../../../web/features/bio-ux-008/tenprintState';
import { TENPRINT_FINGER_POSITIONS } from '../../../web/features/biometrics/ports';

describe('BIO-UX-008-AC01 tenprint slot bookkeeping', () => {
  it('starts with all ten fingers pending', () => {
    const slots = createInitialTenprintSlots();
    expect(TENPRINT_FINGER_POSITIONS.every((finger) => slots[finger].status === 'pending')).toBe(true);
    expect(isTenprintComplete(slots)).toBe(false);
    expect(countByStatus(slots, 'pending')).toBe(10);
  });

  it('recording a good-quality capture marks only that finger captured', () => {
    const slots = recordFingerCapture(createInitialTenprintSlots(), {
      finger: 'right_thumb',
      score: 84,
      outcome: 'good',
      message: 'ok',
    });
    expect(slots.right_thumb.status).toBe('captured');
    expect(slots.right_index.status).toBe('pending');
    expect(countByStatus(slots, 'captured')).toBe(1);
  });

  it('a result carrying exceptionReason marks the slot as an exception, not a routine capture', () => {
    const slots = recordFingerCapture(createInitialTenprintSlots(), {
      finger: 'left_little',
      score: 0,
      outcome: 'low',
      exceptionReason: 'bandaged',
      message: 'exception',
    });
    expect(slots.left_little.status).toBe('exception');
    expect(countByStatus(slots, 'exception')).toBe(1);
    expect(countByStatus(slots, 'captured')).toBe(0);
  });

  it('recapturing an already-captured finger overwrites its slot rather than duplicating state', () => {
    let slots = recordFingerCapture(createInitialTenprintSlots(), {
      finger: 'right_thumb',
      score: 30,
      outcome: 'low',
      message: 'low',
    });
    slots = recordFingerCapture(slots, { finger: 'right_thumb', score: 90, outcome: 'good', message: 'good' });
    expect(slots.right_thumb.status).toBe('captured');
    expect(slots.right_thumb.result?.score).toBe(90);
    expect(countByStatus(slots, 'captured')).toBe(1);
  });

  it('is complete only once every one of the ten fingers has a non-pending result', () => {
    let slots = createInitialTenprintSlots();
    for (const finger of TENPRINT_FINGER_POSITIONS.slice(0, 9)) {
      slots = recordFingerCapture(slots, { finger, score: 80, outcome: 'good', message: 'ok' });
    }
    expect(isTenprintComplete(slots)).toBe(false);
    slots = recordFingerCapture(slots, {
      finger: TENPRINT_FINGER_POSITIONS[9]!,
      score: 0,
      outcome: 'low',
      exceptionReason: 'bandaged',
      message: 'exception',
    });
    // An exception still counts toward "attempted every finger" completion — it is a resolved
    // outcome for that slot, not an unattempted one (BIO-UX-008-AC01's own "exception ... screens
    // are interactive" wording treats it as a first-class terminal outcome, not a blocker).
    expect(isTenprintComplete(slots)).toBe(true);
  });
});
