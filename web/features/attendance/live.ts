import { createLiveAdapter } from '../../lib/module-port';
import type { AttendancePort, WorkforceAttendanceAdjustment, WorkforceAttendancePort, WorkforceClockPair, WorkforceDailySummary, WorkforceShift } from './ports';

/**
 * Attendance live adapters (BIO-UX-010-AC05). Each calls its own real
 * backend endpoint this domain's story wires up; until then both genuinely
 * fail closed to `offline` — the correct behaviour for an unconnected LIVE
 * mode, not a bug.
 */
export const attendanceLivePort: AttendancePort = createLiveAdapter(
  'AttendanceService',
  '/api/v1/attendance/primary-action',
);

export const attendanceFlagExceptionLivePort: AttendancePort = createLiveAdapter(
  'AttendanceService',
  '/api/v1/attendance/flag-exception',
);

// BIO-ATT-003-AC05: this story's own real `/api/v1/workforce-attendance/*` backend
// (`WorkforceAttendanceEndpoints.cs`) - genuinely connected, not a stub. Reads (shift list,
// daily summary) are genuinely live end to end. Writes (create shift, create adjustment) hit the
// real, correctly-secured backend (ADR-0006 Decision 3 provenance + a tenant-scoped permission
// grant) - but no browser-usable mechanism exists anywhere in this codebase yet for a pre-auth
// web page to produce a valid provenance proof (the same disclosed, cross-cutting gap
// BIO-TEN-003's own README records; see docs/features/bio-att-003/README.md "AC05: writes are
// genuinely live but cannot complete from the browser yet"). This adapter still sends the write
// requests for real - it does not fake success - so a write genuinely 401s today, visibly, rather
// than silently no-op'ing.

interface WorkforceShiftWire {
  shift_id: string;
  name: string;
  start_time: string;
  end_time: string;
  grace_minutes: number;
  expected_duration_minutes: number;
  version: number;
}

function fromWireShift(wire: WorkforceShiftWire): WorkforceShift {
  return {
    shiftId: wire.shift_id,
    name: wire.name,
    startTime: wire.start_time,
    endTime: wire.end_time,
    graceMinutes: wire.grace_minutes,
    expectedDurationMinutes: wire.expected_duration_minutes,
    version: wire.version,
  };
}

interface WorkforceClockPairWire {
  status: WorkforceClockPair['status'];
  check_in_event_id: string | null;
  check_out_event_id: string | null;
  duration_minutes: number | null;
}

interface WorkforceAttendanceAdjustmentWire {
  adjustment_id: string;
  original_overtime_minutes: number;
  adjusted_overtime_minutes: number;
  reason: string;
  adjusted_by_actor_id: string;
  adjusted_at: string;
}

function fromWireAdjustment(wire: WorkforceAttendanceAdjustmentWire): WorkforceAttendanceAdjustment {
  return {
    adjustmentId: wire.adjustment_id,
    originalOvertimeMinutes: wire.original_overtime_minutes,
    adjustedOvertimeMinutes: wire.adjusted_overtime_minutes,
    reason: wire.reason,
    adjustedByActorId: wire.adjusted_by_actor_id,
    adjustedAt: wire.adjusted_at,
  };
}

interface WorkforceAttendanceInterpretationWire {
  status: 'on_time' | 'late' | 'absent';
  first_check_in_event_id: string | null;
  minutes_late: number | null;
}

interface WorkforceDailySummaryWire {
  person_id: string;
  date: string;
  shift_id: string;
  pairs: WorkforceClockPairWire[];
  computed_overtime_minutes: number;
  effective_overtime_minutes: number;
  interpretation: WorkforceAttendanceInterpretationWire;
  adjustment: WorkforceAttendanceAdjustmentWire | null;
}

export const workforceAttendanceLivePort: WorkforceAttendancePort = {
  async listShifts() {
    const response = await fetch('/api/v1/workforce-attendance/shifts', {
      headers: { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' },
    });
    if (!response.ok) {
      return [];
    }
    const body = (await response.json()) as { data: WorkforceShiftWire[] };
    return body.data.map(fromWireShift);
  },

  async createShift(input) {
    const response = await fetch('/api/v1/workforce-attendance/shifts', {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' },
      body: JSON.stringify({ name: input.name, start_time: input.startTime, end_time: input.endTime, grace_minutes: input.graceMinutes }),
    });
    if (!response.ok) {
      throw new Error(`Live request to create a shift failed: HTTP ${response.status}`);
    }
    return fromWireShift((await response.json()) as WorkforceShiftWire);
  },

  async getDailySummary(personId, date, shiftId) {
    const response = await fetch(
      `/api/v1/workforce-attendance/people/${personId}/daily-summary/${date}?shift_id=${encodeURIComponent(shiftId)}`,
      { headers: { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' } },
    );
    if (!response.ok) {
      throw new Error(`Live request to read the daily summary failed: HTTP ${response.status}`);
    }
    const wire = (await response.json()) as WorkforceDailySummaryWire;
    const summary: WorkforceDailySummary = {
      personId: wire.person_id,
      date: wire.date,
      shiftId: wire.shift_id,
      pairs: wire.pairs.map((p) => ({ status: p.status, checkInEventId: p.check_in_event_id, checkOutEventId: p.check_out_event_id, durationMinutes: p.duration_minutes })),
      computedOvertimeMinutes: wire.computed_overtime_minutes,
      effectiveOvertimeMinutes: wire.effective_overtime_minutes,
      interpretation: {
        status: wire.interpretation.status,
        firstCheckInEventId: wire.interpretation.first_check_in_event_id,
        minutesLate: wire.interpretation.minutes_late,
      },
      adjustment: wire.adjustment ? fromWireAdjustment(wire.adjustment) : null,
    };
    return summary;
  },

  async createAdjustment(personId, date, input) {
    const response = await fetch(`/api/v1/workforce-attendance/people/${personId}/adjustments/${date}`, {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' },
      body: JSON.stringify({ shift_id: input.shiftId, adjusted_overtime_minutes: input.adjustedOvertimeMinutes, reason: input.reason }),
    });
    if (!response.ok) {
      throw new Error(`Live request to create an adjustment failed: HTTP ${response.status}`);
    }
    return fromWireAdjustment((await response.json()) as WorkforceAttendanceAdjustmentWire);
  },
};
