// Developer tests for BIO-UX-002. SYNTHETIC: demo tenant/role/search fixture data only.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

// Every route the shell's primary nav (or a placeholder page's related-links) points at
// (BIO-UX-002-AC04: "every route renders a deliberate page shell rather than a
// blank/dead route"). Mirrors web/lib/nav-items.ts + the /cards, /administration,
// /settings related-link sub-routes.
const ALL_ROUTES: readonly string[] = [
  '/',
  '/people',
  '/students',
  '/staff',
  '/visitors',
  '/alumni',
  '/contractors',
  '/enrolment',
  '/photographs',
  '/fingerprints',
  '/facial-recognition',
  '/duplicate-review',
  '/cards',
  '/credentials',
  '/cards/issue',
  '/cards/replace',
  '/card-designer',
  '/nfc-uhf',
  '/mobile-credentials',
  '/document-verify',
  '/access-control',
  '/attendance',
  '/transport',
  '/devices',
  '/reports',
  '/audit',
  '/administration',
  '/administration/tenants',
  '/administration/integrations',
  '/settings',
  '/settings/authentication',
];

test.describe('BIO-UX-002-AC04 every route renders a deliberate page shell', () => {
  for (const route of ALL_ROUTES) {
    test(`${route} renders inside the shell, not the not-found page`, async ({ page }) => {
      const url = route === '/' ? '/?as=admin' : `${route}?as=admin`;
      await page.goto(url);
      await expect(page.locator('.biolog-shell')).toBeVisible();
      await expect(page.getByRole('heading', { level: 1 }).first()).not.toHaveText('Not yet available');
      await expect(page.getByRole('navigation', { name: 'Primary' })).toBeVisible();
    });
  }
});

test.describe('BIO-UX-002-AC04 no-dead-primary-button rule on placeholder pages', () => {
  test('the "Simulate primary action" button on a placeholder page produces a visible result', async ({
    page,
  }) => {
    // Since BIO-UX-005, this button calls a real mock service port (module-port.ts) rather than
    // flipping local state. Retargeted from /people to /photographs by BIO-UX-006, which gave
    // /people a real screen (its own AC01) that no longer renders this generic placeholder button
    // (see bio-ux-005-adapter-framework.spec.ts for the fuller loading/success/failure/offline
    // coverage this story added, also retargeted — /photographs' own deterministic first scenario
    // is success, verified directly rather than assumed).
    await page.goto('/photographs?as=admin');
    await expect(page.getByRole('status')).toHaveCount(0);
    await page.getByRole('button', { name: 'Simulate primary action' }).click();
    await expect(page.getByRole('status')).toHaveText(/simulated action completed/i);
  });

  test('related-links from a placeholder page navigate to a real sub-route', async ({ page }) => {
    // "Issue Card" also appears in the primary nav (every role that can see Cards can see
    // it), so this scopes to the related-links panel specifically, not the nav landmark.
    await page.goto('/cards');
    await page.getByRole('navigation', { name: 'Cards related pages' }).getByRole('link', { name: 'Issue Card' }).click();
    await expect(page).toHaveURL(/\/cards\/issue/);
    await expect(page.getByRole('heading', { name: 'Issue Card', level: 1 })).toBeVisible();
  });
});

test.describe('BIO-UX-002-AC01 global search is a real, working primary action', () => {
  test('typing a query shows deterministic synthetic matches', async ({ page }) => {
    await page.goto('/');
    const search = page.getByRole('searchbox', { name: /search people, cards, devices/i });
    await search.fill('amara');
    await expect(page.getByRole('link', { name: /Amara Adeleke/ })).toBeVisible();
  });

  test('a query with no matches shows an explicit empty state, not nothing', async ({ page }) => {
    await page.goto('/');
    const search = page.getByRole('searchbox', { name: /search people, cards, devices/i });
    await search.fill('zzz-no-such-record');
    await expect(page.getByText(/No matches for/)).toBeVisible();
  });

  test('selecting a result navigates to its route', async ({ page }) => {
    await page.goto('/');
    const search = page.getByRole('searchbox', { name: /search people, cards, devices/i });
    await search.fill('issue card');
    await page.getByRole('link', { name: /Issue Card/ }).first().click();
    await expect(page).toHaveURL(/\/cards\/issue/);
  });
});

test.describe('BIO-UX-002-AC01 tenant selector and role selector are real controls', () => {
  test('switching role updates visible nav items without a full page reload error', async ({
    page,
  }) => {
    await page.goto('/');
    const nav = page.getByRole('navigation', { name: 'Primary' });
    await expect(nav.getByRole('link', { name: 'Devices' })).toHaveCount(0);
    await page.getByLabel('Role').selectOption('admin');
    await expect(nav.getByRole('link', { name: 'Devices' })).toBeVisible();
  });

  test('switching tenant updates the session without error', async ({ page }) => {
    await page.goto('/');
    await page.getByLabel('Tenant').selectOption('beta');
    await expect(page.getByLabel('Tenant')).toHaveValue('beta');
  });
});

