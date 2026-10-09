import { createMockAdapter } from '../../lib/module-port';
import type {
  CreateTransportNotificationRulePort,
  CreateTransportNotificationRuleResult,
  ListTransportNotificationDeliveriesPort,
  ListTransportNotificationRulesPort,
  SetTransportNotificationRuleEnabledPort,
  TransportNotificationDeliverySummary,
  TransportNotificationEventTypeId,
  TransportNotificationRuleSummary,
} from './ports';

/**
 * Deterministic in-memory stand-in for the real `/api/v1/transport-notifications/*` backend
 * (BIO-TRN-003-AC01/AC03) - reproducible for local testing and demos, same shape as every other
 * mock adapter in this epic.
 *
 * A fixed synthetic board/alight/missed-alight/exception feed stands in for bio-trn-002's own
 * real captured events (this story's own backend reads that real feed - see
 * `docs/features/bio-trn-003/README.md` "AC05 — web wiring"). One feed item
 * (`mock-evt-exception-1`) always dead-letters - a deliberate, disclosed, deterministic failure
 * fixture standing in for a genuine channel delivery failure (AC03's own "visible" requirement),
 * not an arbitrary trigger: it represents the same real dead-letter outcome the backend's own
 * bounded-retry dispatch produces when a send keeps failing
 * (`TransportNotificationDispatchServiceTests.cs`'s own
 * `A_send_that_keeps_failing_is_dead_lettered_after_the_retry_budget_is_exhausted`).
 */
interface MockFeedItem {
  sourceEventId: string;
  personId: string;
  routeId: string | null;
  eventType: TransportNotificationEventTypeId;
  occurredAt: string;
}

const SYNTHETIC_FEED: readonly MockFeedItem[] = [
  { sourceEventId: 'mock-evt-board-1', personId: 'per_mock_student_1', routeId: 'mock-route-a', eventType: 'board', occurredAt: '2026-10-07T07:45:00Z' },
  { sourceEventId: 'mock-evt-alight-1', personId: 'per_mock_student_1', routeId: 'mock-route-a', eventType: 'alight', occurredAt: '2026-10-07T08:15:00Z' },
  { sourceEventId: 'mock-evt-missed-1', personId: 'per_mock_student_2', routeId: null, eventType: 'missed_alight', occurredAt: '2026-10-07T08:20:00Z' },
  { sourceEventId: 'mock-evt-exception-1', personId: 'per_mock_student_3', routeId: 'mock-route-b', eventType: 'exception', occurredAt: '2026-10-07T07:50:00Z' },
];

let nextRuleNumber = 1;
const rulesByTenant = new Map<string, Map<string, TransportNotificationRuleSummary>>();
const deliveredSourceEventsByTenant = new Map<string, Set<string>>();
const deliveriesByTenant = new Map<string, TransportNotificationDeliverySummary[]>();

function tenantRules(tenantId: string): Map<string, TransportNotificationRuleSummary> {
  let rules = rulesByTenant.get(tenantId);
  if (!rules) {
    rules = new Map();
    rulesByTenant.set(tenantId, rules);
  }
  return rules;
}

function tenantDeliveredKeys(tenantId: string): Set<string> {
  let keys = deliveredSourceEventsByTenant.get(tenantId);
  if (!keys) {
    keys = new Set();
    deliveredSourceEventsByTenant.set(tenantId, keys);
  }
  return keys;
}

function tenantDeliveries(tenantId: string): TransportNotificationDeliverySummary[] {
  let deliveries = deliveriesByTenant.get(tenantId);
  if (!deliveries) {
    deliveries = [];
    deliveriesByTenant.set(tenantId, deliveries);
  }
  return deliveries;
}

