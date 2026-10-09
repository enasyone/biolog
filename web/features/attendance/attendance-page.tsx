'use client';

import { useMemo, useState } from 'react';
import { ActionStatus, type ActionState } from '../../components/action-status';
import { NotificationList } from '../../components/notification-list';
import { OPERATIONAL_SCENARIO_OPTIONS, ScenarioPicker } from '../../components/scenario-picker';
import { StatusBadge, type StatusBadgeTone } from '../../components/status-badge';
import { useSession } from '../../context/session-context';
import { useTestScenarioPreset } from '../../dev-tools/use-test-scenario-preset';
import {
  DEFAULT_SEED,
  deriveNotifications,
  generateDataset,
  type AttendanceStatus,
  type ScenarioId,
} from '../../lib/synthetic-data';
import { attendanceFlagExceptionPort, attendancePort, workforceAttendancePort } from './index';
import type { WorkforceDailySummary, WorkforceShift } from './ports';

const ATTENDANCE_STATUS_TONE: Record<AttendanceStatus, StatusBadgeTone> = {
  present: 'success',
  absent: 'error',
  late: 'warning',
};

/**
 * Attendance page (BIO-UX-010-AC01, AC02, AC05).
 *
 * `route-manifest.ts` names the *primary* action "View attendance record" —
 * this page loads the tenant's attendance list on demand via `attendancePort`
 * (rather than eagerly, matching the manifest's own "primary action" framing
 * of it as something the operator triggers) and "Flag exception" as the
 * secondary action, which marks a record locally as flagged (a real,
 * visible UI state change) after `attendanceFlagExceptionPort` reports its
 * outcome.
 */
