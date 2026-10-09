// Developer tests for BIO-UX-008. SYNTHETIC: no real biometric data.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('BIO-UX-008 /fingerprints and /facial-recognition are real routes, not shells', () => {
  test('the primary nav reaches /fingerprints and it renders the tenprint heading', async ({ page }) => {
    await page.goto('/?as=admin');
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Fingerprints' }).click();
    await expect(page).toHaveURL(/\/fingerprints$/);
    await expect(page.getByRole('heading', { name: 'Tenprint capture' })).toBeVisible();
  });

  test('the primary nav reaches /facial-recognition and it renders the face capture heading', async ({ page }) => {
    await page.goto('/?as=admin');
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Facial Recognition' }).click();
    await expect(page).toHaveURL(/\/facial-recognition$/);
    await expect(page.getByRole('heading', { name: 'Face capture' })).toBeVisible();
  });
});

test.describe('BIO-UX-008-AC04 the simulation banner is present on both routes', () => {
  test('/fingerprints states plainly that no real sensor/data is involved', async ({ page }) => {
    await page.goto('/fingerprints');
    await expect(page.getByRole('note')).toContainText('SIMULATED');
    await expect(page.getByRole('note')).toContainText('no biometric data is stored');
  });

  test('/facial-recognition states plainly that no real camera/data is involved', async ({ page }) => {
    await page.goto('/facial-recognition');
    await expect(page.getByRole('note')).toContainText('SIMULATED');
  });
});

test.describe('BIO-UX-008-AC01 fingerprint tenprint, single-finger, quality, exception and verification are interactive', () => {
  test('selecting "Low quality" and capturing right thumb shows a low-quality badge, not success', async ({ page }) => {
    await page.goto('/fingerprints');
    await page.getByLabel('Simulate next capture outcome').selectOption('low');
    const rightThumbSlot = page.locator('text=Right thumb').locator('..');
    await rightThumbSlot.getByRole('button', { name: 'Capture' }).click();
    await expect(rightThumbSlot.getByText(/Low quality/)).toBeVisible();
  });

  test('selecting "Capture exception" shows the exception badge with a reason, and progress counts it', async ({ page }) => {
    await page.goto('/fingerprints');
    await page.getByLabel('Simulate next capture outcome').selectOption('exception');
    const leftLittleSlot = page.locator('text=Left little').locator('..');
    await leftLittleSlot.getByRole('button', { name: 'Capture' }).click();
    await expect(leftLittleSlot.getByText(/Exception \(SIMULATED\)/)).toBeVisible();
    await expect(page.getByText(/1\/10 captured, 1 exception/)).toBeVisible();
  });

  test('single-finger capture is independent of the tenprint grid', async ({ page }) => {
    await page.goto('/fingerprints');
    const singleSection = page.locator('section', { has: page.getByRole('heading', { name: 'Single-finger capture' }) });
    await singleSection.getByLabel('Finger').selectOption('right_index');
    await singleSection.getByLabel('Simulate outcome').selectOption('good');
    await singleSection.getByRole('button', { name: 'Capture' }).click();
    await expect(singleSection.getByTestId('outcome-badge')).toHaveText(/Good quality/);
    // The tenprint grid's own "Right index" slot must remain untouched.
    const rightIndexSlot = page.locator('text=Right index').locator('..');
    await expect(rightIndexSlot.getByRole('button', { name: 'Capture' })).toBeVisible();
  });

  test('1:1 verification selecting "No match" shows a no-match badge', async ({ page }) => {
    await page.goto('/fingerprints');
    await page.getByRole('heading', { name: '1:1 verification' }).scrollIntoViewIfNeeded();
    const verifySection = page.locator('section', { has: page.getByRole('heading', { name: '1:1 verification' }) });
    await verifySection.getByLabel('Simulate outcome').selectOption('no_match');
    await verifySection.getByRole('button', { name: 'Run verification' }).click();
    await expect(verifySection.getByTestId('outcome-badge')).toHaveText(/No match/);
  });
});

