// Developer tests for TD-WEB-PROVENANCE-BRIDGE round 2 re-review (ZB-WEBPROV-02, Critical).
// SYNTHETIC: every value below is a literal test fixture, never a real credential.
//
// Runs in the DEFAULT test suite, no opt-in flag, no backend required - unlike
// td-web-provenance-bridge-live.spec.ts, this proves a purely web-side property: with
// BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE unset (this repo's actual default, and the correct default
// for every environment that is not an explicitly-sanctioned demo deployment), startDemoSession
// never mints a session token at all, regardless of which role/tenant the caller asks for -
// including 'admin', the exact privilege ZB-WEBPROV-02 showed could otherwise be obtained just
// by asking. If no session token is ever issued, proxy.ts's own fail-closed verification means
// no gated route can ever receive a real X-Actor-Provenance proof as a consequence - this is
// the adversarial proof Zainab's round-2 review asked for, driven through the real Server
// Action boundary (role selector + ?as=), not a direct library-level import bypassing it.
import { expect, test } from '@playwright/test';

const SESSION_TOKEN_COOKIE = 'biolog-session-token';

test.describe('TD-WEB-PROVENANCE-BRIDGE issuance gate (ZB-WEBPROV-02)', () => {
  test('visiting with ?as=admin never mints a session token when demo issuance is not explicitly enabled', async ({ page }) => {
    await page.goto('/?as=admin');
    await page.waitForLoadState('networkidle');
    const cookies = await page.context().cookies();
    expect(cookies.find((c) => c.name === SESSION_TOKEN_COOKIE)).toBeUndefined();
  });

  test('switching the role selector to System Administrator never mints a session token either', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    await page.getByRole('combobox', { name: 'Role' }).selectOption({ label: 'System Administrator' });
    await page.waitForTimeout(500);

    const cookies = await page.context().cookies();
    expect(cookies.find((c) => c.name === SESSION_TOKEN_COOKIE)).toBeUndefined();
  });
});
