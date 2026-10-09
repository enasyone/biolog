import { createMockAdapter } from '../../lib/module-port';
import type {
  CreateWebhookSubscriptionPort,
  CreateWebhookSubscriptionResult,
  ListWebhookSubscriptionsPort,
  SetWebhookSubscriptionEnabledPort,
  WebhookSubscriptionSummary,
} from './ports';

/**
 * Deterministic in-memory stand-in for the real `/api/v1/integrations/webhooks` backend
 * (BIO-INT-004-AC01) - reproducible for local testing and demos, same shape as every other mock
 * adapter in this epic.
 */
let nextSubscriptionNumber = 1;
const subscriptionsByTenant = new Map<string, Map<string, WebhookSubscriptionSummary>>();

function tenantSubscriptions(tenantId: string): Map<string, WebhookSubscriptionSummary> {
  let subscriptions = subscriptionsByTenant.get(tenantId);
  if (!subscriptions) {
    subscriptions = new Map();
    subscriptionsByTenant.set(tenantId, subscriptions);
  }
  return subscriptions;
}

function mockSigningSecret(): string {
  return `mock-signing-secret-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}

const listBase = createMockAdapter('IntegrationService');
export const listWebhookSubscriptionsMockPort: ListWebhookSubscriptionsPort = {
  ...listBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No tenant was provided.' };
    return { outcome: 'success', message: 'Webhook subscriptions loaded (SIMULATION).', subscriptions: [...tenantSubscriptions(input.tenantId).values()] };
  },
};

const createBase = createMockAdapter('IntegrationService');
export const createWebhookSubscriptionMockPort: CreateWebhookSubscriptionPort = {
  ...createBase,
  async primaryAction(input): Promise<CreateWebhookSubscriptionResult> {
    if (!input) return { outcome: 'failure', message: 'No webhook subscription definition was provided.' };
    if (!input.url.trim()) {
      return { outcome: 'failure', message: 'A URL is required (SIMULATION).' };
    }
    if (input.eventTypes.length === 0) {
      return { outcome: 'failure', message: 'At least one event type is required (SIMULATION).' };
    }
    const id = `mock-whs-${nextSubscriptionNumber++}`;
    const subscription: WebhookSubscriptionSummary = {
      id,
      url: input.url,
      eventTypes: input.eventTypes,
      enabled: true,
      version: 1,
      createdAt: new Date().toISOString(),
    };
    tenantSubscriptions(input.tenantId).set(id, subscription);
    return { outcome: 'success', message: 'Webhook subscription created (SIMULATION) - copy the signing secret now, it will not be shown again.', subscription, signingSecret: mockSigningSecret() };
  },
};

const setEnabledBase = createMockAdapter('IntegrationService');
export const setWebhookSubscriptionEnabledMockPort: SetWebhookSubscriptionEnabledPort = {
  ...setEnabledBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No webhook subscription was provided.' };
    const subscriptions = tenantSubscriptions(input.tenantId);
    const current = subscriptions.get(input.id);
    if (!current) return { outcome: 'failure', message: 'Unknown webhook subscription (SIMULATION).' };
    const updated: WebhookSubscriptionSummary = { ...current, enabled: input.enabled, version: current.version + 1 };
    subscriptions.set(input.id, updated);
    return { outcome: 'success', message: `Webhook subscription ${input.enabled ? 'enabled' : 'disabled'} (SIMULATION).`, subscription: updated };
  },
};
