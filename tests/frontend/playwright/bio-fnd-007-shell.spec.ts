// Developer tests for BIO-FND-007. SYNTHETIC: demo tenant/role names only.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('BIO-FND-007-AC01 responsive shell, light and dark mode', () => {
  test('renders the shell at a mobile viewport with the nav closed by default', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/');
    await expect(page.locator('.biolog-shell')).toHaveAttribute('data-nav-open', 'false');
    await expect(page.locator('.biolog-nav-toggle')).toBeVisible();
    const bodyWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(bodyWidth).toBeLessThanOrEqual(375);
  });

  test('opening the nav toggle reveals primary navigation on mobile', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/');
    await page.getByRole('button', { name: 'Open navigation' }).click();
    await expect(page.locator('.biolog-shell')).toHaveAttribute('data-nav-open', 'true');
    await expect(page.getByRole('navigation', { name: 'Primary' })).toBeVisible();
  });

  test('primary navigation is always visible at desktop width', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto('/');
    await expect(page.locator('.biolog-nav-toggle')).toBeHidden();
    await expect(page.getByRole('navigation', { name: 'Primary' })).toBeVisible();
  });

  test('applies the OS dark colour scheme on first load with no saved preference', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto('/');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  });

  test('applies the OS light colour scheme on first load with no saved preference', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  });

  test('the theme toggle switches theme and persists it across reload', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
    await page.getByRole('button', { name: /switch to dark theme/i }).click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  });
});

test.describe('BIO-FND-007-AC02 brand assets and palette are tokenised', () => {
  test('brand orange and navy custom properties are present on the light theme', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
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

  test('the BIOLOG wordmark is rendered with an accessible name', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('img', { name: 'BIOLOG — Biometric Technology' })).toBeVisible();
  });
});

test.describe('BIO-FND-007-AC03 navigation supports permission-based visibility', () => {
  // Nav catalog and permission sets were extended by BIO-UX-002 (grouped sidebar, full route
  // set) — assertions here were updated to the new item/group names; the underlying mechanism
  // (permission-gated visibility) is unchanged and still BIO-FND-007's own code path.
  test('the default (staff) demo session hides items it has no permission for', async ({ page }) => {
    await page.goto('/');
    const nav = page.getByRole('navigation', { name: 'Primary' });
    await expect(nav.getByRole('link', { name: 'Dashboard' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Card Designer' })).toBeVisible();
    // staff has no nav:devices / nav:settings permission (admin-only areas).
    await expect(nav.getByRole('link', { name: 'Devices' })).toHaveCount(0);
    await expect(nav.getByRole('link', { name: 'Settings' })).toHaveCount(0);
  });

  test('the viewer demo session shows only unrestricted items plus its granted group', async ({
    page,
  }) => {
    await page.goto('/?as=viewer');
    const nav = page.getByRole('navigation', { name: 'Primary' });
    // Dashboard carries no requiredPermission, so it stays visible to every session;
    // Reports/Audit are the only permission-gated items the "viewer" demo role grants.
    // Plus one: TD-SHELL-NAVY-INVERSION gave the sidebar its own "BIOLOG home" brand
    // link at its top, present regardless of permission filtering.
    await expect(nav.getByRole('link')).toHaveCount(4);
    await expect(nav.getByRole('link', { name: 'Dashboard' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Reports' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Audit' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Card Designer' })).toHaveCount(0);
  });

  test('the admin demo session shows every nav item', async ({ page }) => {
    await page.goto('/?as=admin');
    const nav = page.getByRole('navigation', { name: 'Primary' });
    await expect(nav.getByRole('link', { name: 'Devices' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Settings' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Administration', exact: true })).toBeVisible();
  });
});

test.describe('BIO-FND-007-AC05 accessibility baseline', () => {
  // Every reported violation fails the scan, at any impact level (BIO-FND-007-QA-01:
  // filtering to serious/critical let a real defect through as a silent pass). A
  // finding that is a deliberate, reviewed exception belongs here as an explicit
  // waiver with its rule id and reason, not as a blanket impact-level filter.
  const WAIVED_RULE_IDS: readonly string[] = [];

  function assertNoViolations(violations: import('axe-core').Result[]) {
    const unwaived = violations.filter((v) => !WAIVED_RULE_IDS.includes(v.id));
    expect(unwaived, JSON.stringify(unwaived, null, 2)).toEqual([]);
  }

  test('has no axe violations in light mode', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await page.goto('/');
    const results = await new AxeBuilder({ page }).analyze();
    assertNoViolations(results.violations);
  });

  test('has no axe violations in dark mode', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.goto('/');
    const results = await new AxeBuilder({ page }).analyze();
    assertNoViolations(results.violations);
  });

  test('has no axe violations with the nav drawer open on mobile', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/');
    await page.getByRole('button', { name: 'Open navigation' }).click();
    const results = await new AxeBuilder({ page }).analyze();
    assertNoViolations(results.violations);
  });

  test('closed mobile nav links are absent from the accessibility tree and cannot receive keyboard focus (BIO-FND-007-QA-01)', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/');
    const nav = page.getByRole('navigation', { name: 'Primary' });
    // Role queries exclude elements the accessibility tree hides (e.g. visibility:hidden).
    await expect(nav).toHaveCount(0);

    // Tabbing from the nav toggle must never land on a nav link while the drawer is closed.
    await page.getByRole('button', { name: 'Open navigation' }).focus();
    for (let i = 0; i < 6; i += 1) {
      await page.keyboard.press('Tab');
      const focusedIsNavLink = await page.evaluate(
        () => document.activeElement?.closest('.biolog-primary-nav') !== null,
      );
      expect(focusedIsNavLink).toBe(false);
    }
  });
});
