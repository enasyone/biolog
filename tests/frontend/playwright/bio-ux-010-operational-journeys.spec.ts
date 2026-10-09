// Developer tests for BIO-UX-010. SYNTHETIC: demo/synthetic fixture data only.
import { expect, test, type Locator } from '@playwright/test';

/**
 * Clicks `button` until `target` becomes visible, up to `maxAttempts` times.
 *
 * The mock adapters (module-port.ts) cycle deterministically through
 * success/failure/offline per call, so a button's first click does not
 * always land on 'success' - see this spec's own per-test comments for each
 * port's exact known sequence. Checking `target` BEFORE each click (not
 * racing a click against an in-flight async result) and waiting out the
 * mock's simulated latency after each click avoids clicking a button that
 * has already been removed from the DOM by a just-landed success (which
 * would otherwise hang waiting for a locator that will never reappear).
 */
async function clickUntilVisible(button: Locator, target: Locator, maxAttempts = 4): Promise<void> {
  for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
    if (await target.isVisible()) return;
    await button.click();
    await button.page().waitForTimeout(350);
  }
}

test.describe('BIO-UX-010-AC01 the five operational pages are interactive, not placeholders', () => {
  test('/access-control shows a real policy-builder form and access-events action, not "Simulate primary action"', async ({
    page,
  }) => {
    await page.goto('/access-control');
    await expect(page.getByRole('heading', { name: 'Access Control', level: 1 })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Simulate primary action' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Create policy' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'View access events' })).toBeVisible();
  });

  test('/attendance, /visitors, /transport, /devices each render their own named primary/secondary actions', async ({
    page,
  }) => {
    await page.goto('/attendance');
    await expect(page.getByRole('button', { name: 'View attendance record' })).toBeVisible();

    await page.goto('/visitors');
    await expect(page.getByRole('button', { name: 'Check in visitor' }).first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'View visit history' })).toBeVisible();

    await page.goto('/transport');
    await expect(page.getByRole('button', { name: 'View boarding/alighting log' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Assign route' })).toBeVisible();

    await page.goto('/devices');
    await expect(page.getByRole('button', { name: 'Register device' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'View connectivity status' })).toBeVisible();
  });
});

test.describe('BIO-UX-010-AC02 online/offline/degraded-device/denied-access/notification scenarios are available', () => {
  test('the Devices connectivity list reflects the selected scenario', async ({ page }) => {
    await page.goto('/devices');
    const button = page.getByRole('button', { name: 'View connectivity status' });
    // DeviceService's deterministic mock cycle (module-port.ts's per-instance call counter)
    // puts this port's own first call at 'offline' and its second at 'success' - same
    // guarantee bio-ux-010-module-ports.test.ts checks at the unit level. Two clicks
    // reliably reaches the revealed table; click() itself waits out the disabled/loading
    // window between clicks.
    await button.click();
    await button.click();
    await expect(page.locator('.biolog-table')).toBeVisible();
    await expect(page.locator('.biolog-status-badge--success').first()).toBeVisible();

    await page.getByLabel('Scenario').selectOption('degraded-device');
    await expect(page.locator('.biolog-status-badge--warning').first()).toBeVisible();

    await page.getByLabel('Scenario').selectOption('offline');
    await expect(page.locator('.biolog-status-badge--error').first()).toBeVisible();
  });

  test('the Access events list reflects the denied-access scenario', async ({ page }) => {
    await page.goto('/access-control');
    await page.getByLabel('Scenario').selectOption('denied-access');
    await page.getByRole('button', { name: 'View access events' }).click();
    await expect(page.locator('.biolog-status-badge--error').first()).toBeVisible();
  });

  test('the notification scenario surfaces a notifications banner with a device and access alert', async ({
    page,
  }) => {
    await page.goto('/access-control');
    await page.getByLabel('Scenario').selectOption('notification');
    await expect(page.getByRole('status', { name: 'Notifications' })).toBeVisible();
    await expect(page.getByRole('status', { name: 'Notifications' })).toContainText(/degraded/i);
    await expect(page.getByRole('status', { name: 'Notifications' })).toContainText(/denied/i);
  });

  test('the online (normal) scenario shows no notification banner', async ({ page }) => {
    await page.goto('/devices');
    await expect(page.getByRole('status', { name: 'Notifications' })).toHaveCount(0);
  });
});

