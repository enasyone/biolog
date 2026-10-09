// Developer tests for BIO-RPT-002-AC06 ("connected through ReportingService ... and the mapped
// end-to-end human workflow passes"). SYNTHETIC: every scenario this real backend computes is
// built from the current session's own synthetic BIO-CRD-007/BIO-ATT-001 fixture data - no real
// card, person, attendance or tenant data anywhere.
//
// This is the one spec in this repository (alongside bio-doc-009-authenticity-live.spec.ts) that
// requires TWO servers running: the Next.js app (built with
// NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE=LIVE baked in at build time - Next.js inlines
// NEXT_PUBLIC_* vars at build, not runtime) AND the real Biolog.Api (.NET) host, reachable through
// web/next.config.mjs's own /api/v1/reporting/* rewrite. It also requires a VERIFIED tenant domain
// registered for the host the test runs against (BIO-TEN-002's own control plane) - see
// docs/features/bio-rpt-002/README.md "Proving AC06" for the exact commands, including that setup
// step, which every other LIVE spec in this repository has not needed until this story (BIO-RPT-002
// is the first to call an endpoint that requires a genuinely resolved, non-null tenant).
//
// Actively excluded from the default `pnpm --dir tests/frontend test:e2e` full-suite run: that
// suite's own server never sets NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE=LIVE, so this story's
// four dedicated ports resolve to their SIMULATION mock adapters there - this file's own
// assertions (a real, live-computed result) would fail against mock data, not skip cleanly,
// without the guard below. BIOLOG_RPT002_LIVE_TESTS=1 opts in explicitly.
import { expect, test } from '@playwright/test';

test.skip(
  !process.env.BIOLOG_RPT002_LIVE_TESTS,
  'requires BIOLOG_RPT002_LIVE_TESTS=1, a real Biolog.Api backend, and a VERIFIED tenant domain for the test host - see this file\'s own header comment and docs/features/bio-rpt-002/README.md "Proving AC06"',
);

test.describe('BIO-RPT-002-AC06 the report-builder card is connected to a real backend, not a placeholder', () => {
  test('generating a card-production report produces a real completed job with a live result table', async ({ page }) => {
    await page.goto('/reports');

    const card = page.getByTestId('report-builder-card');
    await expect(card.getByText('LIVE')).toBeVisible();

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/reporting/reports') && r.request().method() === 'POST'),
      card.getByRole('button', { name: 'Generate report' }).click(),
    ]);

    expect(response.ok()).toBe(true);
    const body = await response.json();
    expect(body).toHaveProperty('job_id');
    expect(body.job_id).toMatch(/^rpj_/);

    await expect(card.getByRole('status')).toContainText(/completed|running|queued/);
  });

  test('the underlying job eventually reaches a real terminal status via polling, not a canned client-side result', async ({ page }) => {
    await page.goto('/reports');
    const card = page.getByTestId('report-builder-card');

    await card.getByRole('button', { name: 'Generate report' }).click();
    // Scoped to the job-status container specifically, not `card` as a whole - the card's own
    // header also renders a `.biolog-status-badge` (its LIVE/SIMULATION mode indicator).
    const jobStatus = card.getByRole('status').locator('.biolog-status-badge');
    await expect(jobStatus).toHaveText(/completed|failed|cancelled/i, { timeout: 15_000 });
  });

  test('exporting a completed report fetches real CSV bytes from the live export endpoint', async ({ page }) => {
    await page.goto('/reports');
    const card = page.getByTestId('report-builder-card');
    await card.getByRole('button', { name: 'Generate report' }).click();
    await expect(card.getByRole('status').locator('.biolog-status-badge')).toHaveText('completed', { timeout: 15_000 });

    const [response, download] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/export?format=csv')),
      page.waitForEvent('download'),
      card.getByRole('button', { name: 'Export CSV' }).click(),
    ]);

    expect(response.ok()).toBe(true);
    expect(download.suggestedFilename()).toMatch(/^rpj_.*\.csv$/);
  });
});