test.describe('BIO-UX-002-AC01 user menu sign-out is a real, working primary action', () => {
  test('sign out flips to a visible signed-out state, and sign-back-in restores the menu', async ({
    page,
  }) => {
    await page.goto('/');
    await page.getByRole('button', { name: /user menu/i }).click();
    await page.getByRole('menuitem', { name: 'Sign out' }).click();
    await expect(page.getByText('Signed out (SIMULATION)')).toBeVisible();
    await page.getByRole('button', { name: 'Sign back in' }).click();
    await expect(page.getByRole('button', { name: /user menu/i })).toBeVisible();
  });
});

test.describe('BIO-UX-002-AC03 responsive and keyboard navigable with visible focus states', () => {
  test('every header control is reachable by keyboard in document order', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/');
    // TD-SHELL-NAVY-INVERSION: the brand link now lives in the sidebar (tab-ordered
    // before the header, since the sidebar is the full-height left column — see the
    // dedicated shell-structure describe block below), so this test starts from the
    // header's own first control (search) rather than the brand link.
    await page.getByRole('searchbox', { name: /search people, cards, devices/i }).focus();
    // tenant select, role select, theme toggle, user menu button.
    for (let i = 0; i < 4; i += 1) {
      await page.keyboard.press('Tab');
      const focused = await page.evaluate(() => document.activeElement?.tagName.toLowerCase());
      expect(['input', 'select', 'button']).toContain(focused);
    }
  });

  test('a keyboard-focused nav link shows a visible focus outline', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/');
    const firstLink = page.getByRole('navigation', { name: 'Primary' }).getByRole('link').first();
    await firstLink.focus();
    const outline = await firstLink.evaluate((el) => getComputedStyle(el).outlineStyle);
    expect(outline).not.toBe('none');
  });
});

test.describe('BIO-UX-002-AC05 shell carries no feature-specific business logic', () => {
  test('a placeholder page never calls a feature API — only renders the shared PlaceholderPage shape', async ({
    page,
  }) => {
    // Retargeted from /people to /document-verify by BIO-UX-006 — /people is a real domain
    // screen now (its own story), not a representative shell-owned placeholder.
    const requests: string[] = [];
    page.on('request', (req) => {
      const url = req.url();
      if (!url.includes('/_next/') && new URL(url).pathname !== '/document-verify') {
        requests.push(url);
      }
    });
    await page.goto('/document-verify');
    await page.waitForLoadState('networkidle');
    expect(requests.filter((u) => /\/api\//.test(u))).toEqual([]);
  });
});

test.describe('BIO-UX-002-AC06 branding matches the approved baseline, no redesign', () => {
  test('brand orange/navy custom properties are unchanged from BIO-FND-007/BIO-UX-001', async ({
    page,
  }) => {
    await page.goto('/');
    const { accent, navy } = await page.evaluate(() => {
      const styles = getComputedStyle(document.documentElement);
      return {
        accent: styles.getPropertyValue('--biolog-color-accent').trim().toLowerCase(),
        navy: styles.getPropertyValue('--biolog-color-brand-navy').trim().toLowerCase(),
      };
    });
    expect(accent).toBe('#f4502a');
    expect(navy).toBe('#0b2341');
  });
});

test.describe('BIO-UX-002 accessibility baseline on the extended shell', () => {
  const WAIVED_RULE_IDS: readonly string[] = [];

  function assertNoViolations(violations: import('axe-core').Result[]) {
    const unwaived = violations.filter((v) => !WAIVED_RULE_IDS.includes(v.id));
    expect(unwaived, JSON.stringify(unwaived, null, 2)).toEqual([]);
  }

  test('dashboard has no axe violations with the full grouped nav (admin role)', async ({ page }) => {
    await page.goto('/?as=admin');
    const results = await new AxeBuilder({ page }).analyze();
    assertNoViolations(results.violations);
  });

  test('a representative placeholder page (Document Verify) has no axe violations', async ({ page }) => {
    // Retargeted from /people by BIO-UX-006 — /people is now a real domain screen with its own
    // axe coverage in bio-ux-006-*.spec.ts, not a representative shell-owned placeholder.
    await page.goto('/document-verify?as=admin');
    const results = await new AxeBuilder({ page }).analyze();
    assertNoViolations(results.violations);
  });

  test('the open search-results panel has no axe violations', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('searchbox', { name: /search people, cards, devices/i }).fill('card');
    const results = await new AxeBuilder({ page }).analyze();
    assertNoViolations(results.violations);
  });

  test('the open user menu panel has no axe violations', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: /user menu/i }).click();
    const results = await new AxeBuilder({ page }).analyze();
    assertNoViolations(results.violations);
  });
});
