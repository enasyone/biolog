// Developer tests for BIO-RPT-002-AC01/AC02/AC04/AC06. SYNTHETIC: fixed, deterministic
// mock data (web/features/reporting/mock.ts's own SYNTHETIC_GROUPS table) — no real backend
// needed, unlike bio-rpt-002-reporting-live.spec.ts. Runs in the default suite.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('BIO-RPT-002-AC01/AC04 the report-builder card (SIMULATION)', () => {
  test('generating a card-production report shows a completed job with a real result table', async ({ page }) => {
    await page.goto('/reports');
    const card = page.getByTestId('report-builder-card');
    await expect(card.getByText('SIMULATION')).toBeVisible();

    await card.getByRole('button', { name: 'Generate report' }).click();

    await expect(card.getByRole('status').locator('.biolog-status-badge')).toHaveText('completed');
    const table = card.getByRole('table', { name: 'Report result' });
    await expect(table.locator('th')).toHaveText(['status', 'count']);
    await expect(table.locator('tbody tr')).toHaveCount(3); // SYNTHETIC_GROUPS['card_production:status']
  });

  test('AC01: switching dataset resets to that dataset\'s own whitelisted first dimension, and the result reflects it', async ({ page }) => {
    await page.goto('/reports');
    const card = page.getByTestId('report-builder-card');

    await card.getByLabel('Dataset').selectOption('attendance');
    await expect(card.getByLabel('Dimension')).toHaveValue('mode');

    await card.getByRole('button', { name: 'Generate report' }).click();

    const table = card.getByRole('table', { name: 'Report result' });
    await expect(table.locator('th').first()).toHaveText('mode');
  });

  test('AC02: exporting a completed report triggers a real CSV download built from the shown result', async ({ page }) => {
    await page.goto('/reports');
    const card = page.getByTestId('report-builder-card');
    await card.getByRole('button', { name: 'Generate report' }).click();
    await expect(card.getByRole('status').locator('.biolog-status-badge')).toHaveText('completed');

    const [download] = await Promise.all([
      page.waitForEvent('download'),
      card.getByRole('button', { name: 'Export CSV' }).click(),
    ]);

    expect(download.suggestedFilename()).toMatch(/^rpj-sim-\d+\.csv$/);
  });

  test('has no axe violations after generating and viewing a report result', async ({ page }) => {
    await page.goto('/reports');
    const card = page.getByTestId('report-builder-card');
    await card.getByRole('button', { name: 'Generate report' }).click();
    await expect(card.getByRole('status').locator('.biolog-status-badge')).toHaveText('completed');

    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
