// Developer tests for BIO-BIO-009-AC01/AC02/AC05 (round 3, Seyi's BIO-BIO-009-F03: a real,
// connected mapped workflow, not a disclosed boundary). SYNTHETIC: no real biometric data.
import { expect, test } from '@playwright/test';

test.describe('BIO-BIO-009 privacy & retention controls are real, connected actions on /fingerprints', () => {
  test('the section is reachable from the real route and states it is audited, not storing data', async ({ page }) => {
    await page.goto('/fingerprints');
    await expect(page.getByRole('heading', { name: 'Privacy & retention' })).toBeVisible();
    const section = page.locator('section', { has: page.getByRole('heading', { name: 'Privacy & retention' }) });
    await expect(section.getByRole('note')).toContainText('audited server-side');
  });

  test('updating the retention policy shows the applied category and day count', async ({ page }) => {
    await page.goto('/fingerprints');
    const section = page.locator('section', { has: page.getByRole('heading', { name: 'Privacy & retention' }) });
    await section.getByLabel('Category').selectOption('face_template');
    await section.getByLabel('Retention days').fill('45');
    await section.getByRole('button', { name: 'Update retention policy' }).click();
    await expect(section.getByTestId('policy-result')).toContainText('face_template');
    await expect(section.getByTestId('policy-result')).toContainText('45');
  });

  test('requesting deletion without a record id is a no-op', async ({ page }) => {
    await page.goto('/fingerprints');
    const section = page.locator('section', { has: page.getByRole('heading', { name: 'Privacy & retention' }) });
    await section.getByRole('button', { name: 'Request deletion' }).click();
    await expect(section.getByTestId('deletion-result')).toHaveCount(0);
  });

  test('requesting deletion with a record id shows a typed outcome badge', async ({ page }) => {
    await page.goto('/fingerprints');
    const section = page.locator('section', { has: page.getByRole('heading', { name: 'Privacy & retention' }) });
    await section.getByLabel('Record id').fill('sess-e2e-demo');
    await section.getByRole('button', { name: 'Request deletion' }).click();
    await expect(section.getByTestId('deletion-result')).toBeVisible();
  });
});
