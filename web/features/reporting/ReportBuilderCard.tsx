'use client';

import { useEffect, useRef, useState } from 'react';
import { StatusBadge, type StatusBadgeTone } from '../../components/status-badge';
import {
  reportingGenerateReportPort,
  reportingGetReportStatusPort,
  reportingCancelReportPort,
  reportingExportReportPort,
  REPORT_DATASETS,
  REPORT_DIMENSIONS_BY_DATASET,
  type ReportJobSnapshot,
} from '.';

const DATASET_LABELS: Readonly<Record<string, string>> = {
  card_production: 'Card production',
  attendance: 'Attendance',
};

const DIMENSION_LABELS: Readonly<Record<string, string>> = {
  status: 'Status',
  source: 'Source',
  card_class: 'Card class',
  mode: 'Mode',
  verification_method: 'Verification method',
};

const STATUS_TONE: Readonly<Record<ReportJobSnapshot['status'], StatusBadgeTone>> = {
  queued: 'info',
  running: 'info',
  completed: 'success',
  failed: 'error',
  cancelled: 'neutral',
};

type BuilderState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'job'; job: ReportJobSnapshot; message: string }
  | { status: 'error'; message: string };

/**
 * BIO-RPT-002-AC01/AC04/AC06: a real, working governed-report form — pick a whitelisted
 * dataset/dimension, generate a report, poll it to completion, cancel it while pending, export
 * the result as CSV. Mounted alongside BIO-UX-011's own existing "Run a report" card on
 * `/reports` (untouched) — this is BIO-RPT-002's own dedicated connection, distinct from that
 * card's generic `primaryAction` button, which stays BIO-RPT-001's own domain (see
 * `./ports.ts`'s header).
 */
export function ReportBuilderCard() {
  const [dataset, setDataset] = useState<string>(REPORT_DATASETS[0]!);
  const [dimension, setDimension] = useState<string>(REPORT_DIMENSIONS_BY_DATASET[REPORT_DATASETS[0]!]![0]!);
  const [state, setState] = useState<BuilderState>({ status: 'idle' });
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => () => {
    if (pollRef.current) clearInterval(pollRef.current);
  }, []);

  function stopPolling() {
    if (pollRef.current) {
      clearInterval(pollRef.current);
      pollRef.current = null;
    }
  }

  function startPolling(jobId: string) {
    stopPolling();
    pollRef.current = setInterval(async () => {
      const result = await reportingGetReportStatusPort.primaryAction({ jobId });
      if (result.outcome !== 'success' || !result.job) return;
      setState({ status: 'job', job: result.job, message: result.message });
      if (result.job.status === 'completed' || result.job.status === 'failed' || result.job.status === 'cancelled') {
        stopPolling();
      }
    }, 1000);
  }

  function onDatasetChange(next: string) {
    setDataset(next);
    setDimension(REPORT_DIMENSIONS_BY_DATASET[next]![0]!);
  }

  async function onGenerate() {
    setState({ status: 'loading' });
    const result = await reportingGenerateReportPort.primaryAction({ dataset, dimension, measure: 'count' });
    if (result.outcome !== 'success' || !result.job) {
      setState({ status: 'error', message: result.message });
      return;
    }
    setState({ status: 'job', job: result.job, message: result.message });
    if (result.job.status === 'queued' || result.job.status === 'running') {
      startPolling(result.job.jobId);
    }
  }

  async function onCancel(jobId: string) {
    const result = await reportingCancelReportPort.primaryAction({ jobId });
    if (result.job) {
      setState({ status: 'job', job: result.job, message: result.message });
    }
    if (!result.job || result.job.status === 'cancelled') {
      stopPolling();
    }
  }

  async function onExport(jobId: string) {
    const result = await reportingExportReportPort.primaryAction({ jobId });
    if (result.outcome !== 'success' || !result.csv) {
      setState({ status: 'error', message: result.message });
      return;
    }
    const blob = new Blob([result.csv], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${jobId}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

  const job = state.status === 'job' ? state.job : undefined;
  const isPending = job?.status === 'queued' || job?.status === 'running';

  return (
    <section className="biolog-card" data-testid="report-builder-card">
      <h2>Build a governed report</h2>
      <p>
        Every dimension/measure below is whitelisted server-side (BIO-RPT-002-AC01) — real data from card
        production and attendance, not arbitrary tenant SQL.{' '}
        <StatusBadge tone={reportingGenerateReportPort.mode === 'live' ? 'success' : 'neutral'}>
          {reportingGenerateReportPort.mode === 'live' ? 'LIVE' : 'SIMULATION'}
        </StatusBadge>
      </p>

      <div className="biolog-field-row">
        <label htmlFor="report-builder-dataset">Dataset</label>
        <select id="report-builder-dataset" value={dataset} onChange={(e) => onDatasetChange(e.target.value)} disabled={isPending}>
          {REPORT_DATASETS.map((d) => (
            <option key={d} value={d}>
              {DATASET_LABELS[d] ?? d}
            </option>
          ))}
        </select>
      </div>

      <div className="biolog-field-row">
        <label htmlFor="report-builder-dimension">Dimension</label>
        <select id="report-builder-dimension" value={dimension} onChange={(e) => setDimension(e.target.value)} disabled={isPending}>
          {(REPORT_DIMENSIONS_BY_DATASET[dataset] ?? []).map((d) => (
            <option key={d} value={d}>
              {DIMENSION_LABELS[d] ?? d}
            </option>
          ))}
        </select>
      </div>

      <button
        type="button"
        className="biolog-button-primary"
        onClick={onGenerate}
        disabled={state.status === 'loading' || isPending}
      >
        {state.status === 'loading' ? 'Generating…' : 'Generate report'}
      </button>

      {job && (
        <div role="status" aria-label="Report job status">
          <p>
            Job <code>{job.jobId}</code> — <StatusBadge tone={STATUS_TONE[job.status]}>{job.status}</StatusBadge>
          </p>
          {state.status === 'job' && <p>{state.message}</p>}
          {isPending && (
            <button type="button" className="biolog-button-secondary" onClick={() => onCancel(job.jobId)}>
              Cancel
            </button>
          )}
          {job.status === 'completed' && job.columns && job.rows && (
            <>
              <table className="biolog-table" aria-label="Report result">
                <thead>
                  <tr>
                    {job.columns.map((column) => (
                      <th scope="col" key={column}>
                        {column}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {job.rows.map((row, i) => (
                    // eslint-disable-next-line react/no-array-index-key -- rows have no stable id of their own
                    <tr key={i}>
                      {row.map((cell, j) => (
                        // eslint-disable-next-line react/no-array-index-key -- cells have no stable id of their own
                        <td key={j}>{cell}</td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
              <button type="button" className="biolog-button-secondary" onClick={() => onExport(job.jobId)}>
                Export CSV
              </button>
            </>
          )}
        </div>
      )}

      {state.status === 'error' && <p role="status">{state.message}</p>}
    </section>
  );
}
