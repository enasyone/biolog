import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * Reporting module ports (BIO-UX-005-AC01, AC02; BIO-RPT-002-AC01/AC06).
 *
 * `primaryAction`/`viewAuditTrail` are BIO-UX-005's own original two ports, still backing
 * BIO-RPT-001's real notification-demo endpoints (`/api/v1/reporting/primary-action`,
 * `.../view-audit-trail`) — `/reports`' "Run report" button and `/`'s "Refresh dashboard" button
 * (BIO-UX-011) — and `/audit`'s "View audit trail" button respectively. BIO-RPT-002 never touches
 * either: see docs/features/bio-rpt-002/README.md "AC06: connecting the existing UI" for why
 * (god's ruling, 2026-09-24 — a genuine two-story route collision at those exact paths, resolved
 * by giving this story its own dedicated routes/ports instead of one story taking the other's).
 *
 * The four ports below are BIO-RPT-002's own dedicated connection: a real governed report's
 * create → poll → (cancel|export) lifecycle, each independently mode-switchable like every other
 * multi-port module in this epic (see `CredentialService`'s own nine-port precedent,
 * `web/features/cards/ports.ts`).
 */
export type ReportingPort = ModulePort;

export type ReportJobStatus = 'queued' | 'running' | 'completed' | 'failed' | 'cancelled';

export interface ReportJobSnapshot {
  jobId: string;
  status: ReportJobStatus;
  dataset: string;
  dimension: string;
  measure: string;
  columns?: readonly string[];
  rows?: ReadonlyArray<readonly string[]>;
  errorCode?: string;
}

export interface GenerateReportInput {
  dataset: string;
  dimension: string;
  measure: string;
}

export interface ReportJobInput {
  jobId: string;
}

export interface ReportJobResult extends PrimaryActionResult {
  job?: ReportJobSnapshot;
}

export interface ExportReportResult extends PrimaryActionResult {
  /** Raw CSV text (small demo-scale reports only) — the caller triggers the actual file save. */
  csv?: string;
}

/** BIO-RPT-002-AC01/AC04: submits a governed report definition and returns its freshly created job. */
export interface GenerateReportPort extends ModulePort {
  primaryAction(input?: GenerateReportInput): Promise<ReportJobResult>;
}

/** BIO-RPT-002-AC04: polls one job's current status/result by id. */
export interface GetReportStatusPort extends ModulePort {
  primaryAction(input?: ReportJobInput): Promise<ReportJobResult>;
}

/** BIO-RPT-002-AC04: cancels a Queued/Running job. */
export interface CancelReportPort extends ModulePort {
  primaryAction(input?: ReportJobInput): Promise<ReportJobResult>;
}

/** BIO-RPT-002-AC02: exports a Completed job's result as CSV. */
export interface ExportReportPort extends ModulePort {
  primaryAction(input?: ReportJobInput): Promise<ExportReportResult>;
}

/** BIO-RPT-002-AC01: the whitelist this story's own UI offers — mirrors `ReportSchemaRegistry` server-side; the LIVE `/api/v1/reporting/schema` endpoint remains the authoritative source, this is only the SIMULATION-mode/initial-render fallback. */
export const REPORT_DATASETS: readonly string[] = ['card_production', 'attendance'];

export const REPORT_DIMENSIONS_BY_DATASET: Readonly<Record<string, readonly string[]>> = {
  card_production: ['status', 'source', 'card_class'],
  attendance: ['mode', 'source', 'verification_method'],
};
