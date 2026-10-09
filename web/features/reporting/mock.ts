import { createMockAdapter } from '../../lib/module-port';
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
import { REPORT_DIMENSIONS_BY_DATASET } from './ports';

/**
 * Reporting mock adapters (BIO-UX-005-AC05). `reportingMockPort`/`reportingViewAuditTrailMockPort`
 * are BIO-UX-005's own original two, untouched — they back BIO-RPT-001's own domain (see
 * `ports.ts`'s own header). The four below are BIO-RPT-002's own dedicated create→poll→
 * (cancel|export) lifecycle mocks, following the same deterministic-outcome, real-precondition
 * discipline `web/features/cards/mock.ts` established (BIO-UX-007): `failure` always reflects a
 * genuine, checkable precondition against this module's own in-memory job store, never an
 * unrelated scripted outcome; `offline` fires on a deterministic per-action schedule before any
 * mutation.
 */
export const reportingMockPort: ReportingPort = createMockAdapter('ReportingService', {
  scenarios: [
    { outcome: 'success', message: 'Reporting: simulated action completed.' },
    { outcome: 'failure', message: 'Reporting: simulated action failed — try again.' },
    { outcome: 'offline', message: 'Reporting: device or network appears offline (simulated).' },
  ],
});

export const reportingViewAuditTrailMockPort: ReportingPort = createMockAdapter('ReportingService', {
  scenarios: [
    { outcome: 'success', message: 'Audit: trail loaded.' },
    { outcome: 'failure', message: 'Audit: trail failed to load — try again.' },
    { outcome: 'offline', message: 'Audit: device or network appears offline (simulated).' },
  ],
});

/** Every 5th call to a given action simulates a dropped connection — see this file's own header note. */
function makeOfflineGate(): () => boolean {
  let n = 0;
  return () => {
    n += 1;
    return n % 5 === 0;
  };
}

const MISSING_INPUT: ReportJobResult = { outcome: 'failure', message: 'No input was provided to this action.' };

/**
 * Deterministic synthetic group counts for one (dataset, dimension) pair — a small, fixed,
 * reproducible table rather than random data (BIO-UX-004's own "never `Math.random()`"
 * discipline), distinct enough per pair that a human demo can tell datasets/dimensions apart.
 */
const SYNTHETIC_GROUPS: Readonly<Record<string, ReadonlyArray<readonly [string, string]>>> = {
  'card_production:status': [
    ['passed', '11'],
    ['failed', '2'],
    ['in_progress', '1'],
  ],
  'card_production:source': [
    ['connected', '12'],
    ['offline', '2'],
  ],
  'card_production:card_class': [
    ['staff', '9'],
    ['visitor', '5'],
  ],
  'attendance:mode': [
    ['check_in', '18'],
    ['check_out', '15'],
    ['presence', '4'],
  ],
  'attendance:source': [
    ['device', '30'],
    ['manual', '5'],
    ['import', '2'],
  ],
  'attendance:verification_method': [
    ['biometric', '25'],
    ['credential', '8'],
    ['unverified', '4'],
  ],
};

const jobStore = new Map<string, ReportJobSnapshot>();
let jobCounter = 0;

function buildCsv(job: ReportJobSnapshot): string {
  const header = (job.columns ?? []).join(',');
  const rows = (job.rows ?? []).map((row) => row.join(','));
  return [header, ...rows].join('\r\n') + '\r\n';
}

export const reportingGenerateReportMockPort: GenerateReportPort = (() => {
  const isOffline = makeOfflineGate();
  return {
    module: 'ReportingService',
    mode: 'mock',
    async primaryAction(input?: GenerateReportInput): Promise<ReportJobResult> {
      if (!input) return MISSING_INPUT;
      if (isOffline()) {
        return { outcome: 'offline', message: 'Reporting: device or network appears offline (simulated).' };
      }
      const allowedDimensions = REPORT_DIMENSIONS_BY_DATASET[input.dataset];
      if (!allowedDimensions || !allowedDimensions.includes(input.dimension)) {
        return { outcome: 'failure', message: `Report generation failed: dimension not whitelisted for this dataset (SIMULATION).` };
      }

      jobCounter += 1;
      const jobId = `rpj-sim-${String(jobCounter).padStart(6, '0')}`;
      const groups = SYNTHETIC_GROUPS[`${input.dataset}:${input.dimension}`] ?? [];
      const job: ReportJobSnapshot = {
        jobId,
        status: 'completed',
        dataset: input.dataset,
        dimension: input.dimension,
        measure: input.measure,
        columns: [input.dimension, input.measure],
        rows: groups.map(([value, count]) => [value, count] as const),
      };
      jobStore.set(jobId, job);

      return { outcome: 'success', message: `Report ${jobId} completed (SIMULATION): ${groups.length} group(s).`, job };
    },
  };
})();

export const reportingGetReportStatusMockPort: GetReportStatusPort = {
  module: 'ReportingService',
  mode: 'mock',
  async primaryAction(input?: ReportJobInput): Promise<ReportJobResult> {
    if (!input) return MISSING_INPUT;
    const job = jobStore.get(input.jobId);
    if (!job) {
      return { outcome: 'failure', message: `No report job '${input.jobId}' found (SIMULATION).` };
    }
    return { outcome: 'success', message: `Report ${job.jobId} is ${job.status} (SIMULATION).`, job };
  },
};

export const reportingCancelReportMockPort: CancelReportPort = {
  module: 'ReportingService',
  mode: 'mock',
  async primaryAction(input?: ReportJobInput): Promise<ReportJobResult> {
    if (!input) return MISSING_INPUT;
    const job = jobStore.get(input.jobId);
    if (!job) {
      return { outcome: 'failure', message: `No report job '${input.jobId}' found (SIMULATION).` };
    }
    // Every mock job resolves to Completed synchronously (no real async window to cancel into) -
    // refusing an already-terminal job mirrors the real backend's own `report_job_not_cancellable`
    // behaviour honestly, rather than pretending a cancel succeeded on nothing.
    return { outcome: 'failure', message: `Report ${job.jobId} is already ${job.status} and cannot be cancelled (SIMULATION).`, job };
  },
};

export const reportingExportReportMockPort: ExportReportPort = {
  module: 'ReportingService',
  mode: 'mock',
  async primaryAction(input?: ReportJobInput): Promise<ExportReportResult> {
    if (!input) return MISSING_INPUT;
    const job = jobStore.get(input.jobId);
    if (!job || job.status !== 'completed') {
      return { outcome: 'failure', message: `Report ${input.jobId} is not completed yet - nothing to export (SIMULATION).` };
    }
    return { outcome: 'success', message: `Report ${job.jobId} exported as CSV (SIMULATION).`, csv: buildCsv(job) };
  },
};
