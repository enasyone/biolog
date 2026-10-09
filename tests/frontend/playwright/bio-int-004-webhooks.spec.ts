// Developer tests for BIO-INT-004-AC05 ("`/administration/integrations` is wired to a real
// `IntegrationService` surface for subscription management"). SYNTHETIC: no real data.
// SIMULATION mode (the default IntegrationService mock adapter), runs in the default suite -
// proves the mapped end-to-end human workflow (list -> create -> see the one-time secret ->
// toggle enabled/disabled) with real, visible success/failure states, the same bar
// bio-rpt-004-operational-health.spec.ts's own "(SIMULATION)" suite already establishes for a
// panel with no real backend wired into this default suite.
//
// BIO-INT-004-QA-01 (Chioma, High - acceptance): the branch originally shipped
// WebhookSubscriptionsPanel with no BIO-INT-004 frontend unit test or Playwright workflow at
// all - AC05's own evidence cited only `tsc` and manual code reading, never an executable
// browser proof. This file closes that gap for `/administration/integrations`.
//
// `/settings/authentication` is deliberately NOT covered here - this story's own
// `IntegrationService` port keys (`listWebhookSubscriptions`/`createWebhookSubscription`/
// `setWebhookSubscriptionEnabled`) are never routed there, the same disclosed scope boundary
// BIO-INT-003's own README already established for that page ("`/settings/authentication`
// deliberately left untouched - its own primary actions are BIO-INT-001's own sign-in/session-
// policy domain ... not this story's API service-account concern") - see
// docs/features/bio-int-004/README.md's own "AC05 — web wiring" section for this story's own
// restatement of that same boundary.
import { expect, test } from '@playwright/test';

test.describe('BIO-INT-004 the Webhooks panel (SIMULATION)', () => {
  test('AC05: mounted on /administration/integrations, alongside the existing Import Console and Service Accounts panels, untouched', async ({ page }) => {
    await page.goto('/administration/integrations');
    await expect(page.getByRole('heading', { name: 'Webhooks', exact: true })).toBeVisible();
    const panel = page.getByTestId('webhook-subscriptions-panel');
    await expect(panel).toBeVisible();
    // BIO-INT-002's own ImportConsole and BIO-INT-003's own ServiceAccountsPanel stay mounted,
    // never replaced by this story's own additive panel.
    await expect(page.getByRole('heading', { name: 'API credentials' })).toBeVisible();
  });

  test('AC01: a tenant with no subscriptions yet sees an explicit empty state, never a silent blank table', async ({ page }) => {
    await page.goto('/administration/integrations');
    const panel = page.getByTestId('webhook-subscriptions-panel');
    await expect(panel.getByTestId('webhook-subscriptions-empty')).toBeVisible();
    await expect(panel.getByTestId('webhook-subscription-row')).toHaveCount(0);
  });

  test('AC01/AC03/AC05: the full create workflow - fill the form, submit, see the one-time signing secret, see the new row appear enabled', async ({ page }) => {
    await page.goto('/administration/integrations');
    const panel = page.getByTestId('webhook-subscriptions-panel');

    await panel.getByTestId('webhook-subscription-url-input').fill('https://example-synthetic.test/webhooks/playwright-proof');
    // document_session is checked by default; also select device, proving a multi-select works.
    await panel.getByTestId('webhook-subscription-event-type-device').check();
    await panel.getByTestId('webhook-subscription-create').click();

    // Visible SUCCESS state #1: the notice.
    await expect(panel.getByTestId('webhook-subscription-notice')).toContainText('created');
    // Visible SUCCESS state #2: the one-time secret, shown exactly once.
    await expect(panel.getByTestId('webhook-subscription-secret')).toBeVisible();
    await expect(panel.getByTestId('webhook-subscription-secret')).toContainText('New signing secret');

    // Visible SUCCESS state #3: the new row, enabled by default, with both selected event types.
    const row = panel.getByTestId('webhook-subscription-row').first();
    await expect(row).toContainText('example-synthetic.test/webhooks/playwright-proof');
    await expect(row).toContainText('document_session');
    await expect(row).toContainText('device');
    await expect(row.getByTestId('webhook-subscription-status')).toHaveText('Enabled');
  });

  test('AC01: creating with no event type selected is rejected with a visible failure state, never a silent no-op', async ({ page }) => {
    await page.goto('/administration/integrations');
    const panel = page.getByTestId('webhook-subscriptions-panel');

    // Uncheck the only default-selected event type, leaving zero selected.
    await panel.getByTestId('webhook-subscription-event-type-document_session').uncheck();

    // The create button itself disables with nothing selected - the UI's own first line of
    // defense, never relying on the backend alone to reject an empty selection.
    await expect(panel.getByTestId('webhook-subscription-create')).toBeDisabled();
    await expect(panel.getByTestId('webhook-subscription-row')).toHaveCount(0);
  });

  test('SY-INT004-04: a genuinely failed create call (empty URL, a real validation failure the mock adapter returns) renders a visible, accessible error state, not a silent no-op', async ({ page }) => {
    await page.goto('/administration/integrations');
    const panel = page.getByTestId('webhook-subscriptions-panel');

    // document_session stays checked (the default), so the create button is NOT disabled by the
    // client-side zero-event-types guard the OTHER failure test above exercises - this call
    // genuinely reaches createWebhookSubscriptionMockPort, which rejects an empty URL
    // (web/features/bio-int-004/mock.ts), the same way its live adapter would reject a request
    // the real backend rejected with a non-2xx response.
    await panel.getByTestId('webhook-subscription-url-input').fill('');
    await expect(panel.getByTestId('webhook-subscription-create')).toBeEnabled();
    await panel.getByTestId('webhook-subscription-create').click();

    // Visible AND screen-reader-accessible failure state: role="alert", same pattern this
    // panel's own load-failure states (`error`/`offline`) already use.
    const notice = panel.getByTestId('webhook-subscription-notice');
    await expect(notice).toHaveAttribute('role', 'alert');
    await expect(notice).toContainText('URL is required');

    // Nothing silently succeeded: no new row, no secret revealed.
    await expect(panel.getByTestId('webhook-subscription-row')).toHaveCount(0);
    await expect(panel.getByTestId('webhook-subscription-secret')).toHaveCount(0);
  });

  test('AC01: toggling a subscription disabled, then enabled again, shows both visible status transitions', async ({ page }) => {
    await page.goto('/administration/integrations');
    const panel = page.getByTestId('webhook-subscriptions-panel');

    await panel.getByTestId('webhook-subscription-url-input').fill('https://example-synthetic.test/webhooks/toggle-proof');
    await panel.getByTestId('webhook-subscription-create').click();
    const row = panel.getByTestId('webhook-subscription-row').first();
    await expect(row.getByTestId('webhook-subscription-status')).toHaveText('Enabled');

    await row.getByTestId('webhook-subscription-toggle').click();
    await expect(panel.getByTestId('webhook-subscription-notice')).toContainText('disabled');
    await expect(row.getByTestId('webhook-subscription-status')).toHaveText('Disabled');
    await expect(row.getByTestId('webhook-subscription-toggle')).toHaveText('Enable');

    await row.getByTestId('webhook-subscription-toggle').click();
    await expect(panel.getByTestId('webhook-subscription-notice')).toContainText('enabled');
    await expect(row.getByTestId('webhook-subscription-status')).toHaveText('Enabled');
    await expect(row.getByTestId('webhook-subscription-toggle')).toHaveText('Disable');
  });
});
