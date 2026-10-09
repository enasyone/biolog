// Developer tests for BIO-RPT-002-AC01, AC02, AC04. SYNTHETIC fixed data only.
// Boundary behaviours a Playwright click-through cannot reach cleanly (an offline call needs 5
// consecutive clicks; a job-not-found/cancel-refused/export-before-completion case has no
// natural path through the real UI) - BIO-UX-005-AC05's own "reproduce ... boundary behaviours"
// bar, met here at the port level directly, the same way bio-ux-010-module-ports.test.ts does
// for its own five modules.
import { describe, expect, it, vi } from 'vitest';
import {
  reportingGenerateReportMockPort,
  reportingGetReportStatusMockPort,
  reportingCancelReportMockPort,
  reportingExportReportMockPort,
} from '../../../web/features/reporting/mock';
import type { GenerateReportPort } from '../../../web/features/reporting/ports';

describe('BIO-RPT-002 reporting mock ports', () => {
  it('AC01 a missing input is a handled failure, not a thrown exception, for every one of the four ports', async () => {
    await expect(reportingGenerateReportMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
    await expect(reportingGetReportStatusMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
    await expect(reportingCancelReportMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
    await expect(reportingExportReportMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
  });

  it('AC01 generating a report rejects a dimension not whitelisted for the chosen dataset, mirroring the real backend', async () => {
    const result = await reportingGenerateReportMockPort.primaryAction({ dataset: 'card_production', dimension: 'mode', measure: 'count' });
    expect(result.outcome).toBe('failure');
    expect(result.job).toBeUndefined();
  });

  it('AC04 every 5th call to generateReport simulates an offline call before any job is created', async () => {
    // A freshly re-imported module instance, not the shared `reportingGenerateReportMockPort`
    // singleton every other test in this file also calls - the offline gate's call counter is
    // per-module-instance state (see mock.ts's own header), so sharing the singleton here would
    // make this test's outcome depend on execution order relative to every other test.
    vi.resetModules();
    const fresh = await import('../../../web/features/reporting/mock');
    const port: GenerateReportPort = fresh.reportingGenerateReportMockPort;

    const outcomes: string[] = [];
    for (let i = 0; i < 5; i += 1) {
      // eslint-disable-next-line no-await-in-loop -- deliberately sequential: this call counter is per-adapter-instance
      const result = await port.primaryAction({ dataset: 'card_production', dimension: 'status', measure: 'count' });
      outcomes.push(result.outcome);
    }
    expect(outcomes[4]).toBe('offline');
    expect(outcomes.slice(0, 4).every((o) => o === 'success')).toBe(true);
  });

  it('AC04 a job id unknown to getReportStatus/cancelReport/exportReport is refused honestly, not treated as any default job', async () => {
    const status = await reportingGetReportStatusMockPort.primaryAction({ jobId: 'rpj-does-not-exist' });
    expect(status.outcome).toBe('failure');

    const cancel = await reportingCancelReportMockPort.primaryAction({ jobId: 'rpj-does-not-exist' });
    expect(cancel.outcome).toBe('failure');

    const exportResult = await reportingExportReportMockPort.primaryAction({ jobId: 'rpj-does-not-exist' });
    expect(exportResult.outcome).toBe('failure');
  });

  it('AC04 a mock job always resolves Completed synchronously, so cancel always refuses it - honest, not a fabricated success', async () => {
    const generated = await reportingGenerateReportMockPort.primaryAction({ dataset: 'attendance', dimension: 'source', measure: 'count' });
    expect(generated.outcome).toBe('success');
    expect(generated.job?.status).toBe('completed');

    const cancel = await reportingCancelReportMockPort.primaryAction({ jobId: generated.job!.jobId });
    expect(cancel.outcome).toBe('failure');
    expect(cancel.job?.status).toBe('completed');
  });

  it('AC02 exporting a completed job returns real CSV text matching its own columns/rows', async () => {
    const generated = await reportingGenerateReportMockPort.primaryAction({ dataset: 'card_production', dimension: 'card_class', measure: 'count' });
    const exportResult = await reportingExportReportMockPort.primaryAction({ jobId: generated.job!.jobId });

    expect(exportResult.outcome).toBe('success');
    expect(exportResult.csv).toContain('card_class,count');
    for (const [value, count] of generated.job!.rows!) {
      expect(exportResult.csv).toContain(`${value},${count}`);
    }
  });

  it('AC04 getReportStatus reflects the same job a prior generateReport call created (a real shared store, not per-call state)', async () => {
    const generated = await reportingGenerateReportMockPort.primaryAction({ dataset: 'attendance', dimension: 'verification_method', measure: 'count' });
    const status = await reportingGetReportStatusMockPort.primaryAction({ jobId: generated.job!.jobId });

    expect(status.outcome).toBe('success');
    expect(status.job).toEqual(generated.job);
  });
});
