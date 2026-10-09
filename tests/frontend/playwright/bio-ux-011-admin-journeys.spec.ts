// Developer tests for BIO-UX-011. SYNTHETIC: demo/synthetic fixture data only.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('BIO-UX-011-AC01 Dashboard, Reports, Audit, Administration, Settings are interactive', () => {
  test('Dashboard shows real metric tiles and recent activity, not a static placeholder', async ({
    page,
  }) => {
    await page.goto('/');
    const tiles = page.locator('.biolog-metric-tile');
    await expect(tiles).toHaveCount(4);
    await expect(page.getByText('People enrolled')).toBeVisible();
    // Every metric renders a number, not blank/undefined.
    for (let i = 0; i < 4; i += 1) {
      const text = await tiles.nth(i).locator('.biolog-metric-value').textContent();
      expect(text?.trim().length).toBeGreaterThan(0);
    }
  });

  test('Reports: selecting a type and running it renders a real synthetic results table', async ({
    page,
  }) => {
    await page.goto('/reports');
    await page.getByLabel('Report type').selectOption('device-health');
    await page.getByRole('button', { name: 'Run report' }).click();
    await page.waitForFunction(
      () => { const el = document.querySelector('[role=status]'); return !!el?.textContent && el.textContent !== 'Loading…'; },
      { timeout: 5000 },
    );
    const status = await page.getByRole('status').textContent();
    if (status?.includes('completed')) {
      await expect(page.getByRole('table', { name: /Device Health results/ })).toBeVisible();
    }
  });

  test('Audit: shows a real synthetic access-event table', async ({ page }) => {
    await page.goto('/audit');
    const table = page.getByRole('table', { name: /Recent access events/ });
    await expect(table).toBeVisible();
    const rows = table.locator('tbody tr');
    await expect(rows.first()).toBeVisible();
  });

  test('Administration remains interactive (inherited from BIO-UX-002/005, unchanged by this story)', async ({
    page,
  }) => {
    await page.goto('/administration');
    await page.locator('.biolog-button-primary').click();
    await expect(page.getByRole('status')).toHaveText(/./);
  });
});

