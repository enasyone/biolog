import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * BIO-INT-004's own dedicated connection: tenant-scoped outbound webhook subscriptions
 * (create/list/enable-disable, AC01), backing a new "Webhooks" panel on
 * `/administration/integrations` mounted ALONGSIDE BIO-INT-002's `ImportConsole` and
 * BIO-INT-003's `ServiceAccountsPanel` (never replacing either) - same precedent
 * `web/features/bio-int-003/ports.ts` already documents for its own new ports next to
 * BIO-INT-002's existing one. Signing/replay protection (AC03) and retry/dead-letter/audit
 * (AC04) are this story's own real backend behaviour but have no UI surface of their own -
 * route-manifest.ts names no "view delivery log"/"resend" action for either, so there is
 * nothing for the web layer to wire; they are exercised directly against the real
 * `IDomainEventBus` subscription and characterised in
 * tests/features/bio-int-004/WebhookDispatchServiceTests.cs.
 */
export type WebhookEventTypeId = 'tenant' | 'person' | 'credential' | 'device' | 'document_session';

export const KNOWN_WEBHOOK_EVENT_TYPES: readonly WebhookEventTypeId[] = ['tenant', 'person', 'credential', 'device', 'document_session'];

export interface WebhookSubscriptionSummary {
  id: string;
  url: string;
  eventTypes: readonly WebhookEventTypeId[];
  enabled: boolean;
  version: number;
  createdAt: string;
}

export interface ListWebhookSubscriptionsInput {
  tenantId: string;
}

export interface ListWebhookSubscriptionsResult extends PrimaryActionResult {
  subscriptions?: readonly WebhookSubscriptionSummary[];
}

export interface CreateWebhookSubscriptionInput {
  tenantId: string;
  url: string;
  eventTypes: readonly WebhookEventTypeId[];
}

export interface CreateWebhookSubscriptionResult extends PrimaryActionResult {
  subscription?: WebhookSubscriptionSummary;
  /** Shown to the caller exactly once (same "plaintext shown once" discipline BIO-INT-003's own service-account secret established) - never retrievable again. */
  signingSecret?: string;
}

export interface SetWebhookSubscriptionEnabledInput {
  tenantId: string;
  id: string;
  enabled: boolean;
  expectedVersion: number;
}

export interface SetWebhookSubscriptionEnabledResult extends PrimaryActionResult {
  subscription?: WebhookSubscriptionSummary;
}

export interface ListWebhookSubscriptionsPort extends ModulePort {
  primaryAction(input?: ListWebhookSubscriptionsInput): Promise<ListWebhookSubscriptionsResult>;
}

export interface CreateWebhookSubscriptionPort extends ModulePort {
  primaryAction(input?: CreateWebhookSubscriptionInput): Promise<CreateWebhookSubscriptionResult>;
}

export interface SetWebhookSubscriptionEnabledPort extends ModulePort {
  primaryAction(input?: SetWebhookSubscriptionEnabledInput): Promise<SetWebhookSubscriptionEnabledResult>;
}
