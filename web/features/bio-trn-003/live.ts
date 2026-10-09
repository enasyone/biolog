import type {
  CreateTransportNotificationRulePort,
  ListTransportNotificationDeliveriesPort,
  ListTransportNotificationRulesPort,
  SetTransportNotificationRuleEnabledPort,
  TransportNotificationDeliverySummary,
  TransportNotificationEventTypeId,
  TransportNotificationRuleSummary,
} from './ports';

/**
 * Live adapters for BIO-TRN-003's real `/api/v1/transport-notifications/*` backend
 * (`BioTrn003Module.cs`) - genuinely connected, not a stub. No tenant header is sent; this
 * host resolves tenancy from the request's own Host channel (ADR-0002 Decision 1.3,
 * `TenantContextMiddleware`), the same convention every other live adapter in this epic already
 * follows (see `web/features/bio-int-004/live.ts`). The real backend also requires a resolved,
 * provenance-verified actor who holds this story's own RBAC permission - this adapter's own
 * static, unauthenticated `ACTOR_HEADERS` is the same disclosed boundary BIO-INT-004's own live
 * adapter already established (see `docs/features/bio-trn-003/README.md` "AC05 — web wiring" for
 * why: the default browser Playwright suite proves the mapped workflow against SIMULATION mode
 * only, the same bar every sibling story's own AC05 section already sets, while this story's real
 * HTTP endpoints - RBAC/provenance enforcement included - are proven directly against the real
 * backend by `tests/dotnet/Biolog.IntegrationTests/BioTrn003EndpointsTests.cs`).
 */
const ACTOR_HEADERS = { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' } as const;
const BASE_PATH = '/api/v1/transport-notifications';

interface TransportNotificationRuleWire {
  id: string;
  name: string;
  eventTypes: readonly TransportNotificationEventTypeId[];
  enabled: boolean;
  version: number;
  createdAt: string;
}

interface TransportNotificationDeliveryWire {
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

function ruleFromWire(wire: TransportNotificationRuleWire): TransportNotificationRuleSummary {
  return {
    id: wire.id,
    name: wire.name,
    eventTypes: wire.eventTypes,
    enabled: wire.enabled,
    version: wire.version,
    createdAt: wire.createdAt,
  };
}

function deliveryFromWire(wire: TransportNotificationDeliveryWire): TransportNotificationDeliverySummary {
  return {
    deliveryId: wire.deliveryId,
    ruleId: wire.ruleId,
    sourceEventId: wire.sourceEventId,
    personId: wire.personId,
    routeId: wire.routeId,
    eventType: wire.eventType,
    occurredAt: wire.occurredAt,
    outcome: wire.outcome,
    attemptCount: wire.attemptCount,
    detail: wire.detail,
    recordedAt: wire.recordedAt,
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

export const listTransportNotificationRulesLivePort: ListTransportNotificationRulesPort = {
  module: 'TransportService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No tenant was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/rules`, { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not load transport notification rules: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as { data: TransportNotificationRuleWire[] };
      return { outcome: 'success', message: 'Transport notification rules loaded.', rules: body.data.map(ruleFromWire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Transport Notifications service - device or network appears offline.' };
    }
  },
};

export const createTransportNotificationRuleLivePort: CreateTransportNotificationRulePort = {
  module: 'TransportService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No rule definition was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/rules`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ name: input.name, eventTypes: input.eventTypes }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not create transport notification rule: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as TransportNotificationRuleWire;
      return { outcome: 'success', message: 'Transport notification rule created.', rule: ruleFromWire(wire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Transport Notifications service - device or network appears offline.' };
    }
  },
};

export const setTransportNotificationRuleEnabledLivePort: SetTransportNotificationRuleEnabledPort = {
  module: 'TransportService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No rule was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/rules/${input.id}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ enabled: input.enabled, version: input.expectedVersion }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not update transport notification rule: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as TransportNotificationRuleWire;
      return { outcome: 'success', message: `Transport notification rule ${wire.enabled ? 'enabled' : 'disabled'}.`, rule: ruleFromWire(wire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Transport Notifications service - device or network appears offline.' };
    }
  },
};

export const listTransportNotificationDeliveriesLivePort: ListTransportNotificationDeliveriesPort = {
  module: 'TransportService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No tenant was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/deliveries`, { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not load transport notification deliveries: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as { data: TransportNotificationDeliveryWire[] };
      return { outcome: 'success', message: 'Transport notification deliveries loaded.', deliveries: body.data.map(deliveryFromWire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Transport Notifications service - device or network appears offline.' };
    }
  },
};
