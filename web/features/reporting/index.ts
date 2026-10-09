import { resolvePort } from '../../lib/feature-mode';
import {
  reportingMockPort,
  reportingViewAuditTrailMockPort,
  reportingGenerateReportMockPort,
  reportingGetReportStatusMockPort,
  reportingCancelReportMockPort,
  reportingExportReportMockPort,
} from './mock';
import {
  reportingLivePort,
  reportingViewAuditTrailLivePort,
  reportingGenerateReportLivePort,
  reportingGetReportStatusLivePort,
  reportingCancelReportLivePort,
  reportingExportReportLivePort,
} from './live';
import type { ReportingPort } from './ports';

export type {
  ReportingPort,
  ReportJobSnapshot,
  ReportJobStatus,
  GenerateReportInput,
  ReportJobInput,
  ReportJobResult,
  ExportReportResult,
} from './ports';
export { REPORT_DATASETS, REPORT_DIMENSIONS_BY_DATASET } from './ports';

/**
 * The Reporting module's resolved ports (BIO-UX-005-AC01, AC02, AC04; BIO-RPT-002-AC01/AC06).
 * Pages/components import these, never `./mock` or `./live` directly — switching
 * `NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE` (module-wide default) or a per-port
 * `NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_<PORTKEY>` override
 * (`web/lib/feature-mode.ts`'s own `PER_PORT_ENV_DEFAULTS`) from SIMULATION to LIVE changes what
 * these resolve to without any importer changing.
 *
 * `primaryAction`/`viewAuditTrail` stay BIO-RPT-001's own domain (see `./ports.ts`'s header); the
 * four `report*` ports are BIO-RPT-002's own dedicated create→poll→(cancel|export) connection.
 */
export const reportingPort: ReportingPort = resolvePort(reportingMockPort, reportingLivePort, 'primaryAction');

export const reportingViewAuditTrailPort: ReportingPort = resolvePort(
  reportingViewAuditTrailMockPort,
  reportingViewAuditTrailLivePort,
  'viewAuditTrail',
);

export const reportingGenerateReportPort = resolvePort(reportingGenerateReportMockPort, reportingGenerateReportLivePort, 'generateReport');

export const reportingGetReportStatusPort = resolvePort(
  reportingGetReportStatusMockPort,
  reportingGetReportStatusLivePort,
  'getReportStatus',
);

export const reportingCancelReportPort = resolvePort(reportingCancelReportMockPort, reportingCancelReportLivePort, 'cancelReport');

export const reportingExportReportPort = resolvePort(reportingExportReportMockPort, reportingExportReportLivePort, 'exportReport');
