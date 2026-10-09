import type {
  AuditInvestigationExportResult,
  AuditInvestigationFilterInput,
  AuditInvestigationRecord,
  AuditInvestigationSearchResult,
  ExportAuditEventsPort,
  SearchAuditEventsPort,
} from './ports';

/**
 * SIMULATION-mode fixture spanning three sources (rbac/card_production/card_templates), one
 * carrying a real Device value — no LIVE source populates one today (every existing backend
 * `*AuditEventBuilder` sets `Device = null`; see docs/features/bio-rpt-003/README.md), so this
 * mock is deliberately the one place the device filter can be demonstrated end-to-end through the
 * real UI. Simplification, disclosed: SIMULATION has no RBAC concept, so sensitive detail is
 * always shown here (the real permission-gated redaction is exercised by the LIVE proof and the
 * backend's own tests instead).
 */
const SYNTHETIC_RECORDS: readonly AuditInvestigationRecord[] = [
  {
    sourceFeature: 'rbac',
    eventId: 'evt_00000000-0000-7000-8000-000000000101',
    tenantId: 'demo-tenant',
    actor: { type: 'user', id: 'admin-1' },
    personId: 'per_00000000-0000-7000-8000-000000000201',
    action: 'rbac.role_assign',
    target: { type: 'person_role_assignment', id: 'per_00000000-0000-7000-8000-000000000201/AUDITOR' },
    outcome: 'ALLOWED',
    reasonCode: 'role_assigned',
    occurredAt: '2026-09-20T09:00:00.000Z',
    classification: 'internal',
    withheld: [],
    detail: { role: 'AUDITOR' },
  },
  {
    sourceFeature: 'card_production',
    eventId: 'evt_00000000-0000-7000-8000-000000000102',
    tenantId: 'demo-tenant',
    actor: { type: 'user', id: 'operator-2' },
    device: { deviceId: 'dev_printer_1', workstation: 'station-a' },
    personId: 'per_00000000-0000-7000-8000-000000000202',
    action: 'production.create_job',
    target: { type: 'production_job', id: 'per_00000000-0000-7000-8000-000000000202:staff:1:1' },
    outcome: 'ALLOWED',
    reasonCode: 'production_job_created',
    occurredAt: '2026-09-21T11:30:00.000Z',
    classification: 'internal',
    withheld: [],
    detail: { card_class: 'staff' },
  },
  {
    sourceFeature: 'card_templates',
    eventId: 'evt_00000000-0000-7000-8000-000000000103',
    tenantId: 'demo-tenant',
    actor: { type: 'user', id: 'designer-3' },
    action: 'card_template.publish',
    target: { type: 'card_template', id: 'tpl_00000000-0000-7000-8000-000000000301' },
    outcome: 'ALLOWED',
    reasonCode: 'card_template_published',
    occurredAt: '2026-09-22T14:00:00.000Z',
    classification: 'public',
    withheld: [],
    detail: {},
  },
];

function matches(record: AuditInvestigationRecord, filter?: AuditInvestigationFilterInput): boolean {
  if (!filter) return true;
  if (filter.actorType && record.actor.type !== filter.actorType) return false;
  if (filter.actorId && record.actor.id !== filter.actorId) return false;
  if (filter.personId && record.personId !== filter.personId) return false;
  if (filter.action && record.action !== filter.action) return false;
  if (filter.deviceId && record.device?.deviceId !== filter.deviceId) return false;
  if (filter.reasonCode && record.reasonCode !== filter.reasonCode) return false;
  if (filter.occurredFrom && record.occurredAt < filter.occurredFrom) return false;
  if (filter.occurredTo && record.occurredAt > filter.occurredTo) return false;
  return true;
}

function search(filter?: AuditInvestigationFilterInput): AuditInvestigationRecord[] {
  return SYNTHETIC_RECORDS.filter((record) => matches(record, filter)).sort((a, b) => (a.occurredAt < b.occurredAt ? 1 : -1));
}

export const auditInvestigationSearchMockPort: SearchAuditEventsPort = {
  module: 'ReportingService',
  mode: 'mock',
  async primaryAction(input?: AuditInvestigationFilterInput): Promise<AuditInvestigationSearchResult> {
    const records = search(input);
    return {
      outcome: 'success',
      message: `${records.length} matching audit event(s) found (SIMULATION).`,
      records,
      totalCount: records.length,
    };
  },
};

function toCsv(records: readonly AuditInvestigationRecord[]): string {
  const header = ['event_id', 'source_feature', 'actor_type', 'actor_id', 'person_id', 'action', 'reason_code', 'occurred_at', 'classification'];
  const rows = records.map((record) => [
    record.eventId,
    record.sourceFeature,
    record.actor.type,
    record.actor.id,
    record.personId ?? '',
    record.action,
    record.reasonCode,
    record.occurredAt,
    record.classification,
  ]);
  return [header, ...rows].map((row) => row.join(',')).join('\r\n');
}

export const auditInvestigationExportMockPort: ExportAuditEventsPort = {
  module: 'ReportingService',
  mode: 'mock',
  async primaryAction(input?: AuditInvestigationFilterInput): Promise<AuditInvestigationExportResult> {
    const records = search(input);
    return {
      outcome: 'success',
      message: `Exported ${records.length} audit event(s) as CSV (SIMULATION).`,
      csv: toCsv(records),
    };
  },
};