export function AttendancePage() {
  const { tenant } = useSession();
  const [scenarioId, setScenarioId] = useTestScenarioPreset<ScenarioId>(
    'scenario',
    OPERATIONAL_SCENARIO_OPTIONS.map((option) => option.id),
    'normal',
  );
  const dataset = useMemo(() => generateDataset(DEFAULT_SEED, scenarioId), [scenarioId]);
  const notifications = useMemo(() => deriveNotifications(dataset), [dataset]);
  const tenantRecords = useMemo(
    () => dataset.attendanceRecords.filter((record) => record.tenantId === tenant.id),
    [dataset, tenant.id],
  );
  const tenantPeople = useMemo(() => dataset.people.filter((p) => p.tenantId === tenant.id), [dataset, tenant.id]);

  function personName(personId: string): string {
    return tenantPeople.find((p) => p.id === personId)?.displayName ?? personId;
  }

  const [recordsVisible, setRecordsVisible] = useState(false);
  const [viewAction, setViewAction] = useState<ActionState>({ status: 'idle' });

  const [flaggedIds, setFlaggedIds] = useState<ReadonlySet<string>>(new Set());
  const [flagAction, setFlagAction] = useState<ActionState>({ status: 'idle' });
  const [flaggingId, setFlaggingId] = useState<string | null>(null);

  async function viewAttendanceRecord() {
    setViewAction({ status: 'loading' });
    const result = await attendancePort.primaryAction();
    setViewAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setRecordsVisible(true);
    }
  }

  async function flagException(recordId: string) {
    setFlaggingId(recordId);
    setFlagAction({ status: 'loading' });
    const result = await attendanceFlagExceptionPort.primaryAction();
    setFlagAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setFlaggedIds((prev) => new Set(prev).add(recordId));
    }
  }

  return (
    <div className="biolog-placeholder-page">
      <div className="biolog-op-page-header">
        <div>
          <h1>Attendance</h1>
          <p className="biolog-placeholder-description">
            Attendance records and exception handling for {tenant.name}.
          </p>
        </div>
        <ScenarioPicker value={scenarioId} onChange={setScenarioId} options={OPERATIONAL_SCENARIO_OPTIONS} />
      </div>

      <NotificationList notifications={notifications} />

      <section className="biolog-card">
        <h2>Attendance record</h2>
        <div className="biolog-op-actions">
          <button
            type="button"
            className="biolog-button-primary"
            onClick={() => void viewAttendanceRecord()}
            disabled={viewAction.status === 'loading'}
          >
            {viewAction.status === 'loading' ? 'Working…' : 'View attendance record'}
          </button>
        </div>
        <ActionStatus action={viewAction} />

        {recordsVisible &&
          (tenantRecords.length === 0 ? (
            <p className="biolog-op-empty">No attendance records for this scenario.</p>
          ) : (
            <table className="biolog-table">
              <caption>Attendance for {tenantRecords[0]?.date}</caption>
              <thead>
                <tr>
                  <th scope="col">Person</th>
                  <th scope="col">Date</th>
                  <th scope="col">Status</th>
                  <th scope="col">Exception</th>
                </tr>
              </thead>
              <tbody>
                {tenantRecords.map((record) => (
                  <tr key={record.id}>
                    <td>{personName(record.personId)}</td>
                    <td>{record.date}</td>
                    <td>
                      <StatusBadge tone={ATTENDANCE_STATUS_TONE[record.status]}>{record.status}</StatusBadge>
                    </td>
                    <td>
                      {flaggedIds.has(record.id) ? (
                        <StatusBadge tone="error">flagged</StatusBadge>
                      ) : (
                        <button
                          type="button"
                          className="biolog-button-secondary"
                          onClick={() => void flagException(record.id)}
                          disabled={flagAction.status === 'loading' && flaggingId === record.id}
                        >
                          {flagAction.status === 'loading' && flaggingId === record.id ? 'Working…' : 'Flag exception'}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ))}
        {flaggingId !== null && <ActionStatus action={flagAction} />}
      </section>

      <WorkforceAttendanceSection />
    </div>
  );
}

/**
 * BIO-ATT-003-AC01/AC02/AC03/AC04/AC05: the real workforce shift/overtime/exception console,
 * additive to the existing section above (the pre-existing "View attendance record"/"Flag
 * exception" actions are untouched - no redesign of the approved page). Calls
 * `workforceAttendancePort` (BIO-ATT-003-AC05) rather than any mock/raw backend directly.
 */
function WorkforceAttendanceSection() {
  const [shifts, setShifts] = useState<WorkforceShift[]>([]);
  const [shiftsLoaded, setShiftsLoaded] = useState(false);
  const [shiftName, setShiftName] = useState('Day');
  const [shiftStart, setShiftStart] = useState('08:00');
  const [shiftEnd, setShiftEnd] = useState('17:00');
  const [shiftGrace, setShiftGrace] = useState('10');
  const [createShiftNotice, setCreateShiftNotice] = useState<string | null>(null);

  const [personId, setPersonId] = useState('');
  const [summaryDate, setSummaryDate] = useState('');
  const [selectedShiftId, setSelectedShiftId] = useState('');
  const [summary, setSummary] = useState<WorkforceDailySummary | null>(null);
  const [summaryNotice, setSummaryNotice] = useState<string | null>(null);

  const [adjustedMinutes, setAdjustedMinutes] = useState('0');
  const [adjustmentReason, setAdjustmentReason] = useState('');
  const [adjustmentNotice, setAdjustmentNotice] = useState<string | null>(null);

  async function loadShifts() {
    const loaded = await workforceAttendancePort.listShifts();
    setShifts(loaded);
    setShiftsLoaded(true);
    if (loaded.length > 0 && !selectedShiftId) {
      setSelectedShiftId(loaded[0]!.shiftId);
    }
  }

  async function createShift() {
    setCreateShiftNotice(null);
    try {
      await workforceAttendancePort.createShift({ name: shiftName, startTime: shiftStart, endTime: shiftEnd, graceMinutes: Number(shiftGrace) || 0 });
      setCreateShiftNotice('Shift created.');
      await loadShifts();
    } catch {
      setCreateShiftNotice('Could not create the shift - the live service may require authentication not yet available from the browser (SIMULATION mode is always reachable).');
    }
  }

  async function loadSummary() {
    setSummaryNotice(null);
    setSummary(null);
    try {
      const loaded = await workforceAttendancePort.getDailySummary(personId, summaryDate, selectedShiftId);
      setSummary(loaded);
    } catch {
      setSummaryNotice('Could not load the daily summary - check the person id, date and shift.');
    }
  }

  async function createAdjustment() {
    setAdjustmentNotice(null);
    try {
      await workforceAttendancePort.createAdjustment(personId, summaryDate, {
        shiftId: selectedShiftId,
        adjustedOvertimeMinutes: Number(adjustedMinutes) || 0,
        reason: adjustmentReason,
      });
      setAdjustmentNotice('Adjustment recorded.');
      await loadSummary();
    } catch {
      setAdjustmentNotice('Could not record the adjustment - the live service may require authentication not yet available from the browser (SIMULATION mode is always reachable).');
    }
  }

  return (
    <>
      <section className="biolog-card">
        <h2>Workforce shift schedules</h2>
        <p className="biolog-placeholder-description">
          BIO-ATT-003-AC01: shift schedules and grace periods are configurable, tenant-wide.
        </p>
        <div className="biolog-op-actions">
          <button type="button" className="biolog-button-secondary" onClick={() => void loadShifts()}>
            Load shifts
          </button>
        </div>
        {shiftsLoaded && (
          <table className="biolog-table" aria-label="Workforce shifts">
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Start</th>
                <th scope="col">End</th>
                <th scope="col">Grace (min)</th>
                <th scope="col">Expected duration (min)</th>
              </tr>
            </thead>
            <tbody>
              {shifts.length === 0 ? (
                <tr>
                  <td colSpan={5} className="biolog-op-empty">No shifts configured yet.</td>
                </tr>
              ) : (
                shifts.map((shift) => (
                  <tr key={shift.shiftId}>
                    <td>{shift.name}</td>
                    <td>{shift.startTime}</td>
                    <td>{shift.endTime}</td>
                    <td>{shift.graceMinutes}</td>
                    <td>{shift.expectedDurationMinutes}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        )}

        <div className="biolog-field-row">
          <label htmlFor="shift-name">Name</label>
          <input id="shift-name" type="text" value={shiftName} onChange={(e) => setShiftName(e.target.value)} />
        </div>
        <div className="biolog-field-row">
          <label htmlFor="shift-start">Start time</label>
          <input id="shift-start" type="text" placeholder="HH:mm" value={shiftStart} onChange={(e) => setShiftStart(e.target.value)} />
        </div>
        <div className="biolog-field-row">
          <label htmlFor="shift-end">End time</label>
          <input id="shift-end" type="text" placeholder="HH:mm" value={shiftEnd} onChange={(e) => setShiftEnd(e.target.value)} />
        </div>
        <div className="biolog-field-row">
          <label htmlFor="shift-grace">Grace minutes</label>
          <input id="shift-grace" type="number" value={shiftGrace} onChange={(e) => setShiftGrace(e.target.value)} />
        </div>
        <div className="biolog-op-actions">
          <button type="button" className="biolog-button-primary" onClick={() => void createShift()}>
            Create shift
          </button>
        </div>
        {createShiftNotice && <p role="status">{createShiftNotice}</p>}
      </section>

      <section className="biolog-card">
        <h2>Daily overtime summary</h2>
        <p className="biolog-placeholder-description">
          BIO-ATT-003-AC02/AC03: clock pairs are validated and overtime is derived from them, never a raw-event mutation.
        </p>
        <div className="biolog-field-row">
          <label htmlFor="summary-person-id">Person id</label>
          <input id="summary-person-id" type="text" value={personId} onChange={(e) => setPersonId(e.target.value)} />
        </div>
        <div className="biolog-field-row">
          <label htmlFor="summary-date">Date</label>
          <input id="summary-date" type="text" placeholder="YYYY-MM-DD" value={summaryDate} onChange={(e) => setSummaryDate(e.target.value)} />
        </div>
        <div className="biolog-field-row">
          <label htmlFor="summary-shift-id">Shift</label>
          <select id="summary-shift-id" value={selectedShiftId} onChange={(e) => setSelectedShiftId(e.target.value)}>
            {shifts.map((shift) => (
              <option key={shift.shiftId} value={shift.shiftId}>
                {shift.name}
              </option>
            ))}
          </select>
        </div>
        <div className="biolog-op-actions">
          <button type="button" className="biolog-button-secondary" onClick={() => void loadSummary()}>
            Load daily summary
          </button>
        </div>
        {summaryNotice && <p role="alert">{summaryNotice}</p>}

        {summary && (
          <>
            <table className="biolog-table" aria-label="Clock pairs">
              <thead>
                <tr>
                  <th scope="col">Status</th>
                  <th scope="col">Check-in</th>
                  <th scope="col">Check-out</th>
                  <th scope="col">Duration (min)</th>
                </tr>
              </thead>
              <tbody>
                {summary.pairs.length === 0 ? (
                  <tr>
                    <td colSpan={4} className="biolog-op-empty">No clock events for this date.</td>
                  </tr>
                ) : (
                  summary.pairs.map((pair, index) => (
                    <tr key={`${pair.checkInEventId ?? 'none'}-${pair.checkOutEventId ?? 'none'}-${index}`}>
                      <td>{pair.status}</td>
                      <td>{pair.checkInEventId ?? '—'}</td>
                      <td>{pair.checkOutEventId ?? '—'}</td>
                      <td>{pair.durationMinutes ?? '—'}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
            <p className="biolog-placeholder-description">
              Computed overtime: {summary.computedOvertimeMinutes} min - Effective overtime: {summary.effectiveOvertimeMinutes} min
              {summary.adjustment && ` (adjusted: "${summary.adjustment.reason}")`}
            </p>
            <p className="biolog-placeholder-description">
              {summary.interpretation.status === 'absent' && 'Absent - no check-in recorded for this date.'}
              {summary.interpretation.status === 'on_time' && 'On time.'}
              {summary.interpretation.status === 'late' && `Late by ${summary.interpretation.minutesLate} min.`}
            </p>

            <h3>BIO-ATT-003-AC04: record an exception</h3>
            <div className="biolog-field-row">
              <label htmlFor="adjustment-minutes">Adjusted overtime (min)</label>
              <input id="adjustment-minutes" type="number" value={adjustedMinutes} onChange={(e) => setAdjustedMinutes(e.target.value)} />
            </div>
            <div className="biolog-field-row">
              <label htmlFor="adjustment-reason">Reason (required)</label>
              <input id="adjustment-reason" type="text" value={adjustmentReason} onChange={(e) => setAdjustmentReason(e.target.value)} />
            </div>
            <div className="biolog-op-actions">
              <button type="button" className="biolog-button-secondary" onClick={() => void createAdjustment()} disabled={adjustmentReason.trim().length === 0}>
                Record adjustment
              </button>
            </div>
            {adjustmentNotice && <p role="status">{adjustmentNotice}</p>}
          </>
        )}
      </section>
    </>
  );
}