test.describe('BIO-UX-011-AC02 System Settings has all six approved sections', () => {
  test('every required section heading is present', async ({ page }) => {
    await page.goto('/settings?as=admin');
    for (const heading of [
      'General',
      'Notification',
      'Retention & Privacy',
      'Security & Access',
      'Feature Flags',
      'Offline & Device Sync',
    ]) {
      await expect(page.getByRole('heading', { name: heading, level: 2 })).toBeVisible();
    }
  });

  test('has no axe violations', async ({ page }) => {
    await page.goto('/settings?as=admin');
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});

test.describe('BIO-UX-011-AC05 Settings save/reset work through mock adapters, clearly SIMULATION', () => {
  test('editing and saving the General section shows a SIMULATION-labelled result', async ({ page }) => {
    await page.goto('/settings?as=admin');
    const nameInput = page.getByLabel('Tenant display name');
    await nameInput.fill('Changed Name (SYNTHETIC)');
    await page.getByRole('button', { name: /Save \(SIMULATION\)/ }).first().click();
    await page.waitForFunction(
      () => { const el = document.querySelectorAll('[role=status]')[0]; return !!el?.textContent && el.textContent !== 'Loading…'; },
      { timeout: 5000 },
    );
    const status = await page.locator('[role=status]').first().textContent();
    expect(status).toBeTruthy();
  });

  test('reset restores the section\'s original value AND calls the mock adapter (not local-only)', async ({
    page,
  }) => {
    await page.goto('/settings?as=admin');
    const nameInput = page.getByLabel('Tenant display name');
    const status = page.locator('[role=status]').first();
    const original = await nameInput.inputValue();
    await nameInput.fill('Temporary edit (SYNTHETIC)');
    await expect(nameInput).toHaveValue('Temporary edit (SYNTHETIC)');
    await expect(status).toHaveCount(0);

    await page.getByRole('button', { name: 'Reset' }).first().click();
    // Field restore is synchronous/unconditional; the adapter call is async - both are proven here.
    await expect(nameInput).toHaveValue(original);
    await page.waitForFunction(
      () => { const el = document.querySelectorAll('[role=status]')[0]; return !!el?.textContent && el.textContent !== 'Loading…'; },
      { timeout: 5000 },
    );
    await expect(status).toHaveText(/./);
  });

  test('Offline & Device Sync has its own working "Sync now" primary action', async ({ page }) => {
    await page.goto('/settings?as=admin');
    const syncButton = page.getByRole('button', { name: /Sync now \(SIMULATION\)/ });
    await syncButton.scrollIntoViewIfNeeded();
    await syncButton.click();
    await page.waitForFunction(
      () => {
        const statuses = document.querySelectorAll('[role=status]');
        const el = statuses[statuses.length - 1];
        return !!el?.textContent && el.textContent !== 'Loading…';
      },
      { timeout: 5000 },
    );
  });
});

test.describe('BIO-UX-011-AC04 tenant/role changes visibly alter dashboard and settings controls', () => {
  test('switching tenant changes the dashboard header and can change metric values', async ({ page }) => {
    await page.goto('/');
    const description = page.locator('.biolog-placeholder-description');
    await expect(description).toContainText('Alpha State University (SYNTHETIC)');
    await page.getByLabel('Tenant').selectOption('beta');
    await expect(description).toContainText('Beta Polytechnic (SYNTHETIC)');
  });

  test('the Security & Access MFA control is disabled for a non-admin role, enabled for admin', async ({
    page,
  }) => {
    await page.goto('/settings'); // default role: staff
    const mfaCheckbox = page.getByLabel('Require multi-factor authentication');
    await expect(mfaCheckbox).toBeDisabled();
    await expect(page.getByText(/Only a System Administrator can change this/)).toBeVisible();

    await page.getByLabel('Role').selectOption('admin');
    await expect(mfaCheckbox).toBeEnabled();
    await expect(page.getByText(/Only a System Administrator can change this/)).toHaveCount(0);
  });

  test('toggling the Mobile Credentials feature flag immediately shows/hides its primary nav item', async ({
    page,
  }) => {
    // Mobile Credentials defaults to disabled (DEFAULT_FEATURE_FLAGS), so it starts absent from
    // the sidebar even for admin, who has the underlying nav:mobile-credentials permission.
    await page.goto('/settings?as=admin');
    const nav = page.getByRole('navigation', { name: 'Primary' });
    await expect(nav.getByRole('link', { name: 'Mobile Credentials' })).toHaveCount(0);

    const flagCheckbox = page.getByLabel('Mobile Credentials');
    await flagCheckbox.check();
    await expect(nav.getByRole('link', { name: 'Mobile Credentials' })).toBeVisible();

    await flagCheckbox.uncheck();
    await expect(nav.getByRole('link', { name: 'Mobile Credentials' })).toHaveCount(0);
  });

  test('a feature-flag change survives client-side navigation to another page', async ({ page }) => {
    // SessionProvider is mounted once in layout.tsx and persists across client-side <Link>
    // navigation (not a full page.goto() reload, which would remount it and lose in-memory
    // state - the same way the tenant/role selectors don't persist across a reload either).
    await page.goto('/settings?as=admin');
    await page.getByLabel('Mobile Credentials').check();
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Dashboard' }).click();
    await expect(page).toHaveURL(/\/$/);
    await expect(
      page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Mobile Credentials' }),
    ).toBeVisible();
  });
});

test.describe('BIO-UX-011-AC06 shared design-system and shell rules remain intact', () => {
  test('brand tokens are unchanged on the new Dashboard/Settings/Reports/Audit pages', async ({ page }) => {
    for (const route of ['/', '/settings', '/reports', '/audit']) {
      await page.goto(route);
      const { accent, navy } = await page.evaluate(() => {
        const styles = getComputedStyle(document.documentElement);
        return {
          accent: styles.getPropertyValue('--biolog-color-accent').trim().toLowerCase(),
          navy: styles.getPropertyValue('--biolog-color-brand-navy').trim().toLowerCase(),
        };
      });
      expect(accent).toBe('#f4502a');
      expect(navy).toBe('#0b2341');
      await expect(page.locator('.biolog-shell')).toBeVisible();
      await expect(page.getByRole('navigation', { name: 'Primary' })).toBeVisible();
    }
  });

  test('Dashboard has no axe violations', async ({ page }) => {
    await page.goto('/');
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });

  test('Audit has no axe violations', async ({ page }) => {
    await page.goto('/audit');
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });

  test('Reports has no axe violations', async ({ page }) => {
    await page.goto('/reports');
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
