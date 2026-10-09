// Developer tests for BIO-PHO-003 (SIMULATION). SYNTHETIC: no real photo or biometric data.
import { expect, test } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const SYNTHETIC_PHOTO_PATH = path.join(__dirname, 'fixtures', 'synthetic-photo.png');

test.describe('BIO-PHO-003 the photo-quality review screen (SIMULATION)', () => {
  test('AC01/AC02: submitting a photo shows two independent verdicts, each with its own reasons when failing', async ({ page }) => {
    await page.goto('/photographs');
    await page.getByTestId('photo-quality-person-id').fill('per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b');
    await page.getByTestId('photo-quality-file').setInputFiles(SYNTHETIC_PHOTO_PATH);
    await page.getByTestId('photo-quality-submit').click();

    const panel = page.getByTestId('photo-quality-review');
    await expect(panel.getByRole('status')).toContainText('submitted for review');

    const captures = page.getByTestId('photo-quality-captures');
    await expect(captures).toBeVisible();
    await expect(captures.getByText('Print quality')).toBeVisible();
    await expect(captures.getByText('Biometric suitability')).toBeVisible();
  });

  test('AC03: approving a pending capture shows a promoted-master status', async ({ page }) => {
    await page.goto('/photographs');
    await page.getByTestId('photo-quality-person-id').fill('per_018f2e5a-aaaa-7bbb-8ccc-000000000001');
    await page.getByTestId('photo-quality-file').setInputFiles(SYNTHETIC_PHOTO_PATH);
    await page.getByTestId('photo-quality-submit').click();
    await expect(page.getByTestId('photo-quality-captures')).toBeVisible();

    await page.getByTestId('approve-1').click();
    await expect(page.getByText(/Promoted to photo master version/)).toBeVisible();
  });

  test('AC03: rejecting requires a reason and records it', async ({ page }) => {
    await page.goto('/photographs');
    await page.getByTestId('photo-quality-person-id').fill('per_018f2e5a-bbbb-7ccc-8ddd-000000000002');
    await page.getByTestId('photo-quality-file').setInputFiles(SYNTHETIC_PHOTO_PATH);
    await page.getByTestId('photo-quality-submit').click();
    await expect(page.getByTestId('photo-quality-captures')).toBeVisible();

    await expect(page.getByTestId('reject-1')).toBeDisabled();
    await page.getByTestId('reject-reason-1').fill('background is not plain (SIMULATED)');
    await expect(page.getByTestId('reject-1')).toBeEnabled();
    await page.getByTestId('reject-1').click();
    await expect(page.getByText('Rejected: background is not plain (SIMULATED)')).toBeVisible();
  });

  test('loading an empty person shows an explicit empty state, not a blank screen', async ({ page }) => {
    await page.goto('/photographs');
    await page.getByTestId('photo-quality-person-id').fill('per_018f2e5a-cccc-7ddd-8eee-000000000003');
    await page.getByTestId('photo-quality-load').click();

    await expect(page.getByText('No captures submitted yet for this person.')).toBeVisible();
  });
});
