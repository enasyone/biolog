'use client';

import { useCallback, useEffect, useState } from 'react';
import { useSession } from '../../context/session-context';
import {
  KNOWN_TRANSPORT_NOTIFICATION_EVENT_TYPES,
  createTransportNotificationRulePort,
  listTransportNotificationDeliveriesPort,
  listTransportNotificationRulesPort,
  setTransportNotificationRuleEnabledPort,
} from './index';
import type { TransportNotificationDeliverySummary, TransportNotificationEventTypeId, TransportNotificationRuleSummary } from './index';

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'offline'; message: string }
  | { status: 'ready'; rules: readonly TransportNotificationRuleSummary[] };

const EVENT_TYPE_LABEL: Record<TransportNotificationEventTypeId, string> = {
  board: 'Board',
  alight: 'Alight',
  missed_alight: 'Missed alight',
  exception: 'Exception',
};

/**
 * BIO-TRN-003-AC05: the real "Transport notifications" panel on `/transport`, mounted ALONGSIDE
 * the existing boarding/alighting log and route-assignment sections (never replacing either - see
 * `web/features/transport/transport-page.tsx`). Configure a tenant-scoped notification rule
 * (select which of board/alight/missed-alight/exception to notify on, AC01) and see the delivery
 * trail with any delivery failure rendered as a genuinely distinct, accessible state (AC03) - the
 * same `role="alert"`-on-failure convention this codebase already establishes (see
 * `web/features/bio-int-004/WebhookSubscriptionsPanel.tsx`'s own SY-INT004-04 fix for why a plain
 * `<p>` identical for success and failure is not sufficient). SYNTHETIC data only.
 */
