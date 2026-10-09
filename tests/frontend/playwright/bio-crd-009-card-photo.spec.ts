// Developer tests for BIO-CRD-009. SYNTHETIC: no real cardholder photos.
//
// Every locator is scoped under [data-testid="bio-crd-009-app"] because this tool renders on the
// same /card-designer route as BIO-CRD-002/003/004/008's own canvases (see
// web/app/card-designer/page.tsx), which expose overlapping control labels (layer/rotate
// buttons) - the same scoping precedent BIO-CRD-003/004's own specs already established.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

function app(page: Page) {
  return page.getByTestId('bio-crd-009-app');
}

test.describe('BIO-CRD-009-AC05 the photo tool is reachable on the existing /card-designer route', () => {
  test('renders alongside the other card-designer canvases, not a separate route', async ({ page }) => {
    await page.goto('/card-designer');
    await expect(page.getByRole('heading', { name: 'Card Designer' })).toBeVisible();
    await expect(app(page).getByRole('heading', { name: 'Card Photo' })).toBeVisible();
  });

  test('the "add photo" control is real - it places a real, visible photo element, not a dead button', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    await expect(app(page).locator('[data-selected="true"]')).toBeVisible();
    await expect(app(page).locator('[data-selected="true"]')).toHaveText(/cardholder-photo-1/);
  });
});

test.describe('BIO-CRD-009-AC01 place, move, resize, rotate and layer a photo', () => {
  test('a placed photo can be moved via the arrow-key nudge', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    const selected = app(page).locator('[data-selected="true"]');
    const before = await selected.evaluate((el) => (el as HTMLElement).style.left);
    await app(page).getByLabel('Width (px)').click(); // focuses inside the properties panel, whose onKeyDown handles the nudge
    await page.keyboard.press('ArrowRight');
    const after = await selected.evaluate((el) => (el as HTMLElement).style.left);
    expect(after).not.toBe(before);
  });

  test('resizing via the width field changes the rendered element', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    await app(page).getByLabel('Width (px)').fill('150');
    await expect(app(page).getByLabel('Width (px)')).toHaveValue('150');
    await expect(app(page).locator('[data-selected="true"]')).toHaveCSS('width', '150px');
  });

  test('rotating a photo applies a visible transform', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    const selected = app(page).locator('[data-selected="true"]');
    const before = await selected.evaluate((el) => getComputedStyle(el).transform);
    await app(page).getByRole('button', { name: 'Rotate +15°' }).click();
    const after = await selected.evaluate((el) => getComputedStyle(el).transform);
    expect(after).not.toBe(before);
  });

  test('layer controls reorder photos', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByLabel('New photo reference').fill('photo-a');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    await app(page).getByLabel('New photo reference').fill('photo-b');
    await app(page).getByRole('button', { name: '+ Photo' }).click();

    const layerButtons = app(page).getByRole('list').getByRole('button');
    // photo-b was added last, so it has the highest zIndex and sorts first (list is zIndex desc).
    await expect(layerButtons.first()).toHaveText(/photo-b/);

    await app(page).getByRole('button', { name: 'Send to back' }).click();
    await expect(layerButtons.first()).toHaveText(/photo-a/);
  });

  test('a locked photo refuses resize - the width field is disabled', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    await app(page).getByLabel('Locked').check();
    await expect(app(page).getByLabel('Width (px)')).toBeDisabled();
  });

  test('removing a photo takes it off the canvas', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    await app(page).getByRole('button', { name: 'Remove photo' }).click();
    await expect(app(page).locator('[data-selected="true"]')).toHaveCount(0);
  });
});

test.describe('BIO-CRD-009-AC03 the photo element is addable/removable on both front and back independently', () => {
  test('a photo added on front does not appear when switching to back', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByLabel('New photo reference').fill('front-only');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    await expect(app(page).getByRole('listitem').filter({ hasText: 'front-only' })).toBeVisible();

    await app(page).getByRole('tab', { name: 'Back' }).click();
    await expect(app(page).getByRole('listitem').filter({ hasText: 'front-only' })).toHaveCount(0);
  });

  test('adding a photo on back does not affect the front side, and switching back shows it again', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByLabel('New photo reference').fill('front-photo');
    await app(page).getByRole('button', { name: '+ Photo' }).click();

    await app(page).getByRole('tab', { name: 'Back' }).click();
    await app(page).getByLabel('New photo reference').fill('back-photo');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    await expect(app(page).getByRole('listitem').filter({ hasText: 'back-photo' })).toBeVisible();
    await expect(app(page).getByRole('listitem').filter({ hasText: 'front-photo' })).toHaveCount(0);

    await app(page).getByRole('tab', { name: 'Front' }).click();
    await expect(app(page).getByRole('listitem').filter({ hasText: 'front-photo' })).toBeVisible();
    await expect(app(page).getByRole('listitem').filter({ hasText: 'back-photo' })).toHaveCount(0);
  });

  test('removing a photo on front does not remove the back photo', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Photo' }).click(); // front
    await app(page).getByRole('tab', { name: 'Back' }).click();
    await app(page).getByRole('button', { name: '+ Photo' }).click(); // back
    await app(page).getByRole('button', { name: 'Remove photo' }).click(); // removes the selected back photo

    await expect(app(page).getByRole('listitem')).toHaveCount(0);
    await app(page).getByRole('tab', { name: 'Front' }).click();
    await expect(app(page).getByRole('listitem')).toHaveCount(1);
  });
});

test.describe('BIO-CRD-009-AC02 content is an opaque reference string only', () => {
  test('the reference text is stored and shown verbatim, never resolved into an image', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByLabel('New photo reference').fill('asset://cardholder/12345');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    await expect(app(page).locator('[data-selected="true"]')).toHaveText(/asset:\/\/cardholder\/12345/);
    // No <img> is ever rendered - content stays an opaque string, matching Logo/Watermark's own boundary.
    await expect(app(page).locator('img')).toHaveCount(0);
  });

  test('the "add photo" control is disabled for an empty reference (invalid input, no silent no-op element)', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByLabel('New photo reference').fill('   ');
    await expect(app(page).getByRole('button', { name: '+ Photo' })).toBeDisabled();
  });
});

test.describe('BIO-CRD-009 regression: existing canvases are unaffected', () => {
  test('BIO-CRD-002/003/004 canvases still render and accept elements alongside the new photo tool', async ({ page }) => {
    await page.goto('/card-designer');
    await expect(page.getByTestId('bio-crd-003-app').getByRole('heading', { name: 'Card Typography, Name Fields & Watermarks' })).toBeVisible();
    await expect(page.getByTestId('bio-crd-004-app').getByRole('heading', { name: 'Card Objects: Barcode, Signature & Logo' })).toBeVisible();
    await page.getByTestId('bio-crd-004-app').getByRole('button', { name: '+ Logo' }).click();
    await expect(page.getByTestId('bio-crd-004-app').locator('[data-selected="true"]')).toBeVisible();
  });
});

test.describe('BIO-CRD-009 accessibility baseline', () => {
  test('has no axe violations with a photo placed and selected', async ({ page }) => {
    await page.goto('/card-designer');
    await app(page).getByRole('button', { name: '+ Photo' }).click();
    const results = await new AxeBuilder({ page }).include('[data-testid="bio-crd-009-app"]').analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
