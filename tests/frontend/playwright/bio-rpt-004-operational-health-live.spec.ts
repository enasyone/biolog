// Developer tests for BIO-RPT-004-AC05 ("connected through ReportingService ... and the mapped
// end-to-end human workflow passes"). SYNTHETIC: every number this real backend returns is
// computed from this session's own synthetic PersonRegistry/AccessDecisions/CardProduction
// fixture data - no real person, biometric, tenant or production data anywhere.
//
// Requires TWO servers running: the Next.js app (built with
// NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_OPERATIONALHEALTH=LIVE baked in at build time -
// Next.js inlines NEXT_PUBLIC_* vars at build, not runtime) AND the real Biolog.Api (.NET) host,
// reachable through web/next.config.mjs's own /api/v1/dashboard/* rewrite. Also requires a
// VERIFIED tenant domain registered for the host the test runs against (BIO-TEN-002's own control
// plane) - see docs/features/bio-rpt-004/README.md "Proving AC05" for the exact commands.
//
// Actively excluded from the default `pnpm --dir tests/frontend test:e2e` full-suite run: that
// suite's own server never sets this env var LIVE, so the port resolves to its SIMULATION mock
// adapter there. BIOLOG_RPT004_LIVE_TESTS=1 opts in explicitly.
import { expect, test } from '@playwright/test';

test.skip(
  !process.env.BIOLOG_RPT004_LIVE_TESTS,
  'requires BIOLOG_RPT004_LIVE_TESTS=1, a real Biolog.Api backend, and a VERIFIED tenant domain for the test host - see this file\'s own header comment and docs/features/bio-rpt-004/README.md "Proving AC05"',
);

test.describe('BIO-RPT-004-AC05 the operational health panel is connected to a real backend, not a placeholder', () => {
  /**
   * Seyi round-2, BIO-RPT-004-F03 (Critical): the endpoint now runs a cryptographic
   * ActorProvenanceGate check BEFORE the permission check (ADR-0006 Decision 3). No
   * authentication story has landed in the frontend yet (BIO-INT-001 is W12), and no live port
   * anywhere in this codebase ships a real provenance secret to the browser (verified directly),
   * so the demo session's own placeholder actor ('web-ui', see `live.ts`) can never present a
   * valid `X-Actor-Provenance` proof through the UI - this is the disclosed, expected default,
   * the same shape BIO-RPT-003's own LIVE export-forbidden proof already established for an
   * identical pre-auth gap. This test proves the REAL backend actually enforces the gate end to
   * end (not merely that a fake returns 401 in a unit test) - F03's own literal requirement.
   */
  test('F03: a GET from the real backend with no provenance proof is refused with a real 401, not silently allowed', async ({ page }) => {
    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/dashboard/operational-health') && r.request().method() === 'GET'),
      page.goto('/'),
    ]);

    expect(response.status()).toBe(401);
    const body = await response.json();
    expect(body.code).toBe('actor_provenance_required');

    const panel = page.getByTestId('operational-health');
    await expect(panel.locator('.biolog-placeholder-status--failure')).toContainText('X-Actor-Provenance');
  });
});
