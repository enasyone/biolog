import type { ModulePort } from '../../lib/module-port';

/**
 * Attendance module ports (BIO-UX-010-AC01, AC05).
 *
 * `route-manifest.ts`'s `/attendance` entry names two actions —
 * `primaryAction` ("View attendance record") and `flagException` ("Flag
 * exception") — following the same multi-port pattern `features/reporting`
 * established (BIO-UX-005-AC02). Both stay `ModulePort`-shaped; the actual
 * attendance-record list rendered on `/attendance` is page-level state
 * sourced from `web/lib/synthetic-data`.
 */
export type AttendancePort = ModulePort;

/** BIO-ATT-003-AC01: one named shift schedule. */
export interface WorkforceShift {
  shiftId: string;
  name: string;
  startTime: string;
  endTime: string;
  graceMinutes: number;
  expectedDurationMinutes: number;
  version: number;
}

/** BIO-ATT-003-AC02/AC03: one validated clock pairing for one day. */
export interface WorkforceClockPair {
  status: 'complete' | 'open_check_in' | 'unmatched_check_out' | 'overlapping';
  checkInEventId: string | null;
  checkOutEventId: string | null;
  durationMinutes: number | null;
}

/** BIO-ATT-003-F01: lateness/absence interpretation, derived from the shift's own start time and grace period. */
export interface WorkforceAttendanceInterpretation {
  status: 'on_time' | 'late' | 'absent';
  firstCheckInEventId: string | null;
  minutesLate: number | null;
}

/** BIO-ATT-003-AC04: the latest manual override, if one exists. */
export interface WorkforceAttendanceAdjustment {
  adjustmentId: string;
  originalOvertimeMinutes: number;
  adjustedOvertimeMinutes: number;
  reason: string;
  adjustedByActorId: string;
  adjustedAt: string;
}

/** BIO-ATT-003-AC02/AC03/AC04: one person's one date, fully derived. */
export interface WorkforceDailySummary {
  personId: string;
  date: string;
  shiftId: string;
  pairs: WorkforceClockPair[];
  computedOvertimeMinutes: number;
  effectiveOvertimeMinutes: number;
  interpretation: WorkforceAttendanceInterpretation;
  adjustment: WorkforceAttendanceAdjustment | null;
}

/**
 * BIO-ATT-003-AC01-AC05: the real workforce-attendance methods this story
 * adds to the Attendance module - additive to `AttendancePort` above (its
 * own `primaryAction`/`flagException` generic shape is untouched), the same
 * "domain story extends the port with real methods" precedent BIO-TEN-003's
 * own `TenantsPort` and BIO-INT-002's own `IntegrationsPort` established.
 */
export interface WorkforceAttendancePort {
  listShifts(): Promise<WorkforceShift[]>;
  createShift(input: { name: string; startTime: string; endTime: string; graceMinutes: number }): Promise<WorkforceShift>;
  getDailySummary(personId: string, date: string, shiftId: string): Promise<WorkforceDailySummary>;
  createAdjustment(
    personId: string,
    date: string,
    input: { shiftId: string; adjustedOvertimeMinutes: number; reason: string },
  ): Promise<WorkforceAttendanceAdjustment>;
}
