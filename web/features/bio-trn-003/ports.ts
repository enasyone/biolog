import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * BIO-TRN-003's own dedicated connection: tenant-scoped parent/system transport notification
 * rules (select board/alight/missed-alight/exception events, AC01) and their visible delivery
 * trail (AC03), backing a new "Transport notifications" panel on `/transport` mounted ALONGSIDE
 * the existing boarding/alighting log and route-assignment panels (never replacing either) - the
 * same "new keys, new panel, existing panel untouched" precedent BIO-INT-004's own webhooks panel
 * already established on `/administration/integrations`.
 */
export type TransportNotificationEventTypeId = 'board' | 'alight' | 'missed_alight' | 'exception';

export const KNOWN_TRANSPORT_NOTIFICATION_EVENT_TYPES: readonly TransportNotificationEventTypeId[] = [
  'board',
  'alight',
  'missed_alight',
  'exception',
];

export interface TransportNotificationRuleSummary {
  id: string;
  name: string;
  eventTypes: readonly TransportNotificationEventTypeId[];
  enabled: boolean;
  version: number;
  createdAt: string;
}

/** AC02 - already minimized: never carries the richer raw fields (location/vehicle) the backend's own redactor drops before this ever reaches the wire (see `docs/features/bio-trn-003/README.md`). */
export interface TransportNotificationDeliverySummary {
  deliveryId: string;
  ruleId: string;
  sourceEventId: string;
  personId: string;
  routeId: string | null;
  eventType: TransportNotificationEventTypeId;
  occurredAt: string;
  outcome: 'delivered' | 'dead_lettered';
  attemptCount: number;
  detail: string;
  recordedAt: string;
}

export interface ListTransportNotificationRulesInput {
  tenantId: string;
}

export interface ListTransportNotificationRulesResult extends PrimaryActionResult {
  rules?: readonly TransportNotificationRuleSummary[];
}

export interface CreateTransportNotificationRuleInput {
  tenantId: string;
  name: string;
  eventTypes: readonly TransportNotificationEventTypeId[];
}

export interface CreateTransportNotificationRuleResult extends PrimaryActionResult {
  rule?: TransportNotificationRuleSummary;
}

export interface SetTransportNotificationRuleEnabledInput {
  tenantId: string;
  id: string;
  enabled: boolean;
  expectedVersion: number;
}

export interface SetTransportNotificationRuleEnabledResult extends PrimaryActionResult {
  rule?: TransportNotificationRuleSummary;
}

export interface ListTransportNotificationDeliveriesInput {
  tenantId: string;
}

export interface ListTransportNotificationDeliveriesResult extends PrimaryActionResult {
  deliveries?: readonly TransportNotificationDeliverySummary[];
}

export interface ListTransportNotificationRulesPort extends ModulePort {
  primaryAction(input?: ListTransportNotificationRulesInput): Promise<ListTransportNotificationRulesResult>;
}

export interface CreateTransportNotificationRulePort extends ModulePort {
  primaryAction(input?: CreateTransportNotificationRuleInput): Promise<CreateTransportNotificationRuleResult>;
}

export interface SetTransportNotificationRuleEnabledPort extends ModulePort {
  primaryAction(input?: SetTransportNotificationRuleEnabledInput): Promise<SetTransportNotificationRuleEnabledResult>;
}

export interface ListTransportNotificationDeliveriesPort extends ModulePort {
  primaryAction(input?: ListTransportNotificationDeliveriesInput): Promise<ListTransportNotificationDeliveriesResult>;
}
