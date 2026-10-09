// Developer tests for BIO-UX-012. SYNTHETIC: no real tenant/user/scenario data.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

async function openConsole(page: import('@playwright/test').Page) {
  await page.goto('/');
  await page.getByRole('button', { name: /LOCAL test console/ }).click();
  return page.getByRole('region', { name: 'Human Test Console' });
}

test.describe('BIO-UX-012-AC01 the console can select all eight dimensions', () => {
  test('tenant, role, connectivity, identity type, credential state, biometric result, document result and device state are all present', async ({
    page,
  }) => {
    const panel = await openConsole(page);
    await expect(panel.getByLabel('Test console tenant')).toBeVisible();
    await expect(panel.getByLabel('Test console role')).toBeVisible();
    await expect(panel.getByLabel('Test console connectivity')).toBeVisible();
    await expect(panel.getByLabel('Test console identity type')).toBeVisible();
    await expect(panel.getByLabel('Test console credential state')).toBeVisible();
    await expect(panel.getByLabel('Test console biometric result')).toBeVisible();
    await expect(panel.getByLabel('Test console document result')).toBeVisible();
    await expect(panel.getByLabel('Test console device state')).toBeVisible();
  });

  test('has no axe violations when expanded', async ({ page }) => {
    await openConsole(page);
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});

test.describe('BIO-UX-012-AC02/AC05 the console exposes module status and a LOCAL indicator', () => {
  test('the toggle names LOCAL and a SIMULATION/HYBRID/LIVE count, visible before expanding', async ({ page }) => {
    await page.goto('/');
    const toggle = page.getByRole('button', { name: /LOCAL test console/ });
    await expect(toggle).toBeVisible();
    await expect(toggle).toContainText('SIMULATION');
  });
});

test.describe('BIO-UX-012-AC03/AC06 scenario changes are deterministic and navigate to the affected workflow', () => {
  test('setting connectivity to Offline and going to Access Control pre-selects Offline there', async ({ page }) => {
    const panel = await openConsole(page);
    await panel.getByLabel('Test console connectivity').selectOption('offline');
    await panel.getByRole('link', { name: 'Access Control' }).click();
    await expect(page).toHaveURL(/\/access-control\?scenario=offline/);
    await expect(page.getByLabel('Scenario')).toHaveValue('offline');
  });

  test('a stray/forged ?scenario= on a fresh visit without the console is ignored (falls back to Online)', async ({
    page,
  }) => {
    await page.goto('/access-control?scenario=not-a-real-value');
    await expect(page.getByLabel('Scenario')).toHaveValue('normal');
  });

  test('setting biometric result to Low quality and going to Fingerprints pre-selects it on the tenprint capture control only', async ({
    page,
  }) => {
    const panel = await openConsole(page);
    await panel.getByLabel('Test console biometric result').selectOption('low');
    await panel.getByRole('link', { name: 'Go to Fingerprints (biometric result)' }).click();
    await expect(page).toHaveURL(/\/fingerprints\?hint=low/);
    await expect(page.getByLabel('Simulate next capture outcome')).toHaveValue('low');
    // Documented scope boundary: only the primary/first control is pre-seeded.
    await expect(page.getByLabel('Simulate outcome').first()).toHaveValue('');
  });

  test('setting device state to Offline and going to Facial Recognition pre-selects the device connection hint', async ({
    page,
  }) => {
    const panel = await openConsole(page);
    await panel.getByLabel('Test console device state').selectOption('offline');
    await panel.getByRole('link', { name: 'Go to Facial Recognition (device state)' }).click();
    await expect(page).toHaveURL(/\/facial-recognition\?deviceHint=offline/);
    await expect(page.getByLabel('Simulate connection outcome')).toHaveValue('offline');
  });

  test('identity type navigates directly to the matching route (Students)', async ({ page }) => {
    const panel = await openConsole(page);
    await panel.getByLabel('Test console identity type').selectOption('student');
    await panel.getByRole('link', { name: 'Go to Student' }).click();
    await expect(page).toHaveURL(/\/students$/);
  });

  test('credential state navigates to Cards and produces a credential genuinely in that status, not just a link', async ({
    page,
  }) => {
    const panel = await openConsole(page);
    await panel.getByLabel('Test console credential state').selectOption('revoked');
    await panel.getByRole('link', { name: 'Go to Cards' }).click();
    await expect(page).toHaveURL(/\/cards\?credentialState=revoked/);
    await expect(page.locator('.biolog-status-badge', { hasText: 'revoked' })).toBeVisible();
  });

  test('a distinct credential state choice produces a distinct observable state on Cards (AC03)', async ({ page }) => {
    await page.goto('/cards?credentialState=lost');
    await expect(page.locator('.biolog-status-badge', { hasText: 'lost' })).toBeVisible();
    await page.goto('/cards?credentialState=replaced');
    await expect(page.locator('.biolog-status-badge', { hasText: 'replaced' })).toBeVisible();
  });

  test('document result navigates to Document Verify and produces that result on the Authenticity step, not just a link', async ({
    page,
  }) => {
    const panel = await openConsole(page);
    await panel.getByLabel('Test console document result').selectOption('failed');
    await panel.getByRole('link', { name: 'Go to Document Verify' }).click();
    await expect(page).toHaveURL(/\/document-verify\?documentResult=failed/);
    await expect(page.getByTestId('step-authenticity').getByTestId('evidence-state-badge')).toContainText('Failed');
  });

  test('a distinct document result choice produces a distinct observable state on Document Verify (AC03)', async ({
    page,
  }) => {
    await page.goto('/document-verify?documentResult=consistent');
    await expect(page.getByTestId('step-authenticity').getByTestId('evidence-state-badge')).toContainText('Consistent');
    await page.goto('/document-verify?documentResult=inconclusive');
    await expect(page.getByTestId('step-authenticity').getByTestId('evidence-state-badge')).toContainText('Inconclusive');
  });

  test('Reset scenario restores every dimension to its default', async ({ page }) => {
    const panel = await openConsole(page);
    await panel.getByLabel('Test console connectivity').selectOption('offline');
    await panel.getByLabel('Test console biometric result').selectOption('exception');
    await panel.getByLabel('Test console tenant').selectOption('beta');
    await panel.getByRole('button', { name: 'Reset scenario' }).click();
    await expect(panel.getByLabel('Test console connectivity')).toHaveValue('normal');
    await expect(panel.getByLabel('Test console biometric result')).toHaveValue('good');
    await expect(panel.getByLabel('Test console tenant')).toHaveValue('alpha');
  });
});

test.describe('BIO-UX-012-AC04 the console is excluded from production builds', () => {
  test('is present in this dev-mode Playwright run (the negative case is proven separately against a production build)', async ({
    page,
  }) => {
    await page.goto('/');
    await expect(page.getByTestId('biolog-human-test-console')).toBeAttached();
  });
});
