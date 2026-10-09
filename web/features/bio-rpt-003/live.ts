import type {
  AuditInvestigationExportResult,
  AuditInvestigationFilterInput,
  AuditInvestigationRecord,
  AuditInvestigationSearchResult,
  ExportAuditEventsPort,
  SearchAuditEventsPort,
} from './ports';

/**
 * Hand-written `ModulePort`s, not `createLiveAdapter` (a fixed zero-arg `POST <path>` that ignores
 * `input`) — the same reason every other genuinely-parameterized live port in this epic is
 * hand-written. Every request body/query param below is built as an EXPLICIT snake_case object
 * matching the backend's own wire contract, never a re-serialized camelCase object directly - a
 * hand-written live port's request body silently drifting from the backend's snake_case DTO
 * contract is a real, previously-hit bug class in this codebase (BIO-CRD-008's own
 * `transitionAction`, caught only by its dual-server LIVE proof) - applied proactively here from
 * the start rather than re-discovered.
 */
const ACTOR_HEADERS = { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' } as const;
const BASE_PATH = '/api/v1/audit-investigations';

async function problemMessage(response: Response, fallback: string): Promise<string> {
  try {
    const problem = (await response.json()) as { message?: string };
    return problem.message ?? fallback;
  } catch {
    return fallback;
  }
}

function buildQuery(input?: AuditInvestigationFilterInput): string {
  if (!input) return '';
  const params = new URLSearchParams();
  if (input.actorType) params.set('actor_type', input.actorType);
  if (input.actorId) params.set('actor_id', input.actorId);
  if (input.personId) params.set('person_id', input.personId);
  if (input.action) params.set('action', input.action);
  if (input.deviceId) params.set('device_id', input.deviceId);
  if (input.reasonCode) params.set('reason_code', input.reasonCode);
  if (input.occurredFrom) params.set('occurred_from', input.occurredFrom);
  if (input.occurredTo) params.set('occurred_to', input.occurredTo);
  if (input.page) params.set('page', String(input.page));
  if (input.pageSize) params.set('page_size', String(input.pageSize));
  const query = params.toString();
  return query ? `?${query}` : '';
}

function exportBody(input?: AuditInvestigationFilterInput): Record<string, string | undefined> {
  return {
    actor_type: input?.actorType,
    actor_id: input?.actorId,
    person_id: input?.personId,
    action: input?.action,
    device_id: input?.deviceId,
    reason_code: input?.reasonCode,
    occurred_from: input?.occurredFrom,
    occurred_to: input?.occurredTo,
  };
}

function mapRecord(raw: Record<string, unknown>): AuditInvestigationRecord {
  const actor = raw.actor as { type: string; id: string };
  const target = raw.target as { type: string; id?: string };
  const device = raw.device as { device_id?: string; workstation?: string } | null | undefined;
  return {
    sourceFeature: String(raw.source_feature),
    eventId: String(raw.event_id),
    tenantId: String(raw.tenant_id),
    actor: { type: actor.type, id: actor.id },
    device: device ? { deviceId: device.device_id, workstation: device.workstation } : undefined,
    personId: raw.person_id as string | undefined,
    action: String(raw.action),
    target: { type: target.type, id: target.id },
    outcome: String(raw.outcome),
    reasonCode: String(raw.reason_code),
    occurredAt: String(raw.occurred_at),
    classification: String(raw.classification),
    withheld: (raw.withheld as string[] | undefined) ?? [],
    detail: (raw.detail as Record<string, unknown> | undefined) ?? {},
  };
}

export const auditInvestigationSearchLivePort: SearchAuditEventsPort = {
  module: 'ReportingService',
  mode: 'live',
  async primaryAction(input?: AuditInvestigationFilterInput): Promise<AuditInvestigationSearchResult> {
    try {
      const response = await fetch(`${BASE_PATH}${buildQuery(input)}`, { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Audit search failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as { records: Record<string, unknown>[]; total_count: number };
      const records = body.records.map(mapRecord);
      return { outcome: 'success', message: `${body.total_count} matching audit event(s) found (LIVE).`, records, totalCount: body.total_count };
    } catch {
      return { outcome: 'offline', message: `No live audit investigation service reachable at ${BASE_PATH} yet.` };
    }
  },
};

export const auditInvestigationExportLivePort: ExportAuditEventsPort = {
  module: 'ReportingService',
  mode: 'live',
  async primaryAction(input?: AuditInvestigationFilterInput): Promise<AuditInvestigationExportResult> {
    try {
      const response = await fetch(`${BASE_PATH}/export`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify(exportBody(input)),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Export failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const csv = await response.text();
      return { outcome: 'success', message: 'Audit events exported as CSV (LIVE).', csv };
    } catch {
      return { outcome: 'offline', message: `No live audit investigation service reachable at ${BASE_PATH}/export yet.` };
    }
  },
};
