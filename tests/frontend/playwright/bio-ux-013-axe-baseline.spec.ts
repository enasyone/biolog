// Developer tests for BIO-UX-013-AC03/AC06. SYNTHETIC: no real data.
//
// A full sweep, not a sample: every static route in web/lib/nav-items.ts plus web/app's own
// route tree, once in light mode and once in dark mode, asserting no unresolved serious/critical
// axe-core violation on any of them (AC03), and that the same holds in dark mode too (AC06's
// "theme parity" for shared components — if a shared component only passes axe in one theme,
// that's exactly the parity gap AC06 asks about). `/people/[personId]` (the one dynamic route)
// is covered separately below, since it needs a real id from the synthetic dataset first.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

// Every static page.tsx under web/app, as of this story (confirmed via `find web/app -name
// page.tsx`) — kept as a literal list, not derived from nav-items.ts, since a few routes here
// (card-designer, cards/issue, cards/replace, settings/authentication,
// administration/integrations, administration/tenants) are reachable but not top-level nav items.
const ROUTES: readonly string[] = [
  '/',
  '/access-control',
  '/administration',
  '/administration/integrations',
  '/administration/tenants',
  '/alumni',
  '/attendance',
  '/audit',
  '/card-designer',
  '/cards',
  '/cards/issue',
  '/cards/replace',
  '/contractors',
  '/credentials',
  '/devices',
  '/document-verify',
  '/duplicate-review',
  '/enrolment',
  '/facial-recognition',
  '/fingerprints',
  '/mobile-credentials',
  '/nfc-uhf',
  '/people',
  '/photographs',
  '/reports',
  '/settings',
  '/settings/authentication',
  '/staff',
  '/students',
  '/transport',
  '/visitors',
];

async function seriousOrCritical(page: Page) {
  const results = await new AxeBuilder({ page }).analyze();
  return results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
}

test.describe('BIO-UX-013-AC03 axe baseline — every route, light mode, admin role', () => {
  for (const route of ROUTES) {
    test(`${route} has no unresolved serious/critical axe violations`, async ({ page }) => {
      // ?as=admin: the highest-privilege role, so gated controls (e.g. Settings' MFA field)
      // render too — a page that's only accessible-clean for a lower-privilege role would be a
      // false pass.
      const sep = route.includes('?') ? '&' : '?';
      await page.goto(`${route}${sep}as=admin`);
      const violations = await seriousOrCritical(page);
      expect(violations, JSON.stringify(violations, null, 2)).toEqual([]);
    });
  }
});

test.describe('BIO-UX-013-AC06 dark-mode parity — every route, no new serious/critical violations', () => {
  for (const route of ROUTES) {
    test(`${route} in dark mode has no unresolved serious/critical axe violations`, async ({ page }) => {
      await page.addInitScript(() => {
        try {
          window.localStorage.setItem('biolog-theme', 'dark');
        } catch {
          // ignore
        }
      });
      const sep = route.includes('?') ? '&' : '?';
      await page.goto(`${route}${sep}as=admin`);
      await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
      const violations = await seriousOrCritical(page);
      expect(violations, JSON.stringify(violations, null, 2)).toEqual([]);
    });
  }
});

test.describe('BIO-UX-013-AC03/AC06 the one dynamic route: /people/[personId]', () => {
  test('a real person detail page has no unresolved serious/critical axe violations, light and dark', async ({
    page,
  }) => {
    await page.goto('/people?as=admin');
    await page.getByRole('link', { name: 'View profile' }).first().click();
    await expect(page).toHaveURL(/\/people\/.+/);
    const light = await seriousOrCritical(page);
    expect(light, JSON.stringify(light, null, 2)).toEqual([]);

    await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'dark'));
    const dark = await seriousOrCritical(page);
    expect(dark, JSON.stringify(dark, null, 2)).toEqual([]);
  });
});
