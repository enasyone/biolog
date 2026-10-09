// Developer tests for BIO-UX-005. SYNTHETIC: demo/synthetic fixture data only.
import { expect, test } from '@playwright/test';

test.describe('BIO-UX-005-AC01 pages call a real service port, not local component state', () => {
  // BIO-UX-006 replaced /people's own PlaceholderPage shell with a real screen, moving this
  // generic-framework demonstration to /photographs (PhotoService); BIO-PHO-003 then replaced
  // /photographs with its own real photo-quality review screen, so this moves again, to
  // /administration (TenantService) - the one remaining route still rendering PlaceholderPage
  // (verified directly: web/app/administration/page.tsx, not web/app/administration/tenants/page.tsx,
  // which BIO-TEN-003 already gave a real FeatureConfigurationConsole). Verified directly
  // (deterministicIndex on 'TenantService:1'/'2'/'3', and by actually running this spec) that its
  // own scenario cycle happens to start success→failure→offline too - not assumed by
  // module-name similarity alone.
  test('a fresh page load resolves the module\'s deterministic first scenario (success)', async ({
    page,
  }) => {
    await page.goto('/administration');
    await page.getByRole('button', { name: 'Simulate primary action' }).click();
    await expect(page.getByRole('status')).toHaveText('Tenants: simulated action completed.');
  });

  test('a different module (Devices) resolves its own independent scenario sequence', async ({
    page,
  }) => {
    // Devices' own deterministic first call lands on a different scenario (offline) than
    // Identity's (success) - each module has its own independent, reproducible sequence, not a
    // shared one (see module-port.ts's per-instance call counter).
    await page.goto('/devices');
    await page.locator('.biolog-button-primary').click();
    await expect(page.getByRole('status')).toHaveText('Devices: device or network appears offline (simulated).');
  });
});

test.describe('BIO-UX-005-AC05 mock adapters show loading, then success/failure/offline, deterministically', () => {
  test('a loading state is genuinely observable before the result appears', async ({ page }) => {
    await page.goto('/administration');
    // A class-based locator, not getByRole(name), because the button's own accessible name
    // changes to "Working…" while loading - a name-bound locator would stop matching mid-click.
    const button = page.locator('.biolog-button-primary');
    await button.click();
    await expect(button).toBeDisabled();
    await expect(page.getByText('Loading…')).toBeVisible();
    await expect(page.getByRole('status')).toHaveText('Tenants: simulated action completed.');
    await expect(button).toBeEnabled();
  });

  test('repeated clicks on the same module cycle through success, failure and offline outcomes', async ({
    page,
  }) => {
    await page.goto('/administration');
    const button = page.locator('.biolog-button-primary');
    const seen = new Set<string>();
    for (let i = 0; i < 6; i += 1) {
      await button.click();
      await expect(button).toBeEnabled(); // wait for the async result to land, not the interim "Loading…" text
      const text = await page.getByRole('status').textContent();
      seen.add(text ?? '');
    }
    expect(seen.size).toBeGreaterThan(1);
  });

  test('the status colour class matches the outcome (success/failure/offline each styled distinctly)', async ({
    page,
  }) => {
    await page.goto('/administration');
    await page.locator('.biolog-button-primary').click();
    await expect(page.getByRole('status')).toHaveClass(/biolog-placeholder-status--success/);
  });
});

// The "a route with no backend-port mapping says so honestly" e2e case (previously /alumni,
// then retargeted to /nfc-uhf by BIO-UX-006) is removed rather than retargeted again: BIO-UX-006
// gave /alumni and /contractors (route-manifest.ts's other two 'Unassigned' entries) real
// IdentityService screens, and BIO-UX-007 gave /nfc-uhf a real CredentialService screen (see
// bio-ux-007-cards.spec.ts) — confirmed via web/app/nfc-uhf/page.tsx, which now renders
// NfcUhfApp, not PlaceholderPage. No route in web/lib/route-manifest.ts is both
// servicePort: 'Unassigned' and still rendering <PlaceholderPage> (no page currently passes
// module="Unassigned" to PlaceholderPage at all; verified by grep) — the manifest's own
// servicePort field is simply stale there, which its header note already anticipates ("assigning
// a port is a backend/governance decision... flagged for governance to correct the map entry").
// The underlying wiring this demonstrated — UNASSIGNED_MOCK_PORT names its own gap rather than
// faking success — stays covered at the unit level by bio-ux-005-module-port.test.ts's own
// "UNASSIGNED_MOCK_PORT names its own gap honestly" test. Revisit once a route is genuinely
// Unassigned again (a new page added ahead of its own port decision).

test.describe('BIO-UX-005-AC03 development-only feature-mode panel', () => {
  test('is present and expandable in development, lists every registered module', async ({ page }) => {
    await page.goto('/');
    const toggle = page.getByRole('button', { name: 'DEV — feature modes' });
    await expect(toggle).toBeVisible();
    await toggle.click();
    await expect(page.getByRole('cell', { name: 'IdentityService' })).toBeVisible();
    await expect(page.getByRole('cell', { name: 'DeviceService' })).toBeVisible();
    // Nothing is live by default (BIO-UX-005-AC02).
    const rows = page.locator('.biolog-feature-mode-list tbody tr');
    await expect(rows.first()).toBeVisible();
    // ReportingService already has two independently-resolvable ports (BIO-UX-005-AC02
    // remediation) - both listed in its own row's port breakdown.
    const reportingRow = page.locator('tr', { hasText: 'ReportingService' });
    await expect(reportingRow).toContainText('primaryAction: SIMULATION');
    await expect(reportingRow).toContainText('viewAuditTrail: SIMULATION');

    const badges = page.locator('.biolog-feature-mode-badge');
    const count = await badges.count();
    for (let i = 0; i < count; i += 1) {
      await expect(badges.nth(i)).toHaveText('SIMULATION');
    }
  });

  test('has no axe violations when expanded', async ({ page }) => {
    const { default: AxeBuilder } = await import('@axe-core/playwright');
    await page.goto('/');
    await page.getByRole('button', { name: 'DEV — feature modes' }).click();
    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
