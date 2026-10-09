// Developer tests for BIO-DOC-009-AC06 ("connected through DocumentVerifyService ... and the
// mapped end-to-end human workflow passes"). SYNTHETIC: every scenario this real backend computes
// is built from synthetic fixture values (see api/features/bio-doc-009's own
// SyntheticAuthenticityFixtures.cs) - no real document or biometric data anywhere.
//
// Unlike every other bio-ux-009-*.spec.ts test, this spec requires TWO servers running: the
// Next.js app (built with NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_AUTHENTICITY=LIVE baked
// in at build time - Next.js inlines NEXT_PUBLIC_* vars at build, not runtime) AND the real
// Biolog.Api (.NET) host, reachable through web/next.config.mjs's own /api/v1/documents/* rewrite.
// See docs/features/bio-doc-009/AUTHENTICITY-ORCHESTRATOR.md "Proving AC06" for the exact manual
// verification commands.
//
// Actively excluded from the default `pnpm --dir tests/frontend test:e2e` full-suite run, not
// just documented as excluded: that suite's own server never sets
// NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_AUTHENTICITY=LIVE, so `checkAuthenticity` resolves
// to the SIMULATION mock adapter there - this file's own assertions (real computed dispositions,
// real AC04 version fields) would then fail against mock data, not skip cleanly, without the guard
// below (caught by running `scripts/quality.sh --full` and finding exactly that failure - fixed
// here rather than left for a reviewer to hit). BIOLOG_DOC009_LIVE_TESTS=1 opts in explicitly.
//
// Must run with a single Playwright worker (`--workers=1`): the real backend's demo scenario
// cycle is a single shared, deterministic, global counter (by design - see
// AuthenticityAssessmentService.cs's own remarks) - concurrent workers would interleave calls
// unpredictably. This file's own tests rely on exactly three CONSECUTIVE calls forming one full
// cycle, robust to whatever phase another test earlier in this same file left the counter at.
import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

test.skip(
  !process.env.BIOLOG_DOC009_LIVE_TESTS,
  'requires BIOLOG_DOC009_LIVE_TESTS=1 and a real Biolog.Api backend reachable through the LIVE-built Next.js server - see this file\'s own header comment and docs/features/bio-doc-009/AUTHENTICITY-ORCHESTRATOR.md "Proving AC06"',
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
  await expect(page.getByTestId('step-authenticity').getByRole('button')).toBeEnabled();
}

/** Clicks the authenticity step's button once and returns its resulting state badge text. */
async function runAuthenticityOnce(page: Page): Promise<string> {
  const card = page.getByTestId('step-authenticity');
  await card.getByRole('button').click();
  await expect(card.getByRole('status')).toBeVisible();
  return (await card.getByTestId('evidence-state-badge').textContent()) ?? '';
}

test.describe('BIO-DOC-009-AC06 the authenticity step is connected to a real backend, not a placeholder', () => {
  test('three consecutive live calls cover all three real computed dispositions (authentic, inconclusive, not authentic) — proof this is a real orchestrator, not a canned response', async ({
    page,
  }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);

    const seen = new Set<string>();
    for (let i = 0; i < 3; i += 1) {
      seen.add(await runAuthenticityOnce(page));
    }

    // Exactly one full period of the backend's own three-scenario deterministic cycle - see
    // this file's own header comment for why three CONSECUTIVE calls guarantee this regardless
    // of the cycle's current phase.
    expect([...seen].sort()).toEqual(['Authentic', 'Failed', 'Inconclusive']);
  });

  test('the live response is genuinely computed server-side, carrying real evidence/ruleset version fields (AC04), not just a bare state string', async ({
    page,
  }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/documents/authenticity') && r.request().method() === 'POST'),
      page.getByTestId('step-authenticity').getByRole('button').click(),
    ]);

    expect(response.ok()).toBe(true);
    const body = await response.json();
    expect(body).toHaveProperty('checks');
    expect(Array.isArray(body.checks)).toBe(true);
    expect(body.checks.length).toBe(6);
    expect(body).toHaveProperty('ruleset_version');
    expect(body).toHaveProperty('evidence_version');
    expect(body).toHaveProperty('audit_event_id');
  });

  test('the evidence-comparison table reflects the live authenticity result, same as any other step', async ({ page }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);
    await runAuthenticityOnce(page);

    const table = page.getByTestId('evidence-comparison-table');
    const row = table.locator('tr', { hasText: 'Authenticity / Evidence Comparison' });
    await expect(row).not.toContainText('Not checked');
  });

  test('has no axe violations after a live authenticity result is shown', async ({ page }) => {
    await page.goto('/document-verify');
    await captureSuccessfully(page);
    await runAuthenticityOnce(page);
    await runAuthenticityOnce(page);
    await runAuthenticityOnce(page);

    const results = await new AxeBuilder({ page }).analyze();
    expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
  });
});
