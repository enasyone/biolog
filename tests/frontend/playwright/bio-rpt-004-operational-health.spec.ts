// Developer tests for BIO-RPT-004-AC01/AC02/AC03/AC05. SYNTHETIC: no real data. SIMULATION mode,
// runs in the default suite - see bio-rpt-004-operational-health-live.spec.ts for the real
// dual-server LIVE proof.
import { expect, test } from '@playwright/test';

test.describe('BIO-RPT-004 the operational health panel (SIMULATION)', () => {
  test('AC05: mounted on the existing Dashboard route, alongside BIO-UX-011\'s own metrics tiles, untouched', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
    await expect(page.getByTestId('dashboard-metrics')).toBeVisible(); // BIO-UX-011's own tiles, untouched
    await expect(page.getByTestId('operational-health')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Operational health' })).toBeVisible();
  });

  test('AC01: all six domain panels render with their own counts', async ({ page }) => {
    await page.goto('/');
    const panel = page.getByTestId('operational-health');
    await expect(panel.getByTestId('operational-health-panel-identity')).toBeVisible();
    await expect(panel.getByTestId('operational-health-panel-card')).toBeVisible();
    await expect(panel.getByTestId('operational-health-panel-device')).toBeVisible();
    await expect(panel.getByTestId('operational-health-panel-sync')).toBeVisible();
    await expect(panel.getByTestId('operational-health-panel-access')).toBeVisible();
    await expect(panel.getByTestId('operational-health-panel-production')).toBeVisible();
  });

  test('F02: Live and Unavailable are visibly distinct - identity/access/production are Live, card/device/sync are Unavailable, never Stale', async ({ page }) => {
    await page.goto('/');
    const panel = page.getByTestId('operational-health');
    await expect(panel.getByTestId('operational-health-panel-identity').getByText('Live')).toBeVisible();
    await expect(panel.getByTestId('operational-health-panel-access').getByText('Live')).toBeVisible();
    await expect(panel.getByTestId('operational-health-panel-production').getByText('Live')).toBeVisible();
    await expect(panel.getByTestId('operational-health-panel-card').getByText('Unavailable')).toBeVisible();
    await expect(panel.getByTestId('operational-health-panel-device').getByText('Unavailable')).toBeVisible();
    await expect(panel.getByTestId('operational-health-panel-sync').getByText('Unavailable')).toBeVisible();
    await expect(panel.getByText('Stale')).toHaveCount(0); // SIMULATION mode never simulates a degraded-live read
    await expect(panel.getByTestId('operational-health-panel-card')).toContainText('Disclosed HYBRID gap');
    await expect(panel.getByTestId('operational-health-panel-device')).toContainText('Disclosed HYBRID gap');
    await expect(panel.getByTestId('operational-health-panel-sync')).toContainText('Disclosed HYBRID gap');
  });

  test('AC02: the admin role sees every drill-down link', async ({ page }) => {
    await page.goto('/?as=admin');
    const panel = page.getByTestId('operational-health');
    await expect(panel.getByTestId('dashboard-drilldown-identity')).toBeVisible();
    await expect(panel.getByTestId('dashboard-drilldown-card')).toBeVisible();
    await expect(panel.getByTestId('dashboard-drilldown-device')).toBeVisible();
    await expect(panel.getByTestId('dashboard-drilldown-access')).toBeVisible();
    await expect(panel.getByTestId('dashboard-drilldown-production')).toBeVisible();
  });

  test('AC02: the viewer role (nav:reports/nav:audit only) sees zero domain drill-down links', async ({ page }) => {
    await page.goto('/?as=viewer');
    const panel = page.getByTestId('operational-health');
    await expect(panel.getByTestId('dashboard-drilldown-identity')).toHaveCount(0);
    await expect(panel.getByTestId('dashboard-drilldown-card')).toHaveCount(0);
    await expect(panel.getByTestId('dashboard-drilldown-device')).toHaveCount(0);
    await expect(panel.getByTestId('dashboard-drilldown-access')).toHaveCount(0);
    await expect(panel.getByTestId('dashboard-drilldown-production')).toHaveCount(0);
  });

  test('AC02: the staff role sees identity/card/production (nav:cards) but not device/access (admin-only permissions)', async ({ page }) => {
    await page.goto('/?as=staff');
    const panel = page.getByTestId('operational-health');
    await expect(panel.getByTestId('dashboard-drilldown-identity')).toBeVisible();
    await expect(panel.getByTestId('dashboard-drilldown-card')).toBeVisible();
    await expect(panel.getByTestId('dashboard-drilldown-production')).toBeVisible();
    await expect(panel.getByTestId('dashboard-drilldown-device')).toHaveCount(0);
    await expect(panel.getByTestId('dashboard-drilldown-access')).toHaveCount(0);
  });

  test('AC04: switching the tenant selector refreshes the panel\'s own numbers', async ({ page }) => {
    await page.goto('/?tenant=alpha');
    const panel = page.getByTestId('operational-health');
    await expect(panel).toBeVisible();
    await expect(panel.getByTestId('operational-health-panels')).toBeVisible();

    await page.goto('/?tenant=beta');
    await expect(page.getByTestId('operational-health').getByTestId('operational-health-panels')).toBeVisible();
  });
});
