// Developer tests for BIO-RPT-003-AC01/AC02/AC03/AC05. SYNTHETIC: no real audit data. SIMULATION
// mode, runs in the default suite - see bio-rpt-003-audit-investigation-live.spec.ts for the real
// dual-server LIVE proof.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('BIO-RPT-003 the audit investigation panel (SIMULATION)', () => {
  test('AC05: mounted on /audit alongside BIO-RPT-001\'s own audit trail card, untouched', async ({ page }) => {
    await page.goto('/audit');
    // exact: true - a substring match against 'Audit' also matches this panel's own new
    // 'Investigate audit events' heading (a real Playwright strict-mode collision, caught before
    // commit, the same class of self-caught locator-scoping bug BIO-RPT-002 hit in its own suite).
    await expect(page.getByRole('heading', { name: 'Audit', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'View audit trail' })).toBeVisible(); // BIO-RPT-001's own action, untouched
    await expect(page.getByTestId('audit-investigation-panel')).toBeVisible();
  });

  test('AC01: searching with no filter, then a specific action filter, narrows the results', async ({ page }) => {
    await page.goto('/audit');
    const panel = page.getByTestId('audit-investigation-panel');

    await panel.getByTestId('audit-investigation-search').click();
    const summary = panel.getByTestId('audit-investigation-summary');
    await expect(summary).toContainText('matching audit event');
    const allRows = panel.locator('tbody tr');
    const totalRows = await allRows.count();
    expect(totalRows).toBeGreaterThan(1);

    await panel.getByLabel('Action').fill('card_template.publish');
    await panel.getByTestId('audit-investigation-search').click();
    await expect(panel.locator('tbody tr')).toHaveCount(1);
    await expect(panel.locator('tbody tr').first()).toContainText('card_template.publish');
  });

  test('AC01: a filter matching nothing shows the empty state, not an error', async ({ page }) => {
    await page.goto('/audit');
    const panel = page.getByTestId('audit-investigation-panel');
    await panel.getByLabel('Reason code').fill('no_such_reason_code');
    await panel.getByTestId('audit-investigation-search').click();
    await expect(panel.getByText('No matching audit events')).toBeVisible();
  });

  test('AC02: no edit/delete control of any kind is offered anywhere on this panel', async ({ page }) => {
    await page.goto('/audit');
    const panel = page.getByTestId('audit-investigation-panel');
    await panel.getByTestId('audit-investigation-search').click();
    await expect(panel.getByRole('button', { name: /^(Edit|Delete|Remove)/i })).toHaveCount(0);
  });

  test('AC03: export downloads a CSV file', async ({ page }) => {
    await page.goto('/audit');
    const panel = page.getByTestId('audit-investigation-panel');

    const [download] = await Promise.all([
      page.waitForEvent('download'),
      panel.getByTestId('audit-investigation-export').click(),
    ]);

    expect(download.suggestedFilename()).toBe('audit-investigation-export.csv');
  });

  test('has no axe violations after searching', async ({ page }) => {
    await page.goto('/audit');
    await page.getByTestId('audit-investigation-search').click();
    await expect(page.getByTestId('audit-investigation-summary')).toBeVisible();

    const results = await new AxeBuilder({ page }).include('[data-testid="audit-investigation-panel"]').analyze();
    expect(results.violations).toEqual([]);
  });
});
