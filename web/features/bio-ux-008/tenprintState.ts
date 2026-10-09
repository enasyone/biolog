/**
 * Pure tenprint-grid bookkeeping (BIO-UX-008-AC01), kept out of
 * `FingerprintJourneyApp.tsx` so it is unit-testable in Node without a DOM
 * (see `scenarioOptions.ts`'s header comment).
 */
import { TENPRINT_FINGER_POSITIONS, type FingerPosition, type FingerprintCaptureResult } from '../biometrics/ports';

export type FingerSlotStatus = 'pending' | 'captured' | 'exception';

export interface FingerSlotState {
  finger: FingerPosition;
  status: FingerSlotStatus;
  result: FingerprintCaptureResult | null;
}

export type TenprintSlots = Readonly<Record<FingerPosition, FingerSlotState>>;

export function createInitialTenprintSlots(): TenprintSlots {
  const slots = {} as Record<FingerPosition, FingerSlotState>;
  for (const finger of TENPRINT_FINGER_POSITIONS) {
    slots[finger] = { finger, status: 'pending', result: null };
  }
  return Object.freeze(slots);
}

export function recordFingerCapture(slots: TenprintSlots, result: FingerprintCaptureResult): TenprintSlots {
  const status: FingerSlotStatus = result.exceptionReason ? 'exception' : 'captured';
  return Object.freeze({
    ...slots,
    [result.finger]: { finger: result.finger, status, result },
  });
}

export function countByStatus(slots: TenprintSlots, status: FingerSlotStatus): number {
  return TENPRINT_FINGER_POSITIONS.filter((finger) => slots[finger].status === status).length;
}

export function isTenprintComplete(slots: TenprintSlots): boolean {
  return TENPRINT_FINGER_POSITIONS.every((finger) => slots[finger].status !== 'pending');
}
