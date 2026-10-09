import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * BIO-RPT-003's own dedicated connection: searchable audit investigation views (AC01), read-only
 * (AC02), permissioned/audited export (AC03), view-time redaction of sensitive detail (AC04).
 * `primaryAction`/`viewAuditTrail` stay BIO-RPT-001's own domain (see
 * `web/features/reporting/ports.ts`'s own header) — these two ports are new, dedicated keys,
 * following the same multi-port precedent BIO-RPT-002 established for this exact module.
 */
export type AuditInvestigationPort = ModulePort;

export interface AuditActorRef {
  type: string;
  id: string;
}

export interface AuditTargetRef {
  type: string;
  id?: string;
}

export interface AuditDeviceRef {
  deviceId?: string;
  workstation?: string;
}

/** Mirrors the backend's `NormalizedAuditRecord` (already redacted per-viewer — see `AuditViewRedaction`). */
export interface AuditInvestigationRecord {
  sourceFeature: string;
  eventId: string;
  tenantId: string;
  actor: AuditActorRef;
  device?: AuditDeviceRef;
  personId?: string;
  action: string;
  target: AuditTargetRef;
  outcome: string;
  reasonCode: string;
  occurredAt: string;
  classification: string;
  withheld: readonly string[];
  detail: Readonly<Record<string, unknown>>;
}

/** BIO-RPT-003-AC01: actor/person/action/time/device/reason, every field optional. */
export interface AuditInvestigationFilterInput {
  actorType?: string;
  actorId?: string;
  personId?: string;
  action?: string;
  deviceId?: string;
  reasonCode?: string;
  occurredFrom?: string;
  occurredTo?: string;
  page?: number;
  pageSize?: number;
}

export interface AuditInvestigationSearchResult extends PrimaryActionResult {
  records?: readonly AuditInvestigationRecord[];
  totalCount?: number;
}

export interface AuditInvestigationExportResult extends PrimaryActionResult {
  /** Raw CSV text (small demo-scale exports only) — the caller triggers the actual file save. */
  csv?: string;
}

export interface SearchAuditEventsPort extends ModulePort {
  primaryAction(input?: AuditInvestigationFilterInput): Promise<AuditInvestigationSearchResult>;
}

export interface ExportAuditEventsPort extends ModulePort {
  primaryAction(input?: AuditInvestigationFilterInput): Promise<AuditInvestigationExportResult>;
}
