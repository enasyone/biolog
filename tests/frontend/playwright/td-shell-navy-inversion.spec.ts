// Developer tests for TD-SHELL-NAVY-INVERSION. SYNTHETIC: no real data.
//
// The human compared the live app to the approved reference screenshots
// (reference/ui/INITIAL_PROTOTYPE_REFERENCE.png,
// reference/ui/APPROVED_SYSTEM_SETTINGS_DIRECTION.png) and found the shell's
// navy/white chrome backwards: the top bar was navy and spanned the FULL
// width above both the sidebar and the content, while the sidebar was white
// and confined below it. The reference has a full-height NAVY sidebar (own
// column, brand at its top) and a WHITE top bar confined to the content
// column only. Fixed in web/app/globals.css (.biolog-shell,
// .biolog-header, .biolog-primary-nav and related child rules) and
// web/components/app-shell.tsx (nav now precedes header in DOM order, and
// gained its own brand block at its top — see docs/features/td-shell-navy-
// inversion/README.md for the full writeup).
import { expect, test } from '@playwright/test';

async function hexToRgb(page: import('@playwright/test').Page, cssVarValue: string): Promise<string> {
  return page.evaluate((hex) => {
    const div = document.createElement('div');
    div.style.color = hex;
    document.body.appendChild(div);
    const rgb = getComputedStyle(div).color;
    div.remove();
    return rgb;
  }, cssVarValue);
}

test.describe('TD-SHELL-NAVY-INVERSION colours match the approved reference, not swapped', () => {
  test('at desktop, the sidebar is navy and the header is the light surface colour', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/?as=admin');

    const { navyHex, surfaceHex } = await page.evaluate(() => {
      const styles = getComputedStyle(document.documentElement);
      return {
        navyHex: styles.getPropertyValue('--biolog-color-brand-navy').trim(),
        surfaceHex: styles.getPropertyValue('--biolog-color-surface').trim(),
      };
    });
    const navyRgb = await hexToRgb(page, navyHex);
    const surfaceRgb = await hexToRgb(page, surfaceHex);

    const navBg = await page
      .getByRole('navigation', { name: 'Primary' })
      .evaluate((el) => getComputedStyle(el).backgroundColor);
    const headerBg = await page.getByRole('banner').evaluate((el) => getComputedStyle(el).backgroundColor);

    expect(navBg).toBe(navyRgb);
    expect(headerBg).toBe(surfaceRgb);
  });
});

test.describe('TD-SHELL-NAVY-INVERSION structure matches the reference, not just colour', () => {
  test('at desktop, the sidebar spans the full shell height and the header is confined to the content column', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/?as=admin');

    const navBox = await page.getByRole('navigation', { name: 'Primary' }).boundingBox();
    const headerBox = await page.getByRole('banner').boundingBox();
    const mainBox = await page.getByRole('main').boundingBox();
    if (!navBox || !headerBox || !mainBox) throw new Error('expected all three shell regions to render');

    // The sidebar starts at the very top (y=0) and reaches at least as far down as the
    // header + main combined — i.e. it spans both grid rows, not just one.
    expect(navBox.y).toBeLessThanOrEqual(1);
    expect(navBox.height).toBeGreaterThanOrEqual(headerBox.height + mainBox.height - 1);

    // The header starts to the RIGHT of the sidebar (confined to the content column),
    // not at x=0 spanning the full width above the sidebar.
    expect(headerBox.x).toBeGreaterThanOrEqual(navBox.x + navBox.width - 1);
  });

  test('at desktop, exactly one reachable "BIOLOG home" link exists, and it is inside the sidebar', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/');

    const homeLinks = page.getByRole('link', { name: 'BIOLOG home' });
    await expect(homeLinks).toHaveCount(1);
    const isInNav = await homeLinks
      .first()
      .evaluate((el) => el.closest('.biolog-primary-nav') !== null);
    expect(isInNav).toBe(true);
  });

  test('at mobile width, the header keeps its own visible brand link (sidebar is off-canvas by default)', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/');

    // Closed drawer: the sidebar (and its own brand block) is excluded from the
    // accessibility tree (BIO-FND-007-QA-01) — only the header's copy is reachable.
    await expect(page.getByRole('navigation', { name: 'Primary' })).toHaveCount(0);
    const homeLinks = page.getByRole('link', { name: 'BIOLOG home' });
    await expect(homeLinks).toHaveCount(1);
    const isInHeader = await homeLinks.first().evaluate((el) => el.closest('.biolog-header') !== null);
    expect(isInHeader).toBe(true);
  });
});