/** Mirrors the real backend's own "dispatch on read" shape (`TransportNotificationDispatchService.DispatchAndListAsync`): one rule delivers at most once per source event, even across more than one call. */
function dispatchAndList(tenantId: string): readonly TransportNotificationDeliverySummary[] {
  const enabledRules = [...tenantRules(tenantId).values()].filter((r) => r.enabled);
  const deliveredKeys = tenantDeliveredKeys(tenantId);
  const deliveries = tenantDeliveries(tenantId);

  for (const rule of enabledRules) {
    for (const item of SYNTHETIC_FEED) {
      if (!rule.eventTypes.includes(item.eventType)) continue;
      const claimKey = `${rule.id}::${item.sourceEventId}`;
      if (deliveredKeys.has(claimKey)) continue;
      deliveredKeys.add(claimKey);

      const isDeadLettered = item.sourceEventId === 'mock-evt-exception-1';
      deliveries.unshift({
        deliveryId: `mock-whd-${deliveries.length + 1}`,
        ruleId: rule.id,
        sourceEventId: item.sourceEventId,
        personId: item.personId,
        routeId: item.routeId,
        eventType: item.eventType,
        occurredAt: item.occurredAt,
        outcome: isDeadLettered ? 'dead_lettered' : 'delivered',
        attemptCount: isDeadLettered ? 3 : 1,
        detail: isDeadLettered
          ? 'channel unavailable - exceeded the retry budget (SIMULATION).'
          : 'delivered to the in-app notification feed (SIMULATION).',
        recordedAt: new Date().toISOString(),
      });
    }
  }

  return deliveries;
}

const listRulesBase = createMockAdapter('TransportService');
export const listTransportNotificationRulesMockPort: ListTransportNotificationRulesPort = {
  ...listRulesBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No tenant was provided.' };
    return { outcome: 'success', message: 'Transport notification rules loaded (SIMULATION).', rules: [...tenantRules(input.tenantId).values()] };
  },
};

const createRuleBase = createMockAdapter('TransportService');
export const createTransportNotificationRuleMockPort: CreateTransportNotificationRulePort = {
  ...createRuleBase,
  async primaryAction(input): Promise<CreateTransportNotificationRuleResult> {
    if (!input) return { outcome: 'failure', message: 'No rule definition was provided.' };
    if (!input.name.trim()) {
      return { outcome: 'failure', message: 'A name is required (SIMULATION).' };
    }
    if (input.eventTypes.length === 0) {
      return { outcome: 'failure', message: 'At least one event type is required (SIMULATION).' };
    }
    const id = `mock-trn-rule-${nextRuleNumber++}`;
    const rule: TransportNotificationRuleSummary = {
      id,
      name: input.name,
      eventTypes: input.eventTypes,
      enabled: true,
      version: 1,
      createdAt: new Date().toISOString(),
    };
    tenantRules(input.tenantId).set(id, rule);
    return { outcome: 'success', message: 'Transport notification rule created (SIMULATION).', rule };
  },
};

const setEnabledBase = createMockAdapter('TransportService');
export const setTransportNotificationRuleEnabledMockPort: SetTransportNotificationRuleEnabledPort = {
  ...setEnabledBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No rule was provided.' };
    const rules = tenantRules(input.tenantId);
    const current = rules.get(input.id);
    if (!current) return { outcome: 'failure', message: 'Unknown transport notification rule (SIMULATION).' };
    const updated: TransportNotificationRuleSummary = { ...current, enabled: input.enabled, version: current.version + 1 };
    rules.set(input.id, updated);
    return { outcome: 'success', message: `Transport notification rule ${input.enabled ? 'enabled' : 'disabled'} (SIMULATION).`, rule: updated };
  },
};

const listDeliveriesBase = createMockAdapter('TransportService');
export const listTransportNotificationDeliveriesMockPort: ListTransportNotificationDeliveriesPort = {
  ...listDeliveriesBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No tenant was provided.' };
    return { outcome: 'success', message: 'Transport notification deliveries loaded (SIMULATION).', deliveries: dispatchAndList(input.tenantId) };
  },
};
