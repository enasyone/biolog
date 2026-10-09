// Developer tests for BIO-CRD-004. SYNTHETIC: no real card designs or signatures.
//
// Every locator is scoped under [data-testid="bio-crd-004-app"] because this tool renders on the
// same /card-designer route as BIO-CRD-002's and BIO-CRD-003's own canvases (see
// web/app/card-designer/page.tsx), which expose overlapping control labels (layer/rotate
// buttons). Scoping avoids Playwright strict-mode violations from matching another tool's
// controls - the same lesson BIO-CRD-003's own spec already applied.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

function app(page: Page) {
  return page.getByTestId('bio-crd-004-app');
}

test.describe('BIO-CRD-004 the card objects tool is reachable on the existing /card-designer route', () => {
  test('renders alongside the BIO-CRD-002/003 canvases, not a separate route', async ({ page }) => {
    await page.goto('/card-designer');
    await expect(page.getByRole('heading', { name: 'Card Designer' })).toBeVisible();
    await expect(app(page).getByRole('heading', { name: 'Card Objects: Barcode, Signature & Logo' })).toBeVisible();
  });
});

test.describe('BIO-CRD-004-AC01 default ID-card barcode profile is alphanumeric', () => {
  test('the default "+ Barcode" button creates an alphanumeric-profile barcode', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Barcode (default: alphanumeric)' }).click();
    await expect(app(page).getByLabel('Barcode profile')).toHaveValue('alphanumeric');
  });
});

test.describe('BIO-CRD-004-AC02 QR/PDF417/Data Matrix/Aztec are optional, never default', () => {
  test('every optional profile is reachable only via its own explicit button', async ({ page }) => {
    await page.goto('/card-designer');
    await expect(app(page).getByRole('button', { name: '+ QR barcode' })).toBeVisible();
    await expect(app(page).getByRole('button', { name: '+ PDF417 barcode' })).toBeVisible();
    await expect(app(page).getByRole('button', { name: '+ Data Matrix barcode' })).toBeVisible();
    await expect(app(page).getByRole('button', { name: '+ Aztec barcode' })).toBeVisible();

    await app(page).getByRole('button', { name: '+ QR barcode' }).click();
    await expect(app(page).getByLabel('Barcode profile')).toHaveValue('qr');
  });

  test('the profile can be changed after creation, including back to the default', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Aztec barcode' }).click();
    await app(page).getByLabel('Barcode profile').selectOption('alphanumeric');
    await expect(app(page).getByLabel('Barcode profile')).toHaveValue('alphanumeric');
  });
});

test.describe('BIO-CRD-004-AC03 barcode objects validate quiet zone/size/contrast', () => {
  test('a default barcode reports the validation status without throwing', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Barcode (default: alphanumeric)' }).click();
    await expect(app(page).getByTestId('barcode-validation-ok').or(app(page).getByTestId('barcode-validation-violations'))).toBeVisible();
  });

  test('setting equal foreground/background colours surfaces a contrast violation', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Barcode (default: alphanumeric)' }).click();

    await app(page).getByLabel('Foreground colour').fill('#808080');
    await app(page).getByLabel('Background colour').fill('#808080');

    const violations = app(page).getByTestId('barcode-validation-violations');
    await expect(violations).toBeVisible();
    await expect(violations).toContainText('contrast_ratio');
  });

  test('shrinking the quiet zone below the minimum surfaces a violation, and restoring it clears the violation', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Barcode (default: alphanumeric)' }).click();

    await app(page).getByLabel('Quiet zone (mm)').fill('0');
    await expect(app(page).getByTestId('barcode-validation-violations')).toContainText('quiet_zone_mm');

    await app(page).getByLabel('Quiet zone (mm)').fill('3');
    await expect(app(page).getByTestId('barcode-validation-ok')).toBeVisible();
  });
});

test.describe('BIO-CRD-004-AC04 signature assets are versioned', () => {
  test('adding a signature pins the lineage id and version chosen at creation', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByLabel('New signature lineage id').fill('synthetic-lineage-42');
    await app(page).getByLabel('New signature version').fill('3');
    await app(page).getByRole('button', { name: '+ Signature' }).click();

    await expect(app(page).getByLabel('Lineage id', { exact: true })).toHaveValue('synthetic-lineage-42');
    await expect(app(page).getByLabel('Version', { exact: true })).toHaveValue('3');
  });

  test('the version can be repointed to a different already-known version without changing the lineage id', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByLabel('New signature lineage id').fill('synthetic-lineage-7');
    await app(page).getByLabel('New signature version').fill('1');
    await app(page).getByRole('button', { name: '+ Signature' }).click();

    await app(page).getByLabel('Version', { exact: true }).fill('5');
    await expect(app(page).getByLabel('Version', { exact: true })).toHaveValue('5');
    await expect(app(page).getByLabel('Lineage id', { exact: true })).toHaveValue('synthetic-lineage-7');
  });
});

test.describe('BIO-CRD-004-AC05 conditional visibility supports profile/card class rules', () => {
  test('restricting an object to one card class hides it when previewing a different class', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Barcode (default: alphanumeric)' }).click();

    const canvasElement = app(page).locator('[data-selected="true"]');
    await expect(canvasElement).not.toHaveAttribute('data-hidden', 'true');

    await app(page).getByLabel('Restrict to specific card classes').check();
    // Uncheck the default-checked first card class and check "visitor" only.
    await app(page).getByRole('checkbox', { name: 'staff' }).uncheck();
    await app(page).getByRole('checkbox', { name: 'visitor' }).check();

    // Preview is "staff" by default - the object should now render hidden.
    await expect(canvasElement).toHaveAttribute('data-hidden', 'true');

    await app(page).getByLabel('Preview card class').selectOption('visitor');
    await expect(canvasElement).not.toHaveAttribute('data-hidden', 'true');
  });

  test('restricting an object to one barcode profile hides it under a different preview profile', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Logo' }).click();

    const canvasElement = app(page).locator('[data-selected="true"]');
    await app(page).getByLabel('Restrict to specific symbologies').check();
    await app(page).getByRole('checkbox', { name: 'QR' }).check();
    await app(page).getByRole('checkbox', { name: 'Alphanumeric' }).uncheck();

    await expect(canvasElement).toHaveAttribute('data-hidden', 'true'); // preview profile defaults to alphanumeric
    await app(page).getByLabel('Preview symbology').selectOption('qr');
    await expect(canvasElement).not.toHaveAttribute('data-hidden', 'true');
  });

  test('an object with no restriction is always visible regardless of preview context', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Logo' }).click();
    const canvasElement = app(page).locator('[data-selected="true"]');

    await app(page).getByLabel('Preview card class').selectOption('visitor');
    await expect(canvasElement).not.toHaveAttribute('data-hidden', 'true');
    await app(page).getByLabel('Preview symbology').selectOption('aztec');
    await expect(canvasElement).not.toHaveAttribute('data-hidden', 'true');
  });
});

test.describe('BIO-CRD-004 accessibility baseline', () => {
  test('has no axe violations with a barcode, a signature and a logo added, and visibility editor open', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Barcode (default: alphanumeric)' }).click();
    await app(page).getByRole('button', { name: '+ Signature' }).click();
    await app(page).getByRole('button', { name: '+ Logo' }).click();
    await app(page).getByLabel('Restrict to specific card classes').check();

    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
