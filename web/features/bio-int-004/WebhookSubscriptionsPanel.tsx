'use client';

import { useCallback, useEffect, useState } from 'react';
import { useSession } from '../../context/session-context';
import {
  KNOWN_WEBHOOK_EVENT_TYPES,
  createWebhookSubscriptionPort,
  listWebhookSubscriptionsPort,
  setWebhookSubscriptionEnabledPort,
} from './index';
import type { WebhookEventTypeId, WebhookSubscriptionSummary } from './index';

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'offline'; message: string }
  | { status: 'ready'; subscriptions: readonly WebhookSubscriptionSummary[] };

/**
 * BIO-INT-004-AC05: the real "Webhooks" panel on `/administration/integrations`, mounted
 * ALONGSIDE BIO-INT-002's `ImportConsole` and BIO-INT-003's `ServiceAccountsPanel` (never
 * replacing either - see `web/app/administration/integrations/page.tsx`). Create a
 * tenant-scoped, event-filtered webhook subscription, see its one-time signing secret, or
 * enable/disable it. SYNTHETIC data only.
 */
export function WebhookSubscriptionsPanel() {
  const session = useSession();
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [working, setWorking] = useState(false);
  // SY-INT004-04 (Seyi, acceptance re-review): a create/toggle NOTICE now carries its own
  // success/failure distinction, rendered with role="alert" only when it is one - mirroring the
  // SAME role="alert" pattern this panel's own load-failure states (`error`/`offline` below)
  // already use, and the pattern every other panel in this codebase already establishes for a
  // visible failure. Before this, a failed create/toggle rendered through the IDENTICAL plain
  // <p> a successful one did - indistinguishable to an assistive-technology user, and nothing a
  // test could tell apart from a silently swallowed error either.
  const [notice, setNotice] = useState<{ message: string; isError: boolean } | null>(null);
  const [revealedSecret, setRevealedSecret] = useState<{ subscriptionId: string; secret: string } | null>(null);

  const [newUrl, setNewUrl] = useState('https://example-synthetic.test/webhooks/inbound');
  const [newEventTypes, setNewEventTypes] = useState<readonly WebhookEventTypeId[]>(['document_session']);

  const load = useCallback(() => {
    setState({ status: 'loading' });
    listWebhookSubscriptionsPort
      .primaryAction({ tenantId: session.tenant.id })
      .then((result) => {
        if (result.outcome === 'offline') {
          setState({ status: 'offline', message: result.message });
        } else if (result.outcome === 'failure') {
          setState({ status: 'error', message: result.message });
        } else {
          setState({ status: 'ready', subscriptions: result.subscriptions ?? [] });
        }
      })
      .catch(() => setState({ status: 'error', message: 'Could not load webhook subscriptions - try again.' }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session.tenant.id]);

  useEffect(() => {
    load();
  }, [load]);

  function toggleEventType(eventType: WebhookEventTypeId) {
    setNewEventTypes((current) => (current.includes(eventType) ? current.filter((t) => t !== eventType) : [...current, eventType]));
  }

  async function handleCreate() {
    setWorking(true);
    setNotice(null);
    setRevealedSecret(null);
    try {
      const result = await createWebhookSubscriptionPort.primaryAction({
        tenantId: session.tenant.id,
        url: newUrl,
        eventTypes: newEventTypes,
      });
      setNotice({ message: result.message, isError: result.outcome !== 'success' });
      if (result.outcome === 'success' && result.subscription && result.signingSecret) {
        setRevealedSecret({ subscriptionId: result.subscription.id, secret: result.signingSecret });
        load();
      }
    } finally {
      setWorking(false);
    }
  }

  async function handleToggleEnabled(subscription: WebhookSubscriptionSummary) {
    setWorking(true);
    setNotice(null);
    setRevealedSecret(null);
    try {
      const result = await setWebhookSubscriptionEnabledPort.primaryAction({
        tenantId: session.tenant.id,
        id: subscription.id,
        enabled: !subscription.enabled,
        expectedVersion: subscription.version,
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
    <section className="biolog-card" data-testid="webhook-subscriptions-panel">
      <h2>Webhooks</h2>
      <p className="biolog-placeholder-description">
        Signed, retryable outbound webhooks for selected events (BIO-INT-004). SYNTHETIC data only.
      </p>

      {state.status === 'loading' && <p role="status">Loading…</p>}
      {state.status === 'error' && <p role="alert">{state.message}</p>}
      {state.status === 'offline' && <p role="alert">{state.message}</p>}

      {state.status === 'ready' && (
        <table className="biolog-table" aria-label="Webhook subscriptions" data-testid="webhook-subscriptions-table">
          <thead>
            <tr>
              <th>URL</th>
              <th>Event types</th>
              <th>Status</th>
              <th>
                <span className="biolog-visually-hidden">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {state.subscriptions.length === 0 && (
              <tr>
                <td colSpan={4} data-testid="webhook-subscriptions-empty">No webhook subscriptions yet for this tenant.</td>
              </tr>
            )}
            {state.subscriptions.map((subscription) => (
              <tr key={subscription.id} data-testid="webhook-subscription-row">
                <td>
                  <code>{subscription.url}</code>
                </td>
                <td>{subscription.eventTypes.join(', ')}</td>
                <td data-testid="webhook-subscription-status">{subscription.enabled ? 'Enabled' : 'Disabled'}</td>
                <td>
                  <button
                    type="button"
                    disabled={working}
                    data-testid="webhook-subscription-toggle"
                    onClick={() => handleToggleEnabled(subscription)}
                  >
                    {subscription.enabled ? 'Disable' : 'Enable'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {revealedSecret && (
        <p role="alert" data-testid="webhook-subscription-secret">
          New signing secret for <code>{revealedSecret.subscriptionId}</code>: <code>{revealedSecret.secret}</code> - copy
          it now, it will not be shown again.
        </p>
      )}

      <h3>Create a webhook subscription</h3>
      <div className="biolog-identity-toolbar">
        <input
          type="url"
          aria-label="Webhook URL"
          data-testid="webhook-subscription-url-input"
          value={newUrl}
          onChange={(e) => setNewUrl(e.target.value)}
        />
        <fieldset>
          <legend>Event types</legend>
          {KNOWN_WEBHOOK_EVENT_TYPES.map((eventType) => (
            <label key={eventType}>
              <input
                type="checkbox"
                data-testid={`webhook-subscription-event-type-${eventType}`}
                checked={newEventTypes.includes(eventType)}
                onChange={() => toggleEventType(eventType)}
              />
              {eventType}
            </label>
          ))}
        </fieldset>
        <button
          type="button"
          className="biolog-button-primary"
          data-testid="webhook-subscription-create"
          disabled={working || newEventTypes.length === 0}
          onClick={handleCreate}
        >
          Create webhook subscription
        </button>
      </div>

      {notice && (
        <p role={notice.isError ? 'alert' : undefined} data-testid="webhook-subscription-notice">
          {notice.message}
        </p>
      )}
    </section>
  );
}
