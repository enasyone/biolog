import { createMockAdapter } from '../../lib/module-port';
import type { AttendancePort, WorkforceAttendanceAdjustment, WorkforceAttendancePort, WorkforceShift } from './ports';

/**
 * Attendance mock adapters (BIO-UX-010-AC01, AC05): deterministically cycle
 * success, failure and offline outcomes so every state is reproducible for
 * local testing and demos, not just the happy path.
 */
export const attendanceMockPort: AttendancePort = createMockAdapter('AttendanceService', {
  scenarios: [
    { outcome: 'success', message: 'Attendance: record loaded.' },
    { outcome: 'failure', message: 'Attendance: record failed to load — try again.' },
    { outcome: 'offline', message: 'Attendance: device or network appears offline (simulated).' },
  ],
});

export const attendanceFlagExceptionMockPort: AttendancePort = createMockAdapter('AttendanceService', {
  scenarios: [
    { outcome: 'success', message: 'Attendance: exception flagged.' },
    { outcome: 'failure', message: 'Attendance: exception could not be flagged — try again.' },
    { outcome: 'offline', message: 'Attendance: device or network appears offline (simulated).' },
  ],
});

/** BIO-ATT-003-AC01-AC05: in-memory mock store, same fail-closed defaults as the real backend (no shifts until one is created, no adjustment until one is created). */
let nextShiftId = 1;
const shifts: WorkforceShift[] = [];
const adjustmentsByKey = new Map<string, WorkforceAttendanceAdjustment>();
let nextAdjustmentId = 1;

export const workforceAttendanceMockPort: WorkforceAttendancePort = {
  async listShifts() {
    return [...shifts];
  },

  async createShift(input) {
    const shift: WorkforceShift = {
      shiftId: `mock-shift-${nextShiftId++}`,
      name: input.name,
      startTime: input.startTime,
      endTime: input.endTime,
      graceMinutes: input.graceMinutes,
      expectedDurationMinutes: 540,
      version: 1,
    };
    shifts.push(shift);
    return shift;
  },

  async getDailySummary(personId, date, shiftId) {
    const adjustment = adjustmentsByKey.get(`${personId}|${date}`) ?? null;
    return {
      personId,
      date,
      shiftId,
      pairs: [{ status: 'complete', checkInEventId: 'mock-evt-in', checkOutEventId: 'mock-evt-out', durationMinutes: 540 }],
      computedOvertimeMinutes: 0,
      effectiveOvertimeMinutes: adjustment?.adjustedOvertimeMinutes ?? 0,
      interpretation: { status: 'on_time', firstCheckInEventId: 'mock-evt-in', minutesLate: null },
      adjustment,
    };
  },

  async createAdjustment(personId, date, input) {
    const adjustment: WorkforceAttendanceAdjustment = {
      adjustmentId: `mock-adj-${nextAdjustmentId++}`,
      originalOvertimeMinutes: 0,
      adjustedOvertimeMinutes: input.adjustedOvertimeMinutes,
      reason: input.reason,
      adjustedByActorId: 'web-ui',
      adjustedAt: new Date().toISOString(),
    };
    adjustmentsByKey.set(`${personId}|${date}`, adjustment);
    return adjustment;
  },
};
