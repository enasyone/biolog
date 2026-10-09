// Developer tests for BIO-CRD-003. SYNTHETIC: no real card designs.
//
// Every locator is scoped under [data-testid="bio-crd-003-app"] because this tool renders on the
// same /card-designer route as BIO-CRD-002's own canvas (see web/app/card-designer/page.tsx),
// which exposes overlapping control labels (layer/rotate buttons). Scoping avoids Playwright
// strict-mode violations from matching both tools' controls.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

function app(page: Page) {
  return page.getByTestId('bio-crd-003-app');
}

test.describe('BIO-CRD-003 the typography tool is reachable on the existing /card-designer route', () => {
  test('renders alongside the BIO-CRD-002 canvas, not a separate route', async ({ page }) => {
    await page.goto('/card-designer');
    await expect(page.getByRole('heading', { name: 'Card Designer' })).toBeVisible();
    await expect(app(page).getByRole('heading', { name: 'Card Typography, Name Fields & Watermarks' })).toBeVisible();
  });
});

test.describe('BIO-CRD-003-AC01 each name field binds independently', () => {
  test('adding two different name fields shows two independent layer entries', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ First Name' }).click();
    await app(page).getByRole('button', { name: '+ Last Name' }).click();
    await expect(app(page).getByRole('listitem').filter({ hasText: 'First Name' })).toBeVisible();
    await expect(app(page).getByRole('listitem').filter({ hasText: 'Last Name' })).toBeVisible();
  });

  test("changing one field's binding does not change a previously added field", async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ First Name' }).click();
    await app(page).getByRole('button', { name: '+ Last Name' }).click();
    // The most recently added field (Last Name) is selected; rebind it to Title.
    await app(page).getByLabel('Data binding').selectOption('title');
    await expect(app(page).getByRole('listitem').filter({ hasText: 'First Name' })).toBeVisible();
  });
});

test.describe('BIO-CRD-003-AC02 typography is editable per field', () => {
  test('toggling bold on the selected field does not affect font family', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Full Name' }).click();
    await app(page).getByLabel('Bold').check();
    await expect(app(page).getByLabel('Bold')).toBeChecked();
    await expect(app(page).getByLabel('Font family')).toHaveValue('Arial');
  });

  test('changing font size is reflected in the field', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Display Name' }).click();
    await app(page).getByLabel('Font size (pt)').fill('28');
    await expect(app(page).getByLabel('Font size (pt)')).toHaveValue('28');
  });

  test('opacity accepts the full 0-100 range', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Title' }).click();
    const opacity = app(page).getByLabel('Opacity (%)');
    await opacity.fill('50');
    await expect(opacity).toHaveValue('50');
  });
});

test.describe('BIO-CRD-003-AC03 copy style does not copy data binding', () => {
  test("applying a copied style changes typography but keeps the target field's own binding", async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ First Name' }).click();
    await app(page).getByLabel('Bold').check();

    await app(page).getByRole('button', { name: '+ Last Name' }).click();
    await expect(app(page).getByLabel('Data binding')).toHaveValue('lastName');
    await expect(app(page).getByLabel('Bold')).not.toBeChecked();

    await app(page).getByLabel('Copy style from').selectOption({ label: 'First Name' });
    await app(page).getByRole('button', { name: 'Apply style (binding unaffected)' }).click();

    await expect(app(page).getByLabel('Bold')).toBeChecked();
    await expect(app(page).getByLabel('Data binding')).toHaveValue('lastName'); // unchanged by the copy
  });
});

test.describe('BIO-CRD-003-AC04 watermarks support opacity, rotation and layer control', () => {
  test('adding a custom watermark and changing its opacity', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Custom watermark' }).click();
    const opacity = app(page).getByLabel('Opacity (%)');
    await opacity.fill('60');
    await expect(opacity).toHaveValue('60');
  });

  test('rotating a watermark applies a visible transform', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Custom watermark' }).click();
    const selected = app(page).locator('[data-selected="true"]');
    const before = await selected.evaluate((el) => getComputedStyle(el).transform);
    await app(page).getByRole('button', { name: 'Rotate +15°' }).click();
    const after = await selected.evaluate((el) => getComputedStyle(el).transform);
    expect(after).not.toBe(before);
  });

  test('layer controls reorder watermarks', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ STAFF watermark' }).click();
    await app(page).getByRole('button', { name: '+ STUDENT watermark' }).click();

    const layerButtons = app(page).getByRole('list').getByRole('button');
    // STUDENT was added last, so it has the highest zIndex and sorts first (list is zIndex desc).
    await expect(layerButtons.first()).toHaveText(/STUDENT/);

    await app(page).getByRole('button', { name: 'Send to back' }).click();
    await expect(layerButtons.first()).toHaveText(/STAFF/);
  });
});

test.describe('BIO-CRD-003-AC05 STAFF/STUDENT/VISITOR starter watermark behaviors are available', () => {
  test('all three preset buttons are present and add a correctly-labelled watermark', async ({ page }) => {
    await page.goto('/card-designer');
    await expect(app(page).getByRole('button', { name: '+ STAFF watermark' })).toBeVisible();
    await expect(app(page).getByRole('button', { name: '+ STUDENT watermark' })).toBeVisible();
    await expect(app(page).getByRole('button', { name: '+ VISITOR watermark' })).toBeVisible();

    await app(page).getByRole('button', { name: '+ VISITOR watermark' }).click();
    await expect(app(page).getByLabel('Text')).toHaveValue('VISITOR');
  });
});

test.describe('BIO-CRD-003 accessibility baseline', () => {
  test('has no axe violations with a name field and a watermark selected', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ First Name' }).click();
    await app(page).getByRole('button', { name: '+ Custom watermark' }).click();
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
