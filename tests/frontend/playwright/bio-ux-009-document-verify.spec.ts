// Developer tests for BIO-UX-009. SYNTHETIC: no real document/biometric data.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

/**
 * Every step's deterministic scenario cycle (`evidence.ts`) does NOT always
 * land on a positive outcome on its first call — e.g. `capture`'s own first
 * deterministic call is a `failed` scenario (the same "a module's first
 * call can legitimately be non-success" precedent BIO-UX-005's own
 * `bio-ux-005-adapter-framework.spec.ts` documents for `/devices`). Rather
 * than hardcode "click N times" (fragile against a future change to a
 * scenario table's order/length), this helper clicks a step's button until
 * its state badge reads `targetLabel`, bounded so a genuine regression
 * (the state never reachable) fails loudly instead of hanging.
 */
async function clickUntilBadge(page: Page, testId: string, targetLabel: string, maxAttempts = 8): Promise<void> {
  const card = page.getByTestId(testId);
  const button = card.getByRole('button');
  for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
    await button.click();
    await expect(card.getByRole('status')).toBeVisible();
    const label = await card.getByTestId('evidence-state-badge').textContent();
    if (label === targetLabel) return;
  }
  throw new Error(`${testId}: never reached badge "${targetLabel}" within ${maxAttempts} attempts`);
}

async function captureSuccessfully(page: Page): Promise<void> {
  await clickUntilBadge(page, 'step-capture', 'Read');
  await expect(page.getByTestId('step-mrz').getByRole('button')).toBeEnabled();
}

test.describe('BIO-UX-009 the Document Verify route is real, not a placeholder', () => {
  test('is reachable from the primary nav and renders the real journey, not "Simulate primary action"', async ({
    page,
  }) => {
    await page.goto('/?as=staff');
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Document Verify' }).click();
    await expect(page).toHaveURL(/\/document-verify$/);
    await expect(page.getByRole('heading', { name: 'Document Verify', level: 1 })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Simulate primary action' })).toHaveCount(0);
  });
});

test.describe('BIO-UX-009-AC01 every named interactive area is present and runnable', () => {
  test('capture, MRZ, barcode, OCR, document identification, NFC and authenticity sections all render', async ({
    page,
  }) => {
    await page.goto('/document-verify');
    for (const testId of [
      'step-capture',
      'step-mrz',
      'step-barcode',
      'step-ocr',
      'step-document-identification',
      'step-nfc',
      'step-authenticity',
      'step-selfie-comparison',
    ]) {
      await expect(page.getByTestId(testId)).toBeVisible();
    }
  });

  test('every later step is disabled until capture succeeds, then unlocks (no dead actions, AC05)', async ({
    page,
  }) => {
    await page.goto('/document-verify');
    const mrzButton = page.getByTestId('step-mrz').getByRole('button');
    await expect(mrzButton).toBeDisabled();

    await captureSuccessfully(page);

    await expect(mrzButton).toBeEnabled();
  });

  test('a failed capture attempt keeps the rest of the journey gated (state genuinely matters, not just "any click")', async ({
    page,
  }) => {
    await page.goto('/document-verify');
    // Capture's own deterministic first call is a `failed` scenario (see this file's header comment).
    await page.getByTestId('step-capture').getByRole('button').click();
    await expect(page.getByTestId('step-capture').getByRole('status')).toContainText('failed');
    await expect(page.getByTestId('step-mrz').getByRole('button')).toBeDisabled();
  });
});

test.describe('BIO-UX-009-AC02 evidence comparison distinguishes states clearly', () => {
  test('the comparison table starts every area at "Not checked" and updates after a step runs', async ({ page }) => {
    await page.goto('/document-verify');
    const table = page.getByTestId('evidence-comparison-table');
    await expect(table.getByText('Not checked').first()).toBeVisible();

    await page.getByTestId('step-capture').getByRole('button').click();
    await expect(page.getByTestId('step-capture').getByRole('status')).toBeVisible();

    // The table row for Capture / Upload no longer reads "Not checked", win or lose.
    const captureRow = table.locator('tr', { hasText: 'Capture / Upload' });
    await expect(captureRow).not.toContainText('Not checked');
  });
});

test.describe('BIO-UX-009-AC03 low-confidence / conflicting-source scenarios route to human review', () => {
  test('an inconclusive OCR result adds an entry to the human review queue', async ({ page }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);

    await clickUntilBadge(page, 'step-ocr', 'Inconclusive');
    await expect(page.getByTestId('step-ocr').getByRole('status')).toContainText('confidence');

    const queue = page.getByTestId('human-review-queue');
    await expect(queue).toContainText('OCR');
    await expect(queue.getByRole('button', { name: 'Resolve' })).toBeVisible();
  });

  test('resolving a queue entry marks it resolved instead of silently disappearing', async ({ page }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);
    await clickUntilBadge(page, 'step-ocr', 'Inconclusive');

    const queue = page.getByTestId('human-review-queue');
    await queue.getByRole('button', { name: 'Resolve' }).click();
    await expect(queue.getByRole('button', { name: 'Resolved' })).toBeVisible();
  });

  test('a retry that resolves the underlying issue drops the earlier queue entry for that step', async ({ page }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);
    await clickUntilBadge(page, 'step-ocr', 'Inconclusive');
    await expect(page.getByTestId('human-review-queue')).toContainText('OCR');

    await clickUntilBadge(page, 'step-ocr', 'Read');
    await expect(page.getByTestId('human-review-queue')).not.toContainText('OCR');
  });

  test('the "Route to human review" secondary action manually escalates once capture has succeeded', async ({
    page,
  }) => {
    await page.goto('/document-verify');
    const routeButton = page.getByRole('button', { name: 'Route to human review' });
    await expect(routeButton).toBeDisabled();

    await captureSuccessfully(page);
    await expect(routeButton).toBeEnabled();
    await routeButton.click();

    await expect(page.getByTestId('human-review-queue')).toContainText('Manually escalated');
    await expect(page.getByTestId('review-queue-count')).toBeVisible();
  });
});

test.describe('BIO-UX-009-AC04 portrait vs. live selfie comparison is simulated, never real biometric data', () => {
  test('the selfie section states plainly that no camera or real biometric data is used', async ({ page }) => {
    await page.goto('/document-verify');
    const selfieSection = page.getByTestId('step-selfie-comparison');
    await expect(selfieSection).toContainText('No camera or real biometric data is used');
  });

  test('the selfie comparison action runs and produces a visible result once capture has succeeded', async ({
    page,
  }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);

    const selfieSection = page.getByTestId('step-selfie-comparison');
    await selfieSection.getByRole('button', { name: /selfie/i }).click();
    await expect(selfieSection.getByRole('status')).toBeVisible();
  });
});

test.describe('BIO-UX-009 accessibility', () => {
  test('has no axe violations on initial load', async ({ page }) => {
    await page.goto('/document-verify');
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });

  test('has no axe violations once success, warning and error badges are all visible (contrast check)', async ({
    page,
  }) => {
    // Damilola's BIO-UX-009 review flagged a real WCAG AA contrast risk in the frozen
    // --biolog-color-success/-warning tokens on their own -bg tint at this badge's size/weight -
    // the initial-load scan above never exercises those tones (every badge starts 'not-checked').
    // Drives the page into a state with success (capture), warning (OCR inconclusive) and error
    // (MRZ failed) badges all visible simultaneously, then re-scans.
    await page.goto('/document-verify');
    await captureSuccessfully(page);
    await clickUntilBadge(page, 'step-ocr', 'Inconclusive');
    await clickUntilBadge(page, 'step-mrz', 'Failed');

    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