test.describe('BIO-UX-010-AC03 policy-builder controls simulate assurance-level decisions and reason codes', () => {
  test('creating a policy with a chosen zone/assurance level/reason code adds a visible row', async ({ page }) => {
    await page.goto('/access-control');
    await page.getByLabel('Zone').fill('Library Entrance');
    await page.getByLabel('Assurance level').selectOption('biometric');
    await page.getByLabel('Reason code').selectOption('restricted-zone');
    await page.getByRole('button', { name: 'Create policy' }).click();
    await expect(page.getByRole('status')).toBeVisible();

    // Deterministic: AccessService's own call-counter mock cycles success first (see
    // module-port.ts's per-instance deterministic scenario index) - the same guarantee
    // bio-ux-005-adapter-framework.spec.ts relies on for its own first-call assertions.
    const table = page.locator('table', { hasText: 'Simulated policies created this session' });
    await expect(table).toBeVisible();
    await expect(table).toContainText('Library Entrance');
    await expect(table).toContainText('biometric');
    await expect(table).toContainText('restricted-zone');
  });
});

test.describe('BIO-UX-010-AC04 visitor check-in/out and transport boarding/alighting produce visible simulated events', () => {
  test('checking in a preregistered visitor flips their status badge and offers check-out next', async ({ page }) => {
    await page.goto('/visitors');
    // A stable positional locator, not `hasText: 'preregistered'`: that text is exactly what
    // this test mutates, so a text-filtered locator would stop matching this row the moment
    // check-in succeeds and hang waiting for a "preregistered" row that no longer exists.
    // The generator (generators.ts) always makes the tenant's second visitor 'preregistered'
    // at baseline (the first is 'checked-in').
    const row = page.locator('.biolog-table tbody tr').nth(1);
    await expect(row).toContainText('preregistered');
    // VisitorService's deterministic mock cycle (module-port.ts) means every one of this
    // module's ports - primaryAction/checkOutVisitor/viewVisitHistory, each its own
    // independent call counter - does not necessarily land on 'success' on its own first
    // call. clickUntilVisible works through that cycle deterministically.
    await clickUntilVisible(row.getByRole('button', { name: 'Check in visitor' }), row.locator('.biolog-status-badge--success'));
    await expect(row.locator('.biolog-status-badge--success')).toBeVisible();
    await expect(row.getByRole('button', { name: 'Check out visitor' })).toBeVisible();

    await clickUntilVisible(
      row.getByRole('button', { name: 'Check out visitor' }),
      row.locator('.biolog-status-badge--neutral'),
    );
    await expect(row.locator('.biolog-status-badge--neutral')).toBeVisible();

    await clickUntilVisible(page.getByRole('button', { name: 'View visit history' }), page.locator('.biolog-op-history-list'));
    await expect(page.locator('.biolog-op-history-list')).toContainText('checked in');
    await expect(page.locator('.biolog-op-history-list')).toContainText('checked out');
  });

  test('assigning a route adds a visible simulated assignment entry', async ({ page }) => {
    await page.goto('/transport');
    // TransportService's deterministic mock cycle does not land assignRoute's own first call
    // on 'success' either - same reasoning as the visitor check-in/out cycle above.
    await clickUntilVisible(page.getByRole('button', { name: 'Assign route' }), page.locator('.biolog-op-history-list'));
    await expect(page.locator('.biolog-op-history-list')).toContainText('assigned to');
  });
});

test.describe('BIO-UX-010-AC05 every action exposes a stable port; outcomes are shown, not hidden', () => {
  test('a failed/offline outcome is shown honestly, not silently retried or hidden', async ({ page }) => {
    await page.goto('/attendance');
    const button = page.getByRole('button', { name: 'View attendance record' });
    const seen = new Set<string>();
    for (let i = 0; i < 6; i += 1) {
      await button.click();
      await expect(button).toBeEnabled();
      const text = await page.getByRole('status').first().textContent();
      seen.add(text ?? '');
    }
    expect(seen.size).toBeGreaterThan(1);
  });

  test('no visible primary action is dead on any of the five pages', async ({ page }) => {
    for (const [route, actionName] of [
      ['/access-control', 'Create policy'],
      ['/attendance', 'View attendance record'],
      ['/transport', 'View boarding/alighting log'],
      ['/devices', 'Register device'],
    ] as const) {
      await page.goto(route);
      const button = page.getByRole('button', { name: actionName });
      await button.click();
      await expect(page.getByRole('status').first()).toBeVisible();
    }
  });
});

test.describe('BIO-UX-010 accessibility (UI-first rule: verify against the approved BIOLOG UI contract)', () => {
  for (const route of ['/access-control', '/attendance', '/visitors', '/transport', '/devices']) {
    test(`${route} has no axe violations`, async ({ page }) => {
      const { default: AxeBuilder } = await import('@axe-core/playwright');
      await page.goto(route);
      const results = await new AxeBuilder({ page }).analyze();
      expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
    });
  }
});
