// Developer tests for BIO-BIO-009-AC05 ("connected through BiometricService ... and the mapped
// end-to-end human workflow passes"). SYNTHETIC data only.
//
// Round 4 disclosed HYBRID boundary (not this story's bug - see
// docs/features/bio-bio-009/README.md "Round 4: disclosed HYBRID boundary on the LIVE path" for
// the full account, and RetentionSection.tsx's own header comment). `web/features/biometrics/live.ts`'s
// `callLive` sends NO actor identity at all - no X-Actor-Id, no X-Actor-Provenance - so the real
// backend's `ActorResolver.Resolve` rejects every LIVE call with 400 `actor_required` before
// `ActorProvenanceGate` is ever reached. No mechanism exists anywhere in this codebase yet for a
// pre-authentication web page to produce a valid actor identity/provenance pair without shipping
// a shared secret to the browser - the exact same cross-cutting gap Tunde hit on BIO-TEN-003,
// escalated as TD-WEB-PROVENANCE-BRIDGE. This file proves the HONEST claim available today - the
// live backend is genuinely connected and correctly rejects an unauthenticated browser call -
// rather than faking a positive write-path pass the current security posture cannot deliver.
//
// Requires TWO servers running, the same shape as
// bio-ten-003-feature-configuration-live.spec.ts/bio-doc-009-authenticity-live.spec.ts: the
// Next.js app (started with NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE_MANAGERETENTIONPOLICY=LIVE
// and NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE_DELETEBIOMETRICRECORD=LIVE in its environment)
// AND the real Biolog.Api (.NET) host, reachable through web/next.config.mjs's own
// /api/v1/biometrics/retention/policy and /delete rewrites (added in round 3).
//
// A separate, pre-existing fact this suite's `beforeAll` works around rather than disclosing as
// part of THIS story's own boundary: a fresh Biolog.Api host starts with NO tenant domain
// registered at all (BIO-TEN-002's own control-plane registry starts empty), so EVERY tenant-
// scoped endpoint - not just this story's - 400s with `tenant_context_missing` before reaching
// any story's own security gate. `beforeAll` registers and verifies one real tenant domain for
// `127.0.0.1` through BIO-TEN-002's own real control-plane API (genuine test setup, the same
// shape any deployment would need to do once per environment - never a shortcut around this
// story's own security gate, which still runs in full afterward).
//
// Actively excluded from the default `pnpm --dir tests/frontend test:e2e` full-suite run: that
// suite's own server never sets these two env vars, so this story's two ports resolve to their
// SIMULATION mock adapter there, which would never produce the real backend's 400 this file
// asserts on. BIOLOG_BIO009_LIVE_TESTS=1 opts in explicitly.
import { expect, test } from '@playwright/test';

const apiBaseUrl = process.env.BIOLOG_API_BASE_URL ?? 'http://127.0.0.1:5080';
const setupActorHeader = { 'X-Actor-Id': 'bio-bio-009-live-test-setup' };

test.skip(
  !process.env.BIOLOG_BIO009_LIVE_TESTS,
  'requires BIOLOG_BIO009_LIVE_TESTS=1 and a real Biolog.Api backend reachable through the LIVE-built Next.js server - see this file\'s own header comment and docs/features/bio-bio-009/README.md "Round 4: disclosed HYBRID boundary on the LIVE path"',
);

test.beforeAll(async ({ request }) => {
  const registerResponse = await request.post(`${apiBaseUrl}/api/v1/control-plane/tenant-domains`, {
    headers: setupActorHeader,
    data: { hostname: '127.0.0.1', tenant_id: 'ten_01926a3c-0000-7000-8000-000000000001' },
    failOnStatusCode: false,
  });

  const domain = registerResponse.status() === 201
    ? await registerResponse.json()
    : await (await request.get(`${apiBaseUrl}/api/v1/control-plane/tenant-domains/127.0.0.1`)).json();

  if (domain.status !== 'VERIFIED') {
    await request.put(`${apiBaseUrl}/api/v1/control-plane/tenant-domains/127.0.0.1/status`, {
      headers: setupActorHeader,
      data: { version: domain.version, status: 'VERIFIED' },
    });
  }
});

test.describe('BIO-BIO-009-AC05 the privacy & retention controls are connected to a real backend, which correctly rejects an unauthenticated browser call', () => {
  test('updating the retention policy against the live backend is rejected with 400 (no actor identity sent), surfaced as a visible error', async ({ page }) => {
    await page.goto('/fingerprints');
    const section = page.locator('section', { has: page.getByRole('heading', { name: 'Privacy & retention' }) });

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/biometrics/retention/policy') && r.request().method() === 'POST'),
      section.getByRole('button', { name: 'Update retention policy' }).click(),
    ]);

    // The real backend, not a mock: a genuine 400 from ActorResolver, because the browser
    // sends no X-Actor-Id at all (never mind a provenance proof).
    expect(response.status()).toBe(400);
    const body = await response.json();
    expect(body.title).toBe('actor_required');

    // The component's own real failure path, not a silently-succeeding SIMULATION fallback.
    await expect(section.getByRole('alert')).toBeVisible();
    await expect(section.getByTestId('policy-result')).toHaveCount(0);
  });

  test('requesting deletion against the live backend is rejected with 400 (no actor identity sent), surfaced as a visible error', async ({ page }) => {
    await page.goto('/fingerprints');
    const section = page.locator('section', { has: page.getByRole('heading', { name: 'Privacy & retention' }) });
    await section.getByLabel('Record id').fill('sess-live-demo');

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/biometrics/retention/delete') && r.request().method() === 'POST'),
      section.getByRole('button', { name: 'Request deletion' }).click(),
    ]);

    expect(response.status()).toBe(400);
    const body = await response.json();
    expect(body.title).toBe('actor_required');
    await expect(section.getByRole('alert')).toBeVisible();
    await expect(section.getByTestId('deletion-result')).toHaveCount(0);
  });
});