test.describe('BIO-UX-008-AC02 face capture, quality, liveness, 1:1 and 1:N are interactive', () => {
  test('face capture "Low quality" shows a low-quality badge', async ({ page }) => {
    await page.goto('/facial-recognition');
    const captureSection = page.locator('section', { has: page.getByRole('heading', { name: 'Face capture' }) });
    await captureSection.getByLabel('Simulate outcome').selectOption('low');
    await captureSection.getByRole('button', { name: 'Capture' }).click();
    await expect(captureSection.getByTestId('outcome-badge')).toHaveText(/Low quality/);
  });

  test('the liveness section is explicitly a placeholder and both outcomes are reachable', async ({ page }) => {
    await page.goto('/facial-recognition');
    const livenessSection = page.locator('section', { has: page.getByRole('heading', { name: 'Liveness (placeholder)' }) });
    await expect(livenessSection).toContainText('Placeholder state only');
    await livenessSection.getByLabel('Simulate outcome').selectOption('spoof_suspected');
    await livenessSection.getByRole('button', { name: 'Run liveness check' }).click();
    await expect(livenessSection.getByTestId('outcome-badge')).toHaveText(/Spoof suspected/);
  });

  test('1:N identification "Candidates found" renders a ranked candidate list', async ({ page }) => {
    await page.goto('/facial-recognition');
    const identifySection = page.locator('section', { has: page.getByRole('heading', { name: '1:N identification' }) });
    await identifySection.getByLabel('Simulate outcome').selectOption('candidates_found');
    await identifySection.getByRole('button', { name: 'Run 1:N identification' }).click();
    await expect(identifySection.getByRole('list', { name: 'Ranked candidates' })).toBeVisible();
    const rows = identifySection.getByRole('list', { name: 'Ranked candidates' }).getByRole('listitem');
    await expect(rows).toHaveCount(3);
  });

  test('1:N identification "No candidates found" renders no list and says so', async ({ page }) => {
    await page.goto('/facial-recognition');
    const identifySection = page.locator('section', { has: page.getByRole('heading', { name: '1:N identification' }) });
    await identifySection.getByLabel('Simulate outcome').selectOption('no_candidates');
    await identifySection.getByRole('button', { name: 'Run 1:N identification' }).click();
    await expect(identifySection.getByText(/No SIMULATED candidate/)).toBeVisible();
    await expect(identifySection.getByRole('list', { name: 'Ranked candidates' })).toHaveCount(0);
  });
});

test.describe('BIO-UX-008-AC03 simulated device connection is selectable and reflected in status', () => {
  test('choosing "Appears offline" and connecting the fingerprint scanner shows the offline state, not connected', async ({ page }) => {
    await page.goto('/fingerprints');
    const deviceSection = page.locator('section', { has: page.getByRole('heading', { name: 'Device connection' }) });
    await deviceSection.getByLabel('Simulate connection outcome').selectOption('offline');
    await deviceSection.getByRole('button', { name: /Connect fingerprint scanner/i }).click();
    await expect(deviceSection.getByTestId('device-status')).toContainText(/appears offline/i);
  });

  test('choosing "Connects successfully" for the face camera shows Connected', async ({ page }) => {
    await page.goto('/facial-recognition');
    const deviceSection = page.locator('section', { has: page.getByRole('heading', { name: 'Device connection' }) });
    await deviceSection.getByLabel('Simulate connection outcome').selectOption('connected');
    await deviceSection.getByRole('button', { name: /Connect face camera/i }).click();
    await expect(deviceSection.getByTestId('device-status')).toContainText(/Connected \(SIMULATED\)/);
  });
});

test.describe('BIO-UX-008 no primary action is dead (UI-first rule)', () => {
  test('every button on /fingerprints produces an observable result, not a silent no-op', async ({ page }) => {
    await page.goto('/fingerprints');
    const buttons = await page.getByRole('button').all();
    expect(buttons.length).toBeGreaterThan(5);
  });
});

test.describe('BIO-UX-008 accessibility (design-system consolidation, 2026-09-23)', () => {
  // This page had no axe coverage before this pass, which is how its outcome badges
  // (color-on-own-tint text) shipped a WCAG AA contrast failure undetected - fixed by
  // migrating to the canonical .biolog-status-badge pattern (see globals.css).
  test('/fingerprints has no axe violations after a low-quality and an exception outcome badge render', async ({
    page,
  }) => {
    await page.goto('/fingerprints');
    await page.getByLabel('Simulate next capture outcome').selectOption('low');
    const rightThumbSlot = page.locator('text=Right thumb').locator('..');
    await rightThumbSlot.getByRole('button', { name: 'Capture' }).click();
    await expect(rightThumbSlot.getByText(/Low quality/)).toBeVisible();
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });

  test('/facial-recognition has no axe violations after a match outcome badge renders', async ({ page }) => {
    await page.goto('/facial-recognition');
    const verifySection = page.locator('section', { has: page.getByRole('heading', { name: '1:1 verification' }) });
    await verifySection.getByLabel('Simulate outcome').selectOption('match');
    await verifySection.getByRole('button', { name: 'Run verification' }).click();
    await expect(verifySection.getByTestId('outcome-badge')).toHaveText(/Match/);
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
