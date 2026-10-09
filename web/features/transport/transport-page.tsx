'use client';

import { useMemo, useState } from 'react';
import { ActionStatus, type ActionState } from '../../components/action-status';
import { NotificationList } from '../../components/notification-list';
import { OPERATIONAL_SCENARIO_OPTIONS, ScenarioPicker } from '../../components/scenario-picker';
import { StatusBadge } from '../../components/status-badge';
import { useSession } from '../../context/session-context';
import { useTestScenarioPreset } from '../../dev-tools/use-test-scenario-preset';
import {
  DEFAULT_SEED,
  deriveNotifications,
  generateDataset,
  TRANSPORT_ROUTE_NAMES,
  type ScenarioId,
} from '../../lib/synthetic-data';
import { TransportNotificationsPanel } from '../bio-trn-003/TransportNotificationsPanel';
import { transportAssignRoutePort, transportPort } from './index';

interface SimulatedAssignment {
  id: string;
  personName: string;
  routeName: string;
}

/**
 * Transport page (BIO-UX-010-AC01, AC02, AC04, AC05).
 *
 * `route-manifest.ts`'s primary action ("View boarding/alighting log") is a
 * query — loaded on demand via `transportPort` — and its secondary action
 * ("Assign route") is a command producing a visible simulated assignment
 * (AC04) after `transportAssignRoutePort` reports success.
 */
export function TransportPage() {
  const { tenant } = useSession();
  const [scenarioId, setScenarioId] = useTestScenarioPreset<ScenarioId>(
    'scenario',
    OPERATIONAL_SCENARIO_OPTIONS.map((option) => option.id),
    'normal',
  );
  const dataset = useMemo(() => generateDataset(DEFAULT_SEED, scenarioId), [scenarioId]);
  const notifications = useMemo(() => deriveNotifications(dataset), [dataset]);
  const tenantEvents = useMemo(
    () => dataset.transportEvents.filter((event) => event.tenantId === tenant.id),
    [dataset, tenant.id],
  );
  const tenantPeople = useMemo(() => dataset.people.filter((p) => p.tenantId === tenant.id), [dataset, tenant.id]);

  function personName(personId: string): string {
    return tenantPeople.find((p) => p.id === personId)?.displayName ?? personId;
  }

  const [logVisible, setLogVisible] = useState(false);
  const [viewLogAction, setViewLogAction] = useState<ActionState>({ status: 'idle' });

  const [selectedPersonId, setSelectedPersonId] = useState(() => tenantPeople[0]?.id ?? '');
  const [selectedRoute, setSelectedRoute] = useState(TRANSPORT_ROUTE_NAMES[0]!);
  const [assignments, setAssignments] = useState<readonly SimulatedAssignment[]>([]);
  const [assignAction, setAssignAction] = useState<ActionState>({ status: 'idle' });

  async function viewBoardingLog() {
    setViewLogAction({ status: 'loading' });
    const result = await transportPort.primaryAction();
    setViewLogAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setLogVisible(true);
    }
  }

  async function assignRoute() {
    setAssignAction({ status: 'loading' });
    const result = await transportAssignRoutePort.primaryAction();
    setAssignAction({ status: 'done', ...result });
    if (result.outcome === 'success' && selectedPersonId) {
      setAssignments((prev) => [
        ...prev,
        { id: `assignment-${prev.length}-${Date.now()}`, personName: personName(selectedPersonId), routeName: selectedRoute },
      ]);
    }
  }

  return (
    <div className="biolog-placeholder-page">
      <div className="biolog-op-page-header">
        <div>
          <h1>Transport</h1>
          <p className="biolog-placeholder-description">
            Transport boarding, alighting and route records for {tenant.name}.
          </p>
        </div>
        <ScenarioPicker value={scenarioId} onChange={setScenarioId} options={OPERATIONAL_SCENARIO_OPTIONS} />
      </div>

      <NotificationList notifications={notifications} />

      <div className="biolog-op-grid">
        <section className="biolog-card">
          <h2>Boarding / alighting log</h2>
          <div className="biolog-op-actions">
            <button
              type="button"
              className="biolog-button-primary"
              onClick={() => void viewBoardingLog()}
              disabled={viewLogAction.status === 'loading'}
            >
              {viewLogAction.status === 'loading' ? 'Working…' : 'View boarding/alighting log'}
            </button>
          </div>
          <ActionStatus action={viewLogAction} />

          {logVisible &&
            (tenantEvents.length === 0 ? (
              <p className="biolog-op-empty">No boarding/alighting events for this scenario.</p>
            ) : (
              <table className="biolog-table">
                <caption>Boarding / alighting log</caption>
                <thead>
                  <tr>
                    <th scope="col">Person</th>
                    <th scope="col">Route</th>
                    <th scope="col">Event</th>
                  </tr>
                </thead>
                <tbody>
                  {tenantEvents.map((event) => (
                    <tr key={event.id}>
                      <td>{personName(event.personId)}</td>
                      <td>{event.routeName}</td>
                      <td>
                        <StatusBadge tone={event.eventType === 'boarding' ? 'success' : 'warning'}>
                          {event.eventType}
                        </StatusBadge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ))}
        </section>

        <aside className="biolog-card">
          <h2>Assign route</h2>
          <form
            className="biolog-op-form"
            onSubmit={(event) => {
              event.preventDefault();
              void assignRoute();
            }}
          >
            <label className="biolog-op-field">
              <span>Person</span>
              <select
                aria-label="Person"
                value={selectedPersonId}
                onChange={(event) => setSelectedPersonId(event.target.value)}
              >
                {tenantPeople.map((person) => (
                  <option key={person.id} value={person.id}>
                    {person.displayName}
                  </option>
                ))}
              </select>
            </label>
            <label className="biolog-op-field">
              <span>Route</span>
              <select aria-label="Route" value={selectedRoute} onChange={(event) => setSelectedRoute(event.target.value)}>
                {TRANSPORT_ROUTE_NAMES.map((route) => (
                  <option key={route} value={route}>
                    {route}
                  </option>
                ))}
              </select>
            </label>
            <div className="biolog-op-actions">
              <button type="submit" className="biolog-button-secondary" disabled={assignAction.status === 'loading'}>
                {assignAction.status === 'loading' ? 'Working…' : 'Assign route'}
              </button>
            </div>
            <ActionStatus action={assignAction} />
          </form>

          {assignments.length > 0 && (
            <ul className="biolog-op-history-list">
              {assignments.map((assignment) => (
                <li key={assignment.id}>
                  {assignment.personName} assigned to {assignment.routeName}.
                </li>
              ))}
            </ul>
          )}
        </aside>
      </div>

      <TransportNotificationsPanel />
    </div>
  );
}
