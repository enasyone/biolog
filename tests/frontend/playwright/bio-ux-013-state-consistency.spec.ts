// Developer tests for BIO-UX-013-AC04. SYNTHETIC: no real data.
//
// Two real inconsistencies found by reading the CSS (not assumed away), both fixed in
// globals.css: `.biolog-identity-status--empty` (BIO-UX-006) was missing the `font-style:
// italic` every other empty-state pattern in the app already has, and
// `.biolog-identity-status--offline` was coloured identically to `--error` instead of using the
// dedicated `--biolog-color-offline` token `.biolog-placeholder-status--offline` (BIO-UX-005/011)
// already gets right. Fixed at the shared globals.css level (this story's own ownership), not by
// touching BIO-UX-006's component markup.
import { expect, test } from '@playwright/test';

test.describe('BIO-UX-013-AC04 empty-state visual consistency across domains', () => {
  test('People\'s empty-filter-result state renders italic, matching the dashboard/operational empty-state pattern', async ({
    page,
  }) => {
    await page.goto('/people?as=admin');
    await page.getByLabel('Filter by name').fill('zzz-no-such-person-zzz');
    const empty = page.getByText(/No people match this tenant\/filter/);
    await expect(empty).toBeVisible();
    const fontStyle = await empty.evaluate((el) => getComputedStyle(el).fontStyle);
    expect(fontStyle).toBe('italic');
  });

  test('the dashboard\'s and an operational page\'s own empty states are both italic (the pattern this fix now matches)', async ({
    page,
  }) => {
    for (const { route, locator } of [
      { route: '/', locator: '.biolog-empty-state' },
      { route: '/access-control', locator: '.biolog-op-empty' },
    ]) {
      await page.goto(route);
      const el = page.locator(locator).first();
      // Not every load has zero data, but if the element is present, it must be italic.
      if ((await el.count()) > 0) {
        const fontStyle = await el.evaluate((node) => getComputedStyle(node).fontStyle);
        expect(fontStyle, `${route} ${locator}`).toBe('italic');
      }
    }
  });
});

test.describe('BIO-UX-013-AC04 offline-state visual consistency across domains', () => {
  test('.biolog-identity-status--offline now resolves to the same colour as .biolog-placeholder-status--offline, not --error', async ({
    page,
  }) => {
    await page.goto('/');
    const colours = await page.evaluate(() => {
      const probe = (className: string) => {
        const el = document.createElement('p');
        el.className = className;
        document.body.appendChild(el);
        const colour = getComputedStyle(el).color;
        el.remove();
        return colour;
      };
      return {
        identityOffline: probe('biolog-identity-status biolog-identity-status--offline'),
        placeholderOffline: probe('biolog-placeholder-status biolog-placeholder-status--offline'),
        identityError: probe('biolog-identity-status biolog-identity-status--error'),
      };
    });
    expect(colours.identityOffline).toBe(colours.placeholderOffline);
    expect(colours.identityOffline).not.toBe(colours.identityError);
  });
});
