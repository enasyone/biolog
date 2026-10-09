// Developer tests for BIO-CRD-008-AC01/AC02/AC05. SYNTHETIC: no real card designs. SIMULATION
// mode, runs in the default suite - see bio-crd-008-template-lifecycle-live.spec.ts for the
// real dual-server LIVE proof.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('BIO-CRD-008 the card-template lifecycle bar (SIMULATION)', () => {
  test('AC05: mounted on /card-designer alongside the existing canvas and Save design action', async ({ page }) => {
    await page.goto('/card-designer');
    await expect(page.getByRole('heading', { name: 'Card Designer' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save design' })).toBeVisible(); // BIO-UX-007's own action, untouched
    await expect(page.getByTestId('template-lifecycle-bar')).toBeVisible();
  });

  test('AC01/AC02: the full lifecycle - create draft, submit, approve, publish, retire, then a new version', async ({ page }) => {
    await page.goto('/card-designer');
    const bar = page.getByTestId('template-lifecycle-bar');

    await bar.getByLabel('Template name').fill('Staff ID v1');
    await bar.getByRole('button', { name: 'Create draft from current canvas' }).click();

    const status = bar.getByRole('status', { name: 'Selected template status' });
    await expect(status.locator('.biolog-status-badge')).toHaveText('draft');

    await status.getByRole('button', { name: 'Submit for validation' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('validation');

    await status.getByRole('button', { name: 'Validation passed' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('approval');

    await status.getByRole('button', { name: 'Publish' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('published');

    // AC01: now immutable - no edit control is offered at all once published; only retire/new-version.
    await expect(status.getByRole('button', { name: 'Retire' })).toBeVisible();
    await expect(status.getByRole('button', { name: 'Create new version' })).toBeVisible();

    await status.getByRole('button', { name: 'Retire' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('retired');

    await status.getByRole('button', { name: 'Create new version' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('draft'); // AC02: a brand-new version, not the retired one reopened
  });

  test('a rejected validation sends the template back to draft, not a dead end', async ({ page }) => {
    await page.goto('/card-designer');
    const bar = page.getByTestId('template-lifecycle-bar');
    await bar.getByLabel('Template name').fill('Needs rework');
    await bar.getByRole('button', { name: 'Create draft from current canvas' }).click();

    const status = bar.getByRole('status', { name: 'Selected template status' });
    await status.getByRole('button', { name: 'Submit for validation' }).click();
    await status.getByRole('button', { name: 'Validation failed' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('draft');
  });

  test('has no axe violations after creating a draft and viewing its status', async ({ page }) => {
    await page.goto('/card-designer');
    const bar = page.getByTestId('template-lifecycle-bar');
    await bar.getByLabel('Template name').fill('Accessibility check');
    await bar.getByRole('button', { name: 'Create draft from current canvas' }).click();
    await expect(bar.getByRole('status', { name: 'Selected template status' })).toBeVisible();

    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
