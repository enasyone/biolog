// Developer test for TD-UX011-DASHBOARD-TZ-HYDRATION. SYNTHETIC: demo/synthetic fixture data only.
//
// Found by Emeka (BIO-UX-014) via page.on('pageerror'): the Dashboard's recent-activity
// timestamps were formatted with the runtime's implicit local time zone
// (`Date.prototype.toLocaleString` with no `timeZone` option). The server process and the
// browser can disagree on their local zone, so the server-rendered text and the
// client-rendered text differ — a genuine React hydration mismatch (error #418), reproducible
// for any real user whose browser time zone differs from wherever the app is deployed.
//
// Fixed in app/page.tsx's `formatTimestamp` by passing an explicit, fixed `timeZone:
// 'Africa/Lagos'` (this app's own established tenant-timezone default — see Settings' General
// section) so server and client render identically regardless of either side's local setting.
import { expect, test } from '@playwright/test';

test.describe('TD-UX011-DASHBOARD-TZ-HYDRATION Dashboard timestamps are time-zone-stable', () => {
  test.use({ timezoneId: 'America/New_York' });

  test('no hydration error when the browser time zone disagrees with the server', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', (err) => pageErrors.push(err.message));

    await page.goto('/');
    await expect(page.locator('.biolog-metric-tile').first()).toBeVisible();

    // Give hydration a moment to settle before asserting no error fired.
    await page.waitForTimeout(250);
    expect(pageErrors).toEqual([]);
  });

  test('recent-activity timestamps render identically regardless of the viewing browser\'s time zone', async ({
    browser,
  }) => {
    const contextLagos = await browser.newContext({ timezoneId: 'Africa/Lagos' });
    const contextTokyo = await browser.newContext({ timezoneId: 'Asia/Tokyo' });

    const pageLagos = await contextLagos.newPage();
    const pageTokyo = await contextTokyo.newPage();
    await pageLagos.goto('/');
    await pageTokyo.goto('/');

    const timeLocator = '.biolog-activity-time';
    const hasActivity = (await pageLagos.locator(timeLocator).count()) > 0;
    if (hasActivity) {
      const lagosText = await pageLagos.locator(timeLocator).first().textContent();
      const tokyoText = await pageTokyo.locator(timeLocator).first().textContent();
      expect(tokyoText).toBe(lagosText);
    }

    await contextLagos.close();
    await contextTokyo.close();
  });
});
