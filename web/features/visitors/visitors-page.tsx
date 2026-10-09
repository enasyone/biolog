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
  type ScenarioId,
  type VisitorStatus,
} from '../../lib/synthetic-data';
import { visitorsCheckOutPort, visitorsPort, visitorsViewHistoryPort } from './index';

const VISITOR_STATUS_TONE: Record<VisitorStatus, StatusBadgeTone> = {
  preregistered: 'warning',
  'checked-in': 'success',
  'checked-out': 'neutral',
};

/**
 * Visitors page (BIO-UX-010-AC01, AC02, AC04, AC05).
 *
 * Check-in/out (AC04 "visible simulated events") is a local status
 * transition on top of the generated dataset, confirmed by
 * `visitorsPort`/`visitorsCheckOutPort`'s outcome banner — a checked-in or
 * checked-out visitor visibly moves row in the table below, not just a
 * toast. "View visit history" (`visitorsViewHistoryPort`) reveals a log of
 * every status transition made this session.
 */
export function VisitorsPage() {
  const { tenant } = useSession();
  const [scenarioId, setScenarioId] = useTestScenarioPreset<ScenarioId>(
    'scenario',
    OPERATIONAL_SCENARIO_OPTIONS.map((option) => option.id),
    'normal',
  );
  const dataset = useMemo(() => generateDataset(DEFAULT_SEED, scenarioId), [scenarioId]);
  const notifications = useMemo(() => deriveNotifications(dataset), [dataset]);
  const tenantVisitors = useMemo(
    () => dataset.visitors.filter((visitor) => visitor.tenantId === tenant.id),
    [dataset, tenant.id],
  );
  const tenantPeople = useMemo(() => dataset.people.filter((p) => p.tenantId === tenant.id), [dataset, tenant.id]);

  function hostName(hostPersonId: string): string {
    return tenantPeople.find((p) => p.id === hostPersonId)?.displayName ?? hostPersonId;
  }

  const [statusOverrides, setStatusOverrides] = useState<ReadonlyMap<string, VisitorStatus>>(new Map());
  const [history, setHistory] = useState<readonly { id: string; message: string }[]>([]);
  const [pendingVisitorId, setPendingVisitorId] = useState<string | null>(null);

  const [checkInAction, setCheckInAction] = useState<ActionState>({ status: 'idle' });
  const [checkOutAction, setCheckOutAction] = useState<ActionState>({ status: 'idle' });
  const [historyVisible, setHistoryVisible] = useState(false);
  const [viewHistoryAction, setViewHistoryAction] = useState<ActionState>({ status: 'idle' });

  function statusOf(visitorId: string, base: VisitorStatus): VisitorStatus {
    return statusOverrides.get(visitorId) ?? base;
  }

  async function checkIn(visitorId: string, fullName: string) {
    setPendingVisitorId(visitorId);
    setCheckInAction({ status: 'loading' });
    const result = await visitorsPort.primaryAction();
    setCheckInAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setStatusOverrides((prev) => new Map(prev).set(visitorId, 'checked-in'));
      setHistory((prev) => [...prev, { id: `${visitorId}-in-${Date.now()}`, message: `${fullName} checked in.` }]);
    }
  }

  async function checkOut(visitorId: string, fullName: string) {
    setPendingVisitorId(visitorId);
    setCheckOutAction({ status: 'loading' });
    const result = await visitorsCheckOutPort.primaryAction();
    setCheckOutAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setStatusOverrides((prev) => new Map(prev).set(visitorId, 'checked-out'));
      setHistory((prev) => [...prev, { id: `${visitorId}-out-${Date.now()}`, message: `${fullName} checked out.` }]);
    }
  }

  async function viewVisitHistory() {
    setViewHistoryAction({ status: 'loading' });
    const result = await visitorsViewHistoryPort.primaryAction();
    setViewHistoryAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setHistoryVisible(true);
    }
  }

  return (
    <div className="biolog-placeholder-page">
      <div className="biolog-op-page-header">
        <div>
          <h1>Visitors</h1>
          <p className="biolog-placeholder-description">
            Visitor identity records and visit history for {tenant.name}.
          </p>
        </div>
        <ScenarioPicker value={scenarioId} onChange={setScenarioId} options={OPERATIONAL_SCENARIO_OPTIONS} />
      </div>

      <NotificationList notifications={notifications} />

      <div className="biolog-op-grid">
        <section className="biolog-card">
          <h2>Visitors</h2>
          {tenantVisitors.length === 0 ? (
            <p className="biolog-op-empty">No visitors for this scenario.</p>
          ) : (
            <table className="biolog-table">
              <caption>Visitors</caption>
              <thead>
                <tr>
                  <th scope="col">Visitor</th>
                  <th scope="col">Host</th>
                  <th scope="col">Status</th>
                  <th scope="col">Action</th>
                </tr>
              </thead>
              <tbody>
                {tenantVisitors.map((visitor) => {
                  const status = statusOf(visitor.id, visitor.status);
                  const isPending = pendingVisitorId === visitor.id;
                  return (
                    <tr key={visitor.id}>
                      <td>{visitor.fullName}</td>
                      <td>{hostName(visitor.hostPersonId)}</td>
                      <td>
                        <StatusBadge tone={VISITOR_STATUS_TONE[status]}>{status}</StatusBadge>
                      </td>
                      <td>
                        {status !== 'checked-in' && (
                          <button
                            type="button"
                            className="biolog-button-primary"
                            onClick={() => void checkIn(visitor.id, visitor.fullName)}
                            disabled={isPending && checkInAction.status === 'loading'}
                          >
                            {isPending && checkInAction.status === 'loading' ? 'Working…' : 'Check in visitor'}
                          </button>
                        )}
                        {status === 'checked-in' && (
                          <button
                            type="button"
                            className="biolog-button-secondary"
                            onClick={() => void checkOut(visitor.id, visitor.fullName)}
                            disabled={isPending && checkOutAction.status === 'loading'}
                          >
                            {isPending && checkOutAction.status === 'loading' ? 'Working…' : 'Check out visitor'}
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
          {pendingVisitorId !== null && checkInAction.status !== 'idle' && <ActionStatus action={checkInAction} />}
          {pendingVisitorId !== null && checkOutAction.status !== 'idle' && <ActionStatus action={checkOutAction} />}
        </section>

        <aside className="biolog-card">
          <h2>Visit history</h2>
          <div className="biolog-op-actions">
            <button
              type="button"
              className="biolog-button-secondary"
              onClick={() => void viewVisitHistory()}
              disabled={viewHistoryAction.status === 'loading'}
            >
              {viewHistoryAction.status === 'loading' ? 'Working…' : 'View visit history'}
            </button>
          </div>
          <ActionStatus action={viewHistoryAction} />

          {historyVisible &&
            (history.length === 0 ? (
              <p className="biolog-op-empty">No visit history yet this session — check a visitor in or out.</p>
            ) : (
              <ul className="biolog-op-history-list">
                {history.map((entry) => (
                  <li key={entry.id}>{entry.message}</li>
                ))}
              </ul>
            ))}
        </aside>
      </div>
    </div>
  );
}
