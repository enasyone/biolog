// Developer tests for BIO-CRD-008-AC05 ("connected through CredentialService ... and the mapped
// end-to-end human workflow passes"). SYNTHETIC: every template created here is throwaway,
// synthetic test data.
//
// Requires TWO servers, the same shape bio-rpt-002-reporting-live.spec.ts established: the
// Next.js app (built with NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE=LIVE baked in at build
// time) AND the real Biolog.Api (.NET) host, reachable through web/next.config.mjs's own
// /api/v1/card-templates/* rewrite. It also requires a VERIFIED tenant domain registered for the
// host the test runs against (BIO-TEN-002's own control plane) - see
// docs/features/bio-crd-008/README.md "Proving AC05" for the exact commands.
//
// Actively excluded from the default `pnpm --dir tests/frontend test:e2e` full-suite run - that
// suite's own server never sets NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE=LIVE, so these ports
// resolve to their SIMULATION mock adapters there. BIOLOG_CRD008_LIVE_TESTS=1 opts in explicitly.
import { expect, test } from '@playwright/test';

test.skip(
  !process.env.BIOLOG_CRD008_LIVE_TESTS,
  'requires BIOLOG_CRD008_LIVE_TESTS=1, a real Biolog.Api backend, and a VERIFIED tenant domain for the test host - see this file\'s own header comment and docs/features/bio-crd-008/README.md "Proving AC05"',
);

test.describe('BIO-CRD-008-AC05 the template lifecycle bar is connected to a real backend, not a placeholder', () => {
  test('creating a draft produces a real tpl_ template id from the live backend', async ({ page }) => {
    await page.goto('/card-designer');
    const bar = page.getByTestId('template-lifecycle-bar');
    await expect(bar.getByText('LIVE')).toBeVisible();

    await bar.getByLabel('Template name').fill('LIVE Staff ID');

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/card-templates') && r.request().method() === 'POST'),
      bar.getByRole('button', { name: 'Create draft from current canvas' }).click(),
    ]);

    expect(response.ok()).toBe(true);
    const body = await response.json();
    expect(body.template_id).toMatch(/^tpl_/);
    expect(body.status).toBe('draft');

    const status = bar.getByRole('status', { name: 'Selected template status' });
    await expect(status.locator('.biolog-status-badge')).toHaveText('draft');
  });

  test('the full lifecycle reaches a real published status via real HTTP round trips', async ({ page }) => {
    await page.goto('/card-designer');
    const bar = page.getByTestId('template-lifecycle-bar');
    await bar.getByLabel('Template name').fill('LIVE full lifecycle');
    await bar.getByRole('button', { name: 'Create draft from current canvas' }).click();

    const status = bar.getByRole('status', { name: 'Selected template status' });
    await expect(status.locator('.biolog-status-badge')).toHaveText('draft');

    await status.getByRole('button', { name: 'Submit for validation' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('validation', { timeout: 10_000 });

    await status.getByRole('button', { name: 'Validation passed' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('approval', { timeout: 10_000 });

    const [publishResponse] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/publish')),
      status.getByRole('button', { name: 'Publish' }).click(),
    ]);
    expect(publishResponse.ok()).toBe(true);
    await expect(status.locator('.biolog-status-badge')).toHaveText('published', { timeout: 10_000 });
  });

  test('AC01: a published template genuinely refuses an edit attempt at the API level (real 409, not simulated)', async ({ page, request }) => {
    await page.goto('/card-designer');
    const bar = page.getByTestId('template-lifecycle-bar');
    await bar.getByLabel('Template name').fill('LIVE immutability check');
    const [created] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/card-templates') && r.request().method() === 'POST'),
      bar.getByRole('button', { name: 'Create draft from current canvas' }).click(),
    ]);
    const template = await created.json();

    const status = bar.getByRole('status', { name: 'Selected template status' });
    await status.getByRole('button', { name: 'Submit for validation' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('validation', { timeout: 10_000 });
    await status.getByRole('button', { name: 'Validation passed' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('approval', { timeout: 10_000 });
    await status.getByRole('button', { name: 'Publish' }).click();
    await expect(status.locator('.biolog-status-badge')).toHaveText('published', { timeout: 10_000 });

    // Real HTTP request against the real backend, bypassing the UI (which offers no edit
    // control once published at all) - this is the genuine AC01 enforcement check, not the UI's
    // own honesty about what buttons it shows.
    const editResponse = await request.put(`/api/v1/card-templates/${template.template_id}/versions/${template.version}`, {
      headers: { 'X-Actor-Id': 'live-e2e', 'Content-Type': 'application/json' },
      data: { snapshot: template.snapshot, expected_revision: 999 },
    });
    expect(editResponse.status()).toBe(409);
    const problem = await editResponse.json();
    expect(problem.code).toBe('card_template_immutable');
  });
});
