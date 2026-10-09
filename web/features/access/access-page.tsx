'use client';

import { useMemo, useState } from 'react';
import { ActionStatus, type ActionState } from '../../components/action-status';
import { NotificationList } from '../../components/notification-list';
import { OPERATIONAL_SCENARIO_OPTIONS, ScenarioPicker } from '../../components/scenario-picker';
import { StatusBadge } from '../../components/status-badge';
import { useSession } from '../../context/session-context';
import { useTestScenarioPreset } from '../../dev-tools/use-test-scenario-preset';
import {
  ACCESS_REASON_CODES,
  ASSURANCE_LEVELS,
  DEFAULT_SEED,
  deriveNotifications,
  generateDataset,
  type AccessReasonCode,
  type AssuranceLevel,
  type ScenarioId,
} from '../../lib/synthetic-data';
import { accessPort, accessViewAccessEventsPort } from './index';

interface SimulatedPolicy {
  id: string;
  zone: string;
  assuranceLevel: AssuranceLevel;
  reasonCode: AccessReasonCode;
}

/**
 * Access Control page (BIO-UX-010-AC01, AC02, AC03, AC05).
 *
 * Replaces the BIO-UX-002/003 `PlaceholderPage` shell for `/access-control`
 * with real content: a policy-builder form (AC03 — simulates an assurance-
 * level decision and reason code), a live access-events list sourced from
 * `web/lib/synthetic-data`, and the shared scenario picker (AC02). Every
 * button calls `accessPort`/`accessViewAccessEventsPort` for its network-
 * outcome banner (AC05 — a stable port a later live service replaces
 * without this page changing); the policy itself and the event list are
 * page-level synthetic state, not something the port needs to transport
 * (see `ports.ts`'s header comment for the full reasoning).
 */
