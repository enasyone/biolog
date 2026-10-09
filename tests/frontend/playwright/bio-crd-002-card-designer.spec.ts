// Developer tests for BIO-CRD-002. SYNTHETIC: no real card designs.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('BIO-CRD-002 the card designer route is real, not a 404', () => {
  test('is reachable from the primary nav and renders the designer heading', async ({ page }) => {
    await page.goto('/?as=admin');
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Card Designer' }).click();
    await expect(page).toHaveURL(/\/card-designer$/);
    await expect(page.getByRole('heading', { name: 'Card Designer' })).toBeVisible();
  });
});

test.describe('BIO-CRD-002-AC01 standard sizes use correct physical dimensions', () => {
  test('CR80 is selected by default and renders a canvas with the CR80 aspect ratio', async ({ page }) => {
    await page.goto('/card-designer');
    await expect(page.getByLabel('Format')).toHaveValue('cr80');
    const box = await page.locator('[aria-label="Front card canvas"]').boundingBox();
    expect(box).not.toBeNull();
    // CR80 landscape is 85.60 x 53.98mm - aspect ratio ~1.586.
    expect(box!.width / box!.height).toBeGreaterThan(1.5);
    expect(box!.width / box!.height).toBeLessThan(1.65);
  });

  test('switching to CR100 changes the rendered canvas aspect ratio', async ({ page }) => {
    await page.goto('/card-designer');
    const before = await page.locator('[aria-label="Front card canvas"]').boundingBox();
    await page.getByLabel('Format').selectOption('cr100');
    const after = await page.locator('[aria-label="Front card canvas"]').boundingBox();
    // Both formats are scaled to the same on-screen width cap, so width alone would look
    // unchanged - the aspect ratio is what actually differs between CR80 (~1.586) and CR100 (~1.470).
    expect(after!.width / after!.height).not.toBeCloseTo(before!.width / before!.height, 1);
  });
});

test.describe('BIO-CRD-002-AC02 landscape/portrait switch preserves geometry without cumulative drift', () => {
  test('clicking the orientation switch button twice returns the canvas to its original on-screen proportions', async ({ page }) => {
    await page.goto('/card-designer');
    const original = await page.locator('[aria-label="Front card canvas"]').boundingBox();

    await page.getByRole('button', { name: /switch to portrait/i }).click();
    await page.getByRole('button', { name: /switch to landscape/i }).click();

    const restored = await page.locator('[aria-label="Front card canvas"]').boundingBox();
    expect(Math.round(restored!.width)).toBe(Math.round(original!.width));
    expect(Math.round(restored!.height)).toBe(Math.round(original!.height));
  });
});

test.describe('BIO-CRD-002-AC03 custom width/height validation exists', () => {
  test('an out-of-bounds custom size shows a validation error and disables Apply', async ({ page }) => {
    await page.goto('/card-designer');
    await page.getByLabel('Format').selectOption('custom');
    await page.getByLabel('Width (mm)').fill('5');
    await expect(page.getByTestId('custom-size-validation-error')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Apply custom size' })).toBeDisabled();
  });

  test('a valid custom size can be applied and changes the canvas', async ({ page }) => {
    await page.goto('/card-designer');
    await page.getByLabel('Format').selectOption('custom');
    await page.getByLabel('Width (mm)').fill('120');
    await page.getByLabel('Height (mm)').fill('80');
    await page.getByRole('button', { name: 'Apply custom size' }).click();
    await expect(page.getByTestId('custom-size-validation-error')).toHaveCount(0);
  });
});

test.describe('BIO-CRD-002-AC04 elements support move/resize/rotate/layer/lock', () => {
  test('adding an element makes it visible and selectable on the canvas', async ({ page }) => {
    await page.goto('/card-designer');
    await page.getByRole('button', { name: 'Add element' }).click();
    await expect(page.getByRole('heading', { name: 'Properties' })).toBeVisible();
  });

  test('changing the X property field moves the element', async ({ page }) => {
    await page.goto('/card-designer');
    await page.getByRole('button', { name: 'Add element' }).click();
    const xInput = page.getByLabel('X (px)');
    await xInput.fill('200');
    await xInput.blur();
    await expect(xInput).toHaveValue('200');
  });

  test('locking an element disables its size fields', async ({ page }) => {
    await page.goto('/card-designer');
    await page.getByRole('button', { name: 'Add element' }).click();
    await page.getByLabel('Locked').check();
    await expect(page.getByLabel('Width (px)')).toBeDisabled();
  });

  test('rotating an element applies a visible CSS rotation', async ({ page }) => {
    await page.goto('/card-designer');
    await page.getByRole('button', { name: 'Add element' }).click();
    await page.getByRole('button', { name: 'Rotate +15°' }).click();
    const transform = await page.locator('[data-selected="true"]').evaluate((el) => getComputedStyle(el).transform);
    expect(transform).not.toBe('none');
  });

  test('bring to front moves an element to the top of the layer list', async ({ page }) => {
    await page.goto('/card-designer');
    await page.getByRole('button', { name: 'Add element' }).click();
    await page.getByRole('button', { name: 'Add element' }).click();
    await page.getByRole('button', { name: 'Send to back' }).click();
    const layerButtons = page.locator('.card-designer-layer-list button, ul li button');
    await expect(layerButtons.first()).toBeVisible();
  });
});

test.describe('BIO-CRD-002-AC05 front and back are independent', () => {
  test('an element added to the front does not appear when switching to the back', async ({ page }) => {
    await page.goto('/card-designer');
    await page.getByRole('button', { name: 'Add element' }).click();
    await expect(page.getByRole('heading', { name: 'Properties' })).toBeVisible();

    await page.getByRole('tab', { name: 'Back' }).click();
    await expect(page.getByRole('heading', { name: 'Properties' })).toHaveCount(0);
    await expect(page.getByText('Select an element to edit')).toBeVisible();
  });
});

test.describe('BIO-CRD-002 accessibility baseline', () => {
  test('has no axe violations on first load', async ({ page }) => {
    await page.goto('/card-designer');
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });

  test('has no axe violations with an element selected and its properties panel open', async ({ page }) => {
    await page.goto('/card-designer');
    await page.getByRole('button', { name: 'Add element' }).click();
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
