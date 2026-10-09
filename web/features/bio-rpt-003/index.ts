import { resolvePort } from '../../lib/feature-mode';
import { auditInvestigationSearchMockPort, auditInvestigationExportMockPort } from './mock';
import { auditInvestigationSearchLivePort, auditInvestigationExportLivePort } from './live';

export type {
  AuditInvestigationPort,
  AuditActorRef,
  AuditTargetRef,
  AuditDeviceRef,
  AuditInvestigationRecord,
  AuditInvestigationFilterInput,
  AuditInvestigationSearchResult,
  AuditInvestigationExportResult,
} from './ports';

/**
 * BIO-RPT-003's resolved ports. Pages/components import these, never `./mock` or `./live`
 * directly (same discipline as every other module in this epic).
 */
export const searchAuditEventsPort = resolvePort(auditInvestigationSearchMockPort, auditInvestigationSearchLivePort, 'searchAuditEvents');
export const exportAuditEventsPort = resolvePort(auditInvestigationExportMockPort, auditInvestigationExportLivePort, 'exportAuditEvents');