export function AccessControlPage() {
  const { tenant } = useSession();
  const [scenarioId, setScenarioId] = useTestScenarioPreset<ScenarioId>(
    'scenario',
    OPERATIONAL_SCENARIO_OPTIONS.map((option) => option.id),
    'normal',
  );
  const dataset = useMemo(() => generateDataset(DEFAULT_SEED, scenarioId), [scenarioId]);
  const notifications = useMemo(() => deriveNotifications(dataset), [dataset]);
  const tenantEvents = useMemo(
    () => dataset.accessEvents.filter((event) => event.tenantId === tenant.id),
    [dataset, tenant.id],
  );
  const tenantPeople = useMemo(() => dataset.people.filter((p) => p.tenantId === tenant.id), [dataset, tenant.id]);
  const tenantDevices = useMemo(() => dataset.devices.filter((d) => d.tenantId === tenant.id), [dataset, tenant.id]);

  const [zone, setZone] = useState('Main Gate');
  const [assuranceLevel, setAssuranceLevel] = useState<AssuranceLevel>('card');
  const [reasonCode, setReasonCode] = useState<AccessReasonCode>('after-hours');
  const [policies, setPolicies] = useState<readonly SimulatedPolicy[]>([]);
  const [createAction, setCreateAction] = useState<ActionState>({ status: 'idle' });

  const [eventsVisible, setEventsVisible] = useState(false);
  const [viewEventsAction, setViewEventsAction] = useState<ActionState>({ status: 'idle' });

  function personName(personId: string): string {
    return tenantPeople.find((p) => p.id === personId)?.displayName ?? personId;
  }
  function deviceName(deviceId: string): string {
    return tenantDevices.find((d) => d.id === deviceId)?.name ?? deviceId;
  }

  async function createPolicy() {
    setCreateAction({ status: 'loading' });
    const result = await accessPort.primaryAction();
    setCreateAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setPolicies((prev) => [
        ...prev,
        { id: `policy-${prev.length}-${Date.now()}`, zone, assuranceLevel, reasonCode },
      ]);
    }
  }

  async function viewAccessEvents() {
    setViewEventsAction({ status: 'loading' });
    const result = await accessViewAccessEventsPort.primaryAction();
    setViewEventsAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setEventsVisible(true);
    }
  }

  return (
    <div className="biolog-placeholder-page">
      <div className="biolog-op-page-header">
        <div>
          <h1>Access Control</h1>
          <p className="biolog-placeholder-description">
            Access policy, assurance levels and access events for {tenant.name}.
          </p>
        </div>
        <ScenarioPicker value={scenarioId} onChange={setScenarioId} options={OPERATIONAL_SCENARIO_OPTIONS} />
      </div>

      <NotificationList notifications={notifications} />

      <div className="biolog-op-grid">
        <section className="biolog-card">
          <h2>Create policy</h2>
          <p>Simulate an assurance-level decision and reason code for a zone (AC03).</p>
          <form
            className="biolog-op-form"
            onSubmit={(event) => {
              event.preventDefault();
              void createPolicy();
            }}
          >
            <label className="biolog-op-field">
              <span>Zone</span>
              <input
                aria-label="Zone"
                value={zone}
                onChange={(event) => setZone(event.target.value)}
                required
              />
            </label>
            <label className="biolog-op-field">
              <span>Assurance level</span>
              <select
                aria-label="Assurance level"
                value={assuranceLevel}
                onChange={(event) => setAssuranceLevel(event.target.value as AssuranceLevel)}
              >
                {ASSURANCE_LEVELS.map((level) => (
                  <option key={level} value={level}>
                    {level}
                  </option>
                ))}
              </select>
            </label>
            <label className="biolog-op-field">
              <span>Reason code</span>
              <select
                aria-label="Reason code"
                value={reasonCode}
                onChange={(event) => setReasonCode(event.target.value as AccessReasonCode)}
              >
                {ACCESS_REASON_CODES.map((code) => (
                  <option key={code} value={code}>
                    {code}
                  </option>
                ))}
              </select>
            </label>
            <div className="biolog-op-actions">
              <button type="submit" className="biolog-button-primary" disabled={createAction.status === 'loading'}>
                {createAction.status === 'loading' ? 'Working…' : 'Create policy'}
              </button>
            </div>
            <ActionStatus action={createAction} />
          </form>

          {policies.length > 0 && (
            <table className="biolog-table">
              <caption>Simulated policies created this session</caption>
              <thead>
                <tr>
                  <th scope="col">Zone</th>
                  <th scope="col">Assurance level</th>
                  <th scope="col">Reason code</th>
                </tr>
              </thead>
              <tbody>
                {policies.map((policy) => (
                  <tr key={policy.id}>
                    <td>{policy.zone}</td>
                    <td>{policy.assuranceLevel}</td>
                    <td>{policy.reasonCode}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>

        <aside className="biolog-card">
          <h2>Access events</h2>
          <div className="biolog-op-actions">
            <button
              type="button"
              className="biolog-button-secondary"
              onClick={() => void viewAccessEvents()}
              disabled={viewEventsAction.status === 'loading'}
            >
              {viewEventsAction.status === 'loading' ? 'Working…' : 'View access events'}
            </button>
          </div>
          <ActionStatus action={viewEventsAction} />

          {eventsVisible &&
            (tenantEvents.length === 0 ? (
              <p className="biolog-op-empty">No access events for this scenario.</p>
            ) : (
              <table className="biolog-table">
                <caption>Recent access events</caption>
                <thead>
                  <tr>
                    <th scope="col">Person</th>
                    <th scope="col">Device</th>
                    <th scope="col">Decision</th>
                    <th scope="col">Reason</th>
                  </tr>
                </thead>
                <tbody>
                  {tenantEvents.map((event) => (
                    <tr key={event.id}>
                      <td>{personName(event.personId)}</td>
                      <td>{deviceName(event.deviceId)}</td>
                      <td>
                        <StatusBadge tone={event.decision === 'granted' ? 'success' : 'error'}>
                          {event.decision}
                        </StatusBadge>
                      </td>
                      <td>{event.reasonCode ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ))}
        </aside>
      </div>
    </div>
  );
}
