import { resolvePort } from '../../lib/feature-mode';
import { createWebhookSubscriptionMockPort, listWebhookSubscriptionsMockPort, setWebhookSubscriptionEnabledMockPort } from './mock';
import { createWebhookSubscriptionLivePort, listWebhookSubscriptionsLivePort, setWebhookSubscriptionEnabledLivePort } from './live';

export type {
  CreateWebhookSubscriptionInput,
  CreateWebhookSubscriptionResult,
  ListWebhookSubscriptionsInput,
  ListWebhookSubscriptionsResult,
  SetWebhookSubscriptionEnabledInput,
  SetWebhookSubscriptionEnabledResult,
  WebhookEventTypeId,
  WebhookSubscriptionSummary,
} from './ports';
export { KNOWN_WEBHOOK_EVENT_TYPES } from './ports';

/**
 * BIO-INT-004's resolved ports. Pages/components import these, never `./mock` or `./live`
 * directly (same discipline as every other module in this epic). Each is independently
 * mode-switchable via its own `NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE_<KEY>` override
 * (`web/lib/feature-mode.ts`'s own `PER_PORT_ENV_DEFAULTS`), falling back to the module-wide
 * `NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE` default.
 */
export const listWebhookSubscriptionsPort = resolvePort(listWebhookSubscriptionsMockPort, listWebhookSubscriptionsLivePort, 'listWebhookSubscriptions');
export const createWebhookSubscriptionPort = resolvePort(createWebhookSubscriptionMockPort, createWebhookSubscriptionLivePort, 'createWebhookSubscription');
export const setWebhookSubscriptionEnabledPort = resolvePort(
  setWebhookSubscriptionEnabledMockPort,
  setWebhookSubscriptionEnabledLivePort,
  'setWebhookSubscriptionEnabled',
);
