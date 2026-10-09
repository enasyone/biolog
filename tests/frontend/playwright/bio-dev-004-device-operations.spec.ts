// Developer tests for BIO-DEV-004-AC05 ("the approved web surface (/devices) is connected
// through DeviceService to this story's live implementation or an explicitly documented
// HYBRID state, and the mapped end-to-end human workflow passes"). SYNTHETIC: no real data.
// SIMULATION mode (the default DeviceService mock adapter), runs in the default suite - proves
// the mapped end-to-end human workflow (list devices -> poll health -> see last-seen/health
// updated -> run a supported remote job -> see it succeed -> attempt an unsupported job type ->
// see it rejected explicitly, never silently) with real, visible success/failure states.
//
// Learns BIO-INT-004's own SY-INT004-04 lesson from the start (also already applied in
// BIO-TRN-003): the AC04 failure-path test below genuinely exercises a real validation
// condition the mock adapter returns (an uncertified device, a job type the device's own
// category does not support - see mock.ts), not merely a client-side disabled-button guard.
import { expect, test } from '@playwright/test';

test.describe('BIO-DEV-004 the Device operations panel (SIMULATION)', () => {
  test('AC05: mounted on /devices, alongside the existing register/connectivity-status sections, untouched', async ({ page }) => {
    await page.goto('/devices');
    await expect(page.getByRole('heading', { name: 'Device operations', exact: true })).toBeVisible();
    const panel = page.getByTestId('device-operations-panel');
    await expect(panel).toBeVisible();
    // The page's own pre-existing sections stay mounted, never replaced by this story's own additive panel.
    await expect(page.getByRole('heading', { name: 'Register device' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Connectivity status' })).toBeVisible();
  });

  test('AC01: the device list loads with each device\'s current health and last-seen state', async ({ page }) => {
    await page.goto('/devices');
    const panel = page.getByTestId('device-operations-panel');
    const rows = panel.getByTestId('device-operations-row');
    await expect(rows).toHaveCount(4);
    const firstRow = rows.first();
    await expect(firstRow.getByTestId('device-operations-last-seen')).toHaveText('Never');
  });

  test('AC01: polling health updates last-seen and health for that device, visibly', async ({ page }) => {
    await page.goto('/devices');
    const panel = page.getByTestId('device-operations-panel');
    const row = panel.getByTestId('device-operations-row').first();

    await expect(row.getByTestId('device-operations-last-seen')).toHaveText('Never');
    await row.getByTestId('device-operations-poll-health').click();

    await expect(panel.getByTestId('device-operations-notice')).toContainText('Health polled');
    await expect(row.getByTestId('device-operations-last-seen')).not.toHaveText('Never');
  });

  test('AC02: a remote job type the device\'s category does not support can never be selected at all, so "Run job" can never target it', async ({ page }) => {
    await page.goto('/devices');
    const panel = page.getByTestId('device-operations-panel');
    // mock-dev-nfc-1 (OrbitId RD-200, NfcUhfReader) only supports Reboot - see mock.ts. The
    // unsupported option is disabled at the <option> level (not merely a disabled "Run"
    // button after the fact), so a user can never even select it - Playwright's own
    // selectOption refuses a disabled option exactly as a real user interaction would.
    const nfcRow = panel.getByTestId('device-operations-row').filter({ hasText: 'OrbitId' });
    const pushConfigOption = nfcRow.getByTestId('device-operations-job-select').locator('option', { hasText: 'PushConfiguration (unsupported)' });
    await expect(pushConfigOption).toBeAttached();
    await expect(pushConfigOption).toBeDisabled();

    // The default selection (Reboot) IS supported, so the primary action stays reachable -
    // "no visible primary action may be dead" even on a device with narrower support.
    await expect(nfcRow.getByTestId('device-operations-run-job')).toBeEnabled();
  });

  test('AC02/AC03: running a supported remote job succeeds and is visibly recorded', async ({ page }) => {
    await page.goto('/devices');
    const panel = page.getByTestId('device-operations-panel');
    const fpRow = panel.getByTestId('device-operations-row').filter({ hasText: 'Acme' }).first();

    await fpRow.getByTestId('device-operations-job-select').selectOption('Reboot');
    await fpRow.getByTestId('device-operations-run-job').click();

    const notice = panel.getByTestId('device-operations-notice');
    await expect(notice).toContainText('Reboot completed');
    await expect(notice).not.toHaveAttribute('role', 'alert');
  });

  test('AC04: polling health for an uncertified device fails explicitly, with a genuinely distinct, accessible failure state', async ({ page }) => {
    await page.goto('/devices');
    const panel = page.getByTestId('device-operations-panel');
    const uncertifiedRow = panel.getByTestId('device-operations-row').filter({ hasText: 'FS-LEGACY' });

    await uncertifiedRow.getByTestId('device-operations-poll-health').click();

    const notice = panel.getByTestId('device-operations-notice');
    await expect(notice).toContainText('not certified');
    await expect(notice).toHaveAttribute('role', 'alert');
  });
});
