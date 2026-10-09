// Developer tests for BIO-INT-005-AC05 ("`/administration/integrations` is wired to a real
// `IntegrationService` surface for conflict review"). SYNTHETIC: no real data. SIMULATION mode
// (the default IntegrationService mock adapter), runs in the default suite - proves the mapped
// end-to-end human workflow (scan -> see conflicting fields -> record a decision -> see the
// decision persist) with real, visible success/failure states, the same bar
// bio-int-004-webhooks.spec.ts already establishes for a panel with no real backend wired into
// this default suite.
//
// `/settings/authentication` is deliberately NOT covered here - this story's own
// `IntegrationService` port keys (`scanConflicts`/`listConflicts`/`decideConflict`) are never
// routed there, the same disclosed scope boundary BIO-INT-003/004's own README already
// established for that page.
import { expect, test } from '@playwright/test';

test.describe('BIO-INT-005 the Conflict Review panel (SIMULATION)', () => {
  test('AC05: mounted on /administration/integrations, alongside the existing Import Console, Service Accounts and Webhooks panels, untouched', async ({ page }) => {
    await page.goto('/administration/integrations');
    await expect(page.getByRole('heading', { name: 'Conflict Review', exact: true })).toBeVisible();
    const panel = page.getByTestId('conflict-review-panel');
    await expect(panel).toBeVisible();
    await expect(page.getByRole('heading', { name: 'API credentials' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Webhooks', exact: true })).toBeVisible();
  });

  test('AC02: refreshing before ever scanning sees an explicit empty state, never a silent blank table', async ({ page }) => {
    await page.goto('/administration/integrations');
    const panel = page.getByTestId('conflict-review-panel');

    await panel.getByTestId('conflict-review-refresh').click();

    await expect(panel.getByTestId('conflict-review-empty')).toBeVisible();
    await expect(panel.getByTestId('conflict-review-row')).toHaveCount(0);
  });

  test('AC01: scanning surfaces conflicting rows with the exact existing-vs-incoming field comparison', async ({ page }) => {
    await page.goto('/administration/integrations');
    const panel = page.getByTestId('conflict-review-panel');

    await panel.getByTestId('conflict-review-scan').click();

    await expect(panel.getByTestId('conflict-review-notice').or(panel.getByTestId('conflict-review-table'))).toBeVisible();
    const rows = panel.getByTestId('conflict-review-row');
    await expect(rows).toHaveCount(2);
    await expect(rows.first()).toContainText('existing');
    await expect(rows.first()).toContainText('incoming');
    await expect(rows.first().getByTestId('conflict-review-status')).toHaveText('PENDING REVIEW');
  });

  test('AC02/AC03: recording a decision requires a reason, is audited visibly, and the row leaves the decidable state', async ({ page }) => {
    await page.goto('/administration/integrations');
    const panel = page.getByTestId('conflict-review-panel');
    await panel.getByTestId('conflict-review-scan').click();
    const row = panel.getByTestId('conflict-review-row').first();

    // Missing reason: a visible, accessible failure state, never a silent no-op.
    await row.getByTestId('conflict-review-decide').click();
    const notice = panel.getByTestId('conflict-review-notice');
    await expect(notice).toHaveAttribute('role', 'alert');
    await expect(notice).toContainText('reason is required');
    await expect(row.getByTestId('conflict-review-status')).toHaveText('PENDING REVIEW');

    // A real decision, with a reason, succeeds visibly.
    await row.getByTestId('conflict-review-decision-select').selectOption('KEEP_EXISTING');
    await row.getByTestId('conflict-review-reason-input').fill('existing value already verified');
    await row.getByTestId('conflict-review-decide').click();

    await expect(notice).toContainText('keep existing');
    await expect(row.getByTestId('conflict-review-status')).toHaveText('KEEP EXISTING');
    // Decided rows are no longer decidable - the decision controls disappear.
    await expect(row.getByTestId('conflict-review-decision-select')).toHaveCount(0);
    await expect(row.getByTestId('conflict-review-decide')).toHaveCount(0);
  });

  test('AC04: a USE_SOURCE decision records the choice but the field comparison text is unchanged - nothing is applied', async ({ page }) => {
    await page.goto('/administration/integrations');
    const panel = page.getByTestId('conflict-review-panel');
    await panel.getByTestId('conflict-review-scan').click();
    const row = panel.getByTestId('conflict-review-row').first();
    const fieldTextBefore = await row.getByTestId('conflict-review-field').first().innerText();

    await row.getByTestId('conflict-review-decision-select').selectOption('USE_SOURCE');
    await row.getByTestId('conflict-review-reason-input').fill('source system is authoritative here');
    await row.getByTestId('conflict-review-decide').click();

    await expect(panel.getByTestId('conflict-review-notice')).toContainText('use source');
    await expect(row.getByTestId('conflict-review-status')).toHaveText('USE SOURCE');
    const fieldTextAfter = await row.getByTestId('conflict-review-field').first().innerText();
    expect(fieldTextAfter).toBe(fieldTextBefore);
  });

  test('a decision persists across a refresh - the queue reflects durable state, not just local UI state', async ({ page }) => {
    await page.goto('/administration/integrations');
    const panel = page.getByTestId('conflict-review-panel');
    await panel.getByTestId('conflict-review-scan').click();
    const row = panel.getByTestId('conflict-review-row').first();
    await row.getByTestId('conflict-review-decision-select').selectOption('ESCALATED');
    await row.getByTestId('conflict-review-reason-input').fill('needs a second opinion');
    await row.getByTestId('conflict-review-decide').click();
    await expect(row.getByTestId('conflict-review-status')).toHaveText('ESCALATED');

    await panel.getByTestId('conflict-review-refresh').click();

    await expect(panel.getByTestId('conflict-review-row').first().getByTestId('conflict-review-status')).toHaveText('ESCALATED');
  });
});
