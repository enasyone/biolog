// Developer tests for BIO-DOC-010-AC05 ("connected through DocumentVerifyService ... and the
// mapped end-to-end human workflow passes"). SYNTHETIC: every scenario this real backend computes
// is built from synthetic seed data (see api/features/bio-doc-010's own NigeriaDocumentPackV1.cs)
// - no real document or biometric data anywhere.
//
// Unlike every other bio-ux-009-*.spec.ts test, this spec requires TWO servers running: the
// Next.js app (built with NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_DOCUMENTIDENTIFICATION=LIVE
// baked in at build time - Next.js inlines NEXT_PUBLIC_* vars at build, not runtime) AND the real
// Biolog.Api (.NET) host, reachable through web/next.config.mjs's own /api/v1/documents/* rewrite.
// `capture` deliberately stays on its default SIMULATION mock (no env var needed for it) - see
// `web/lib/feature-mode.ts`'s own `MODULE_PORT_KEYS` comment for why `documentIdentification` is
// its own independent port key rather than sharing `primaryAction`'s mode. See
// docs/features/bio-doc-010/NIGERIA-DOCUMENT-PACK.md "Proving AC05" for the exact manual
// verification commands.
//
// Actively excluded from the default `pnpm --dir tests/frontend test:e2e` full-suite run, not
// just documented as excluded (the same BIO-DOC-009-F01-adjacent lesson: a comment alone is not
// enforcement - see that story's own spec header). BIOLOG_DOC010_LIVE_TESTS=1 opts in explicitly.
//
// Must run with a single Playwright worker (`--workers=1`): the real backend's demo scenario
// cycle is a single shared, deterministic, global counter (by design - see
// DocumentTemplatePackService.cs's own remarks). This file's own tests rely on exactly three
// CONSECUTIVE calls forming one full cycle, robust to whatever phase another test earlier in this
// same file left the counter at.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

test.skip(
  !process.env.BIOLOG_DOC010_LIVE_TESTS,
  'requires BIOLOG_DOC010_LIVE_TESTS=1 and a real Biolog.Api backend reachable through the LIVE-built Next.js server - see this file\'s own header comment and docs/features/bio-doc-010/NIGERIA-DOCUMENT-PACK.md "Proving AC05"',
);

async function clickUntilBadge(page: Page, testId: string, targetLabel: string, maxAttempts = 8): Promise<void> {
  const card = page.getByTestId(testId);
  const button = card.getByRole('button');
  for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
    await button.click();
    await expect(card.getByRole('status')).toBeVisible();
    const label = await card.getByTestId('evidence-state-badge').textContent();
    if (label === targetLabel) return;
  }
  throw new Error(`${testId}: never reached badge "${targetLabel}" within ${maxAttempts} attempts`);
}

async function captureSuccessfully(page: Page): Promise<void> {
  await clickUntilBadge(page, 'step-capture', 'Read');
  await expect(page.getByTestId('step-document-identification').getByRole('button')).toBeEnabled();
}

/** Clicks the document-identification step's button once and returns its resulting state badge text. */
async function runIdentifyOnce(page: Page): Promise<string> {
  const card = page.getByTestId('step-document-identification');
  await card.getByRole('button').click();
  await expect(card.getByRole('status')).toBeVisible();
  return (await card.getByTestId('evidence-state-badge').textContent()) ?? '';
}

test.describe('BIO-DOC-010-AC05 the document-identification step is connected to a real governed-pack backend, not a placeholder', () => {
  test('three consecutive live calls cover all three real computed verification states (consistent, failed, inconclusive) — proof this is a real governed pack, not a canned response', async ({
    page,
  }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);

    const seen = new Set<string>();
    for (let i = 0; i < 3; i += 1) {
      seen.add(await runIdentifyOnce(page));
    }

    // Exactly one full period of the backend's own three-scenario deterministic cycle - see this
    // file's own header comment for why three CONSECUTIVE calls guarantee this regardless of the
    // cycle's current phase.
    expect([...seen].sort()).toEqual(['Consistent', 'Failed', 'Inconclusive']);
  });

  test('the live response is genuinely computed server-side, carrying the governed pack/entry it verified against (AC04), not just a bare state string', async ({
    page,
  }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/documents/identify') && r.request().method() === 'POST'),
      page.getByTestId('step-document-identification').getByRole('button').click(),
    ]);

    expect(response.ok()).toBe(true);
    const body = await response.json();
    expect(body).toHaveProperty('pack_id', 'nigeria-document-pack');
    expect(body).toHaveProperty('pack_version', 'nigeria-document-pack-v1');
    expect(body).toHaveProperty('entry_id');
    expect(body).toHaveProperty('issuer');
    expect(body).toHaveProperty('record_id');
  });

  test('the evidence-comparison table reflects the live identification result, same as any other step', async ({ page }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);
    await runIdentifyOnce(page);

    const table = page.getByTestId('evidence-comparison-table');
    const row = table.locator('tr', { hasText: 'Document Identification' });
    await expect(row).not.toContainText('Not checked');
  });

  test('has no axe violations after a live identification result is shown', async ({ page }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);
    await runIdentifyOnce(page);
    await runIdentifyOnce(page);
    await runIdentifyOnce(page);

    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
