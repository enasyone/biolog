import { resolvePort } from '../../lib/feature-mode';
import {
  createTransportNotificationRuleMockPort,
  listTransportNotificationDeliveriesMockPort,
  listTransportNotificationRulesMockPort,
  setTransportNotificationRuleEnabledMockPort,
} from './mock';
import {
  createTransportNotificationRuleLivePort,
  listTransportNotificationDeliveriesLivePort,
  listTransportNotificationRulesLivePort,
  setTransportNotificationRuleEnabledLivePort,
} from './live';

export type {
  CreateTransportNotificationRuleInput,
  CreateTransportNotificationRuleResult,
  ListTransportNotificationDeliveriesInput,
  ListTransportNotificationDeliveriesResult,
  ListTransportNotificationRulesInput,
  ListTransportNotificationRulesResult,
  SetTransportNotificationRuleEnabledInput,
  SetTransportNotificationRuleEnabledResult,
  TransportNotificationDeliverySummary,
  TransportNotificationEventTypeId,
  TransportNotificationRuleSummary,
} from './ports';
export { KNOWN_TRANSPORT_NOTIFICATION_EVENT_TYPES } from './ports';

/**
 * BIO-TRN-003's resolved ports. Pages/components import these, never `./mock` or `./live`
 * directly (same discipline as every other module in this epic). Each is independently
 * mode-switchable via its own `NEXT_PUBLIC_FEATURE_MODE_TRANSPORTSERVICE_<KEY>` override
 * (`web/lib/feature-mode.ts`'s own `PER_PORT_ENV_DEFAULTS`), falling back to the module-wide
 * `NEXT_PUBLIC_FEATURE_MODE_TRANSPORTSERVICE` default.
 */
export const listTransportNotificationRulesPort = resolvePort(
  listTransportNotificationRulesMockPort,
  listTransportNotificationRulesLivePort,
  'listTransportNotificationRules',
);
export const createTransportNotificationRulePort = resolvePort(
  createTransportNotificationRuleMockPort,
  createTransportNotificationRuleLivePort,
  'createTransportNotificationRule',
);
export const setTransportNotificationRuleEnabledPort = resolvePort(
  setTransportNotificationRuleEnabledMockPort,
  setTransportNotificationRuleEnabledLivePort,
  'setTransportNotificationRuleEnabled',
);
export const listTransportNotificationDeliveriesPort = resolvePort(
  listTransportNotificationDeliveriesMockPort,
  listTransportNotificationDeliveriesLivePort,
  'listTransportNotificationDeliveries',
);
