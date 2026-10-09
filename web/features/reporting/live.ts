import { createLiveAdapter } from '../../lib/module-port';
import type {
  CancelReportPort,
  ExportReportPort,
  ExportReportResult,
  GenerateReportInput,
  GenerateReportPort,
  GetReportStatusPort,
  ReportingPort,
  ReportJobInput,
  ReportJobResult,
  ReportJobSnapshot,
} from './ports';

/**
 * Reporting live adapters. `reportingLivePort`/`reportingViewAuditTrailLivePort` are BIO-UX-005's
 * own original two, untouched — real, live and served by BIO-RPT-001's own notification-demo
 * endpoints (see `ports.ts`'s own header for why BIO-RPT-002 never claims those two paths).
 *
 * The four below are BIO-RPT-002's own dedicated connection to its real governed report engine
 * (`api/features/bio-rpt-002`). Unlike `createLiveAdapter` (a fixed `POST <path>` with no body,
 * ignoring any `input`), each of these genuinely sends `input` as a JSON body or interpolates it
 * into the URL — a real backend now exists to receive it, unlike when `createLiveAdapter` was
 * written (BIO-UX-005), so a bespoke implementation per action is the honest shape here, not
 * reused stub plumbing.
 */
export const reportingLivePort: ReportingPort = createLiveAdapter(
  'ReportingService',
  '/api/v1/reporting/primary-action',
);

export const reportingViewAuditTrailLivePort: ReportingPort = createLiveAdapter(
  'ReportingService',
  '/api/v1/reporting/view-audit-trail',
);

const ACTOR_HEADERS = { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' } as const;
const BASE_PATH = '/api/v1/reporting';

function mapJob(body: Record<string, unknown>): ReportJobSnapshot {
  const result = body.result as { columns?: string[]; rows?: string[][] } | undefined;
  return {
    jobId: String(body.job_id),
    status: body.status as ReportJobSnapshot['status'],
    dataset: String(body.dataset),
    dimension: String(body.dimension),
    measure: String(body.measure),
    columns: result?.columns,
    rows: result?.rows,
    errorCode: body.error_code as string | undefined,
  };
}

async function problemMessage(response: Response, fallback: string): Promise<string> {
  try {
    const problem = (await response.json()) as { message?: string };
    return problem.message ?? fallback;
  } catch {
    return fallback;
  }
}

export const reportingGenerateReportLivePort: GenerateReportPort = {
  module: 'ReportingService',
  mode: 'live',
  async primaryAction(input?: GenerateReportInput): Promise<ReportJobResult> {
    if (!input) return { outcome: 'failure', message: 'No report definition was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/reports`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify(input),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Report generation failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as Record<string, unknown>;
      const job = mapJob(body);
      return { outcome: 'success', message: `Report ${job.jobId} ${job.status} (LIVE).`, job };
    } catch {
      return { outcome: 'offline', message: `No live reporting service reachable at ${BASE_PATH}/reports yet.` };
    }
  },
};

export const reportingGetReportStatusLivePort: GetReportStatusPort = {
  module: 'ReportingService',
  mode: 'live',
  async primaryAction(input?: ReportJobInput): Promise<ReportJobResult> {
    if (!input) return { outcome: 'failure', message: 'No report job id was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/reports/${encodeURIComponent(input.jobId)}`, { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Report status check failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as Record<string, unknown>;
      const job = mapJob(body);
      return { outcome: 'success', message: `Report ${job.jobId} is ${job.status} (LIVE).`, job };
    } catch {
      return { outcome: 'offline', message: `No live reporting service reachable at ${BASE_PATH}/reports/${input.jobId} yet.` };
    }
  },
};

export const reportingCancelReportLivePort: CancelReportPort = {
  module: 'ReportingService',
  mode: 'live',
  async primaryAction(input?: ReportJobInput): Promise<ReportJobResult> {
    if (!input) return { outcome: 'failure', message: 'No report job id was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/reports/${encodeURIComponent(input.jobId)}/cancel`, { method: 'POST', headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Cancel failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as Record<string, unknown>;
      const job = mapJob(body);
      return { outcome: 'success', message: `Report ${job.jobId} cancel requested (LIVE).`, job };
    } catch {
      return { outcome: 'offline', message: `No live reporting service reachable at ${BASE_PATH}/reports/${input.jobId}/cancel yet.` };
    }
  },
};

export const reportingExportReportLivePort: ExportReportPort = {
  module: 'ReportingService',
  mode: 'live',
  async primaryAction(input?: ReportJobInput): Promise<ExportReportResult> {
    if (!input) return { outcome: 'failure', message: 'No report job id was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/reports/${encodeURIComponent(input.jobId)}/export?format=csv`, { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Export failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const csv = await response.text();
      return { outcome: 'success', message: `Report ${input.jobId} exported as CSV (LIVE).`, csv };
    } catch {
      return { outcome: 'offline', message: `No live reporting service reachable at ${BASE_PATH}/reports/${input.jobId}/export yet.` };
    }
  },
};