export function TransportNotificationsPanel() {
  const session = useSession();
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [working, setWorking] = useState(false);
  const [notice, setNotice] = useState<{ message: string; isError: boolean } | null>(null);
  const [deliveries, setDeliveries] = useState<readonly TransportNotificationDeliverySummary[]>([]);

  const [newName, setNewName] = useState('Board alerts');
  const [newEventTypes, setNewEventTypes] = useState<readonly TransportNotificationEventTypeId[]>(['board']);

  const load = useCallback(() => {
    setState({ status: 'loading' });
    listTransportNotificationRulesPort
      .primaryAction({ tenantId: session.tenant.id })
      .then((result) => {
        if (result.outcome === 'offline') {
          setState({ status: 'offline', message: result.message });
        } else if (result.outcome === 'failure') {
          setState({ status: 'error', message: result.message });
        } else {
          setState({ status: 'ready', rules: result.rules ?? [] });
        }
      })
      .catch(() => setState({ status: 'error', message: 'Could not load transport notification rules - try again.' }));

    listTransportNotificationDeliveriesPort
      .primaryAction({ tenantId: session.tenant.id })
      .then((result) => {
        if (result.outcome === 'success') {
          setDeliveries(result.deliveries ?? []);
        }
      })
      .catch(() => undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session.tenant.id]);

  useEffect(() => {
    load();
  }, [load]);

  function toggleEventType(eventType: TransportNotificationEventTypeId) {
    setNewEventTypes((current) => (current.includes(eventType) ? current.filter((t) => t !== eventType) : [...current, eventType]));
  }

  async function handleCreate() {
    setWorking(true);
    setNotice(null);
    try {
      const result = await createTransportNotificationRulePort.primaryAction({
        tenantId: session.tenant.id,
        name: newName,
        eventTypes: newEventTypes,
      });
      setNotice({ message: result.message, isError: result.outcome !== 'success' });
      if (result.outcome === 'success') {
        load();
      }
    } finally {
      setWorking(false);
    }
  }

  async function handleToggleEnabled(rule: TransportNotificationRuleSummary) {
    setWorking(true);
    setNotice(null);
    try {
      const result = await setTransportNotificationRuleEnabledPort.primaryAction({
        tenantId: session.tenant.id,
        id: rule.id,
        enabled: !rule.enabled,
        expectedVersion: rule.version,
      });
      setNotice({ message: result.message, isError: result.outcome !== 'success' });
      if (result.outcome === 'success') {
        load();
      }
    } finally {
      setWorking(false);
    }
  }

  return (
    <section className="biolog-card" data-testid="transport-notifications-panel">
      <h2>Transport notifications</h2>
      <p className="biolog-placeholder-description">
        Parent/system notification rules for boarding, alighting, missed-alight and exception events (BIO-TRN-003). SYNTHETIC data only.
      </p>

      {state.status === 'loading' && <p role="status">Loading…</p>}
      {state.status === 'error' && <p role="alert">{state.message}</p>}
      {state.status === 'offline' && <p role="alert">{state.message}</p>}

      {state.status === 'ready' && (
        <table className="biolog-table" aria-label="Transport notification rules" data-testid="transport-notification-rules-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Event types</th>
              <th>Status</th>
              <th>
                <span className="biolog-visually-hidden">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {state.rules.length === 0 && (
              <tr>
                <td colSpan={4} data-testid="transport-notification-rules-empty">No transport notification rules yet for this tenant.</td>
              </tr>
            )}
            {state.rules.map((rule) => (
              <tr key={rule.id} data-testid="transport-notification-rule-row">
                <td>{rule.name}</td>
                <td>{rule.eventTypes.map((t) => EVENT_TYPE_LABEL[t]).join(', ')}</td>
                <td data-testid="transport-notification-rule-status">{rule.enabled ? 'Enabled' : 'Disabled'}</td>
                <td>
                  <button
                    type="button"
                    disabled={working}
                    data-testid="transport-notification-rule-toggle"
                    onClick={() => handleToggleEnabled(rule)}
                  >
                    {rule.enabled ? 'Disable' : 'Enable'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h3>Create a notification rule</h3>
      <div className="biolog-identity-toolbar">
        <input
          type="text"
          aria-label="Rule name"
          data-testid="transport-notification-rule-name-input"
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
        />
        <fieldset>
          <legend>Event types</legend>
          {KNOWN_TRANSPORT_NOTIFICATION_EVENT_TYPES.map((eventType) => (
            <label key={eventType}>
              <input
                type="checkbox"
                data-testid={`transport-notification-event-type-${eventType}`}
                checked={newEventTypes.includes(eventType)}
                onChange={() => toggleEventType(eventType)}
              />
              {EVENT_TYPE_LABEL[eventType]}
            </label>
          ))}
        </fieldset>
        <button
          type="button"
          className="biolog-button-primary"
          data-testid="transport-notification-rule-create"
          disabled={working || newEventTypes.length === 0}
          onClick={handleCreate}
        >
          Create notification rule
        </button>
      </div>

      {notice && (
        <p role={notice.isError ? 'alert' : undefined} data-testid="transport-notification-rule-notice">
          {notice.message}
        </p>
      )}

      <h3>Recent deliveries</h3>
      {deliveries.length === 0 ? (
        <p data-testid="transport-notification-deliveries-empty">No deliveries yet - create a rule above to see matching events delivered here.</p>
      ) : (
        <table className="biolog-table" aria-label="Transport notification deliveries" data-testid="transport-notification-deliveries-table">
          <thead>
            <tr>
              <th>Person</th>
              <th>Event type</th>
              <th>Outcome</th>
              <th>Detail</th>
            </tr>
          </thead>
          <tbody>
            {deliveries.map((delivery) => (
              <tr key={delivery.deliveryId} data-testid="transport-notification-delivery-row">
                <td>{delivery.personId}</td>
                <td>{EVENT_TYPE_LABEL[delivery.eventType]}</td>
                <td>
                  <span
                    role={delivery.outcome === 'dead_lettered' ? 'alert' : undefined}
                    data-testid="transport-notification-delivery-outcome"
                  >
                    {delivery.outcome === 'dead_lettered' ? 'Failed' : 'Delivered'}
                  </span>
                </td>
                <td>{delivery.detail}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
