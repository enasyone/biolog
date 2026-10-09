import type {
  CreateWebhookSubscriptionPort,
  ListWebhookSubscriptionsPort,
  SetWebhookSubscriptionEnabledPort,
  WebhookEventTypeId,
  WebhookSubscriptionSummary,
} from './ports';

/**
 * Live adapters for BIO-INT-004's real `/api/v1/integrations/webhooks/*` backend
 * (`BioInt004Module.cs`) - genuinely connected, not a stub. No tenant header is sent; this
 * host resolves tenancy from the request's own Host channel (ADR-0002 Decision 1.3,
 * `TenantContextMiddleware`), the same convention every other live adapter in this epic
 * already follows (see `web/features/bio-int-003/live.ts`).
 */
const ACTOR_HEADERS = { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' } as const;
const BASE_PATH = '/api/v1/integrations/webhooks';

interface WebhookSubscriptionWire {
  id: string;
  url: string;
  eventTypes: readonly WebhookEventTypeId[];
  enabled: boolean;
  version: number;
  createdAt: string;
  signingSecret?: string | null;
}

function fromWire(wire: WebhookSubscriptionWire): WebhookSubscriptionSummary {
  return {
    id: wire.id,
    url: wire.url,
    eventTypes: wire.eventTypes,
    enabled: wire.enabled,
    version: wire.version,
    createdAt: wire.createdAt,
  };
}

async function problemMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as { title?: string; detail?: string };
    return body.detail ?? body.title ?? fallback;
  } catch {
    return fallback;
  }
}

export const listWebhookSubscriptionsLivePort: ListWebhookSubscriptionsPort = {
  module: 'IntegrationService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No tenant was provided.' };
    try {
      const response = await fetch(BASE_PATH, { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not load webhook subscriptions: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as { data: WebhookSubscriptionWire[] };
      return { outcome: 'success', message: 'Webhook subscriptions loaded.', subscriptions: body.data.map(fromWire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Webhooks service - device or network appears offline.' };
    }
  },
};

export const createWebhookSubscriptionLivePort: CreateWebhookSubscriptionPort = {
  module: 'IntegrationService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No webhook subscription definition was provided.' };
    try {
      const response = await fetch(BASE_PATH, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ url: input.url, eventTypes: input.eventTypes }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not create webhook subscription: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as WebhookSubscriptionWire;
      return {
        outcome: 'success',
        message: 'Webhook subscription created - copy the signing secret now, it will not be shown again.',
        subscription: fromWire(wire),
        signingSecret: wire.signingSecret ?? undefined,
      };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Webhooks service - device or network appears offline.' };
    }
  },
};

export const setWebhookSubscriptionEnabledLivePort: SetWebhookSubscriptionEnabledPort = {
  module: 'IntegrationService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No webhook subscription was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/${input.id}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ enabled: input.enabled, version: input.expectedVersion }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not update webhook subscription: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as WebhookSubscriptionWire;
      return { outcome: 'success', message: `Webhook subscription ${wire.enabled ? 'enabled' : 'disabled'}.`, subscription: fromWire(wire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Webhooks service - device or network appears offline.' };
    }
  },
};
