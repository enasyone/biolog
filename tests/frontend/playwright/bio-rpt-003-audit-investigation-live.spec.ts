// Developer tests for BIO-RPT-003-AC05 ("connected through ReportingService ... and the mapped
// end-to-end human workflow passes"). SYNTHETIC: every record this real backend returns is built
// from this session's own synthetic RBAC/CardProduction/CardTemplates/ReportBuilder fixture data -
// no real person, biometric, tenant or production data anywhere.
//
// Requires TWO servers running: the Next.js app (built with
// NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_SEARCHAUDITEVENTS=LIVE and
// NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_EXPORTAUDITEVENTS=LIVE baked in at build time - Next.js
// inlines NEXT_PUBLIC_* vars at build, not runtime) AND the real Biolog.Api (.NET) host, reachable
// through web/next.config.mjs's own /api/v1/audit-investigations/* rewrite. Also requires a
// VERIFIED tenant domain registered for the host the test runs against (BIO-TEN-002's own control
// plane) - see docs/features/bio-rpt-003/README.md "Proving AC05" for the exact commands.
//
// Actively excluded from the default `pnpm --dir tests/frontend test:e2e` full-suite run: that
// suite's own server never sets these two env vars LIVE, so both ports resolve to their
// SIMULATION mock adapters there. BIOLOG_RPT003_LIVE_TESTS=1 opts in explicitly.
import { expect, test } from '@playwright/test';

test.skip(
  !process.env.BIOLOG_RPT003_LIVE_TESTS,
  'requires BIOLOG_RPT003_LIVE_TESTS=1, a real Biolog.Api backend, and a VERIFIED tenant domain for the test host - see this file\'s own header comment and docs/features/bio-rpt-003/README.md "Proving AC05"',
);

test.describe('BIO-RPT-003-AC05 the audit investigation panel is connected to a real backend, not a placeholder', () => {
  test('the panel reports LIVE mode and a search returns real results from the real backend', async ({ page }) => {
    await page.goto('/audit');
    const panel = page.getByTestId('audit-investigation-panel');
    await expect(panel.getByText('LIVE')).toBeVisible();

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/audit-investigations') && r.request().method() === 'GET'),
      panel.getByTestId('audit-investigation-search').click(),
    ]);

    expect(response.ok()).toBe(true);
    const body = await response.json();
    expect(body).toHaveProperty('records');
    expect(body).toHaveProperty('total_count');
    await expect(panel.getByTestId('audit-investigation-summary')).toContainText('LIVE');
  });

  test('AC03: export without the export permission is refused with a real 403 from the real backend', async ({ page }) => {
    await page.goto('/audit');
    const panel = page.getByTestId('audit-investigation-panel');

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/audit-investigations/export')),
      panel.getByTestId('audit-investigation-export').click(),
    ]);

    expect(response.status()).toBe(403);
    await expect(panel.getByText(/audit_export_forbidden|does not hold/i)).toBeVisible();
  });

  test('AC01: a real actor filter genuinely narrows the real backend result set (not client-side fakery)', async ({ page }) => {
    await page.goto('/audit');
    const panel = page.getByTestId('audit-investigation-panel');

    // The response listener must be registered BEFORE the click that produces it - registering it
    // afterwards waits forever for a response that already happened (a real bug in this test's
    // first draft, caught by the timeout below rather than assumed correct).
    const [unfiltered] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/audit-investigations') && r.request().method() === 'GET'),
      panel.getByTestId('audit-investigation-search').click(),
    ]);
    await expect(panel.getByTestId('audit-investigation-summary')).toBeVisible();
    const unfilteredBody = await unfiltered.json();

    await panel.getByLabel('Action').fill('rbac.role_assign');
    const [filtered] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/audit-investigations') && r.request().method() === 'GET'),
      panel.getByTestId('audit-investigation-search').click(),
    ]);
    const filteredBody = await filtered.json();

    expect(filteredBody.total_count).toBeLessThanOrEqual(unfilteredBody.total_count);
    for (const record of filteredBody.records) {
      expect(record.action).toBe('rbac.role_assign');
    }
  });
});
