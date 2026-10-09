// Developer tests for BIO-TRN-003-AC05 ("the approved web surface (/transport) is connected
// through TransportService to this story's live implementation or an explicitly documented
// HYBRID state, and the mapped end-to-end human workflow passes"). SYNTHETIC: no real data.
// SIMULATION mode (the default TransportService mock adapter), runs in the default suite -
// proves the mapped end-to-end human workflow (list -> create a rule -> see it matched against
// the synthetic feed -> see both a visible SUCCESS delivery and a visible, accessible FAILURE
// delivery) with real, visible success/failure states, the same bar every sibling story's own
// "(SIMULATION)" suite already establishes.
//
// This suite learns BIO-INT-004's own SY-INT004-04 lesson from the start: the failure-path test
// below genuinely invokes a failed create call (an empty name, a real validation condition
// `createTransportNotificationRuleMockPort` already returns - see mock.ts), not merely a
// client-side disabled-button guard - and separately, the deliveries table's own dead-lettered
// row is asserted to render with role="alert", distinct from a delivered row.
import { expect, test } from '@playwright/test';

test.describe('BIO-TRN-003 the Transport notifications panel (SIMULATION)', () => {
  test('AC05: mounted on /transport, alongside the existing boarding/alighting log and route-assignment sections, untouched', async ({ page }) => {
    await page.goto('/transport');
    await expect(page.getByRole('heading', { name: 'Transport notifications', exact: true })).toBeVisible();
    const panel = page.getByTestId('transport-notifications-panel');
    await expect(panel).toBeVisible();
    // The page's own pre-existing sections stay mounted, never replaced by this story's own additive panel.
    await expect(page.getByRole('heading', { name: 'Boarding / alighting log' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Assign route' })).toBeVisible();
  });

  test('AC01: a tenant with no rules yet sees an explicit empty state, never a silent blank table', async ({ page }) => {
    await page.goto('/transport');
    const panel = page.getByTestId('transport-notifications-panel');
    await expect(panel.getByTestId('transport-notification-rules-empty')).toBeVisible();
    await expect(panel.getByTestId('transport-notification-rule-row')).toHaveCount(0);
  });

  test('AC01/AC03: creating a rule for board events shows the new rule, then a matching visible SUCCESS delivery and a visible, accessible FAILURE delivery', async ({ page }) => {
    await page.goto('/transport');
    const panel = page.getByTestId('transport-notifications-panel');

    await panel.getByTestId('transport-notification-rule-name-input').fill('Board and exception alerts');
    // board is checked by default; also select exception, so this single rule proves both a
    // delivered row (the synthetic board event) and a dead-lettered row (the synthetic
    // "mock-evt-exception-1" item, a deliberate, disclosed failure fixture - see mock.ts).
    await panel.getByTestId('transport-notification-event-type-exception').check();
    await panel.getByTestId('transport-notification-rule-create').click();

    // Visible SUCCESS state #1: the notice.
    await expect(panel.getByTestId('transport-notification-rule-notice')).toContainText('created');
    // Visible SUCCESS state #2: the new row.
    const row = panel.getByTestId('transport-notification-rule-row').first();
    await expect(row).toContainText('Board and exception alerts');
    await expect(row.getByTestId('transport-notification-rule-status')).toHaveText('Enabled');

    // AC03 - the delivery trail shows BOTH a delivered row and a genuinely distinct, accessible
    // failed row, never a plain cell identical in markup for both outcomes.
    const deliveredOutcome = panel.getByTestId('transport-notification-delivery-outcome').filter({ hasText: 'Delivered' }).first();
    await expect(deliveredOutcome).toBeVisible();
    await expect(deliveredOutcome).not.toHaveAttribute('role', 'alert');

    const failedOutcome = panel.getByTestId('transport-notification-delivery-outcome').filter({ hasText: 'Failed' }).first();
    await expect(failedOutcome).toBeVisible();
    await expect(failedOutcome).toHaveAttribute('role', 'alert');
  });

  test('AC01: creating with no event type selected is rejected with a visible failure state, never a silent no-op', async ({ page }) => {
    await page.goto('/transport');
    const panel = page.getByTestId('transport-notifications-panel');

    // Uncheck the only default-selected event type, leaving zero selected.
    await panel.getByTestId('transport-notification-event-type-board').uncheck();

    // The create button itself disables with nothing selected - the UI's own first line of
    // defense, never relying on the backend alone to reject an empty selection.
    await expect(panel.getByTestId('transport-notification-rule-create')).toBeDisabled();
    await expect(panel.getByTestId('transport-notification-rule-row')).toHaveCount(0);
  });

  test('SY-INT004-04 lesson applied: a genuinely failed create call (empty name, a real validation failure the mock adapter returns) renders a visible, accessible error state, not a silent no-op', async ({ page }) => {
    await page.goto('/transport');
    const panel = page.getByTestId('transport-notifications-panel');

    // board stays checked (the default), so the create button is NOT disabled by the
    // client-side zero-event-types guard the OTHER failure test above exercises - this call
    // genuinely reaches createTransportNotificationRuleMockPort, which rejects an empty name
    // (web/features/bio-trn-003/mock.ts), the same way its live adapter would reject a request
    // the real backend rejected with a non-2xx response.
    await panel.getByTestId('transport-notification-rule-name-input').fill('');
    await expect(panel.getByTestId('transport-notification-rule-create')).toBeEnabled();
    await panel.getByTestId('transport-notification-rule-create').click();

    const notice = panel.getByTestId('transport-notification-rule-notice');
    await expect(notice).toHaveAttribute('role', 'alert');
    await expect(notice).toContainText('name is required');

    // Nothing silently succeeded: no new row.
    await expect(panel.getByTestId('transport-notification-rule-row')).toHaveCount(0);
  });

  test('AC01: toggling a rule disabled, then enabled again, shows both visible status transitions', async ({ page }) => {
    await page.goto('/transport');
    const panel = page.getByTestId('transport-notifications-panel');

    await panel.getByTestId('transport-notification-rule-name-input').fill('Toggle test rule');
    await panel.getByTestId('transport-notification-rule-create').click();
    const row = panel.getByTestId('transport-notification-rule-row').first();
    await expect(row.getByTestId('transport-notification-rule-status')).toHaveText('Enabled');

    await row.getByTestId('transport-notification-rule-toggle').click();
    await expect(panel.getByTestId('transport-notification-rule-notice')).toContainText('disabled');
    await expect(row.getByTestId('transport-notification-rule-status')).toHaveText('Disabled');
    await expect(row.getByTestId('transport-notification-rule-toggle')).toHaveText('Enable');

    await row.getByTestId('transport-notification-rule-toggle').click();
    await expect(panel.getByTestId('transport-notification-rule-notice')).toContainText('enabled');
    await expect(row.getByTestId('transport-notification-rule-status')).toHaveText('Enabled');
    await expect(row.getByTestId('transport-notification-rule-toggle')).toHaveText('Disable');
  });
});
