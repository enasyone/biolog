// Developer tests for BIO-ATT-003-AC05 ("connected through AttendanceService ... and the mapped
// end-to-end human workflow passes"). SYNTHETIC data only - every id, secret and tenant below
// is a literal test fixture, never a real credential or a real tenant.
//
// TD-WEB-PROVENANCE-BRIDGE-REVERIFY/BIO-ATT-003-F02 (round-1 review, Seyi, High): supersedes the
// prior round's negative-only spec. The builder based this story before merged bridge `7ac4876`
// and documented that authenticated browser writes could not complete - but that dependency had
// already shipped before builder-complete, so the disclosed-boundary pattern no longer applies.
// AC05 requires the mapped human workflow to actually pass: a real signed admin session creates
// a shift through the real backend, and a real signed viewer session (no shift_manage grant) is
// correctly denied the same write.
//
// Requires TWO servers running, the same shape as bio-ten-003-feature-configuration-live.spec.ts:
// the Next.js app (built with NEXT_PUBLIC_FEATURE_MODE_ATTENDANCESERVICE_WORKFORCEATTENDANCE=LIVE
// baked in at build time) AND the real Biolog.Api (.NET) host, reachable through
// web/next.config.mjs's own /api/v1/workforce-attendance/:path* rewrite. Both must share the SAME
// `WorkforceAttendance:ActorProvenanceSecret` value and the SAME
// `WebSessionBridge:TokenSecret`/`WEB_SESSION_TOKEN_SECRET` pair. The API host additionally needs
// `WebSessionBridge:SyntheticTenantId` configured - this triggers both `DemoSessionIdentitySeed`
// (Person+PersonRoleAssignment) AND `BioAtt003Module`'s own RolePermissionGrant seeding to run at
// startup (no tenant-registry seeding needed here - this story resolves tenant ambiently, unlike
// BIO-TEN-003's own explicit-tenant-in-URL design).
//
// Manual run:
//   WorkforceAttendance__ActorProvenanceSecret=this-is-a-synthetic-att003-provenance-secret-32c \
//     WebSessionBridge__TokenSecret=this-is-a-synthetic-web-session-bridge-secret-32chars \
//     WebSessionBridge__SyntheticTenantId=ten_01a113cb-0d05-774b-a463-a954598ed2e9 \
//     cd api/Biolog.Api && dotnet run --urls http://127.0.0.1:5080
//   BIOLOG_API_BASE_URL=http://127.0.0.1:5080 \
//     NEXT_PUBLIC_FEATURE_MODE_ATTENDANCESERVICE_WORKFORCEATTENDANCE=LIVE \
//     WorkforceAttendance__ActorProvenanceSecret=this-is-a-synthetic-att003-provenance-secret-32c \
//     WEB_SESSION_TOKEN_SECRET=this-is-a-synthetic-web-session-bridge-secret-32chars \
//     BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE=true \
//     pnpm --dir web build && pnpm --dir web start -p 3010 -H 127.0.0.1
//   BIOLOG_ATT003_LIVE_TESTS=1 BIOLOG_BASE_URL=http://127.0.0.1:3010 \
//     pnpm --dir tests/frontend exec playwright test bio-att-003-workforce-attendance-live --workers=1
//
// Actively excluded from the default `pnpm --dir tests/frontend test:e2e` full-suite run: that
// suite's own server never sets this story's own LIVE env var, so this story's port resolves to
// its SIMULATION mock adapter there (and the pre-existing `primaryAction`/`flagException` ports
// stay untouched, since they do not share this env var). BIOLOG_ATT003_LIVE_TESTS=1 opts in
// explicitly.
//
// Known manual-run flake (see bio-ten-003-feature-configuration-live.spec.ts's own header for
// the full account): running both servers as plain local processes can intermittently surface an
// empty-body 400 from a Kestrel keep-alive/Next-rewrite-proxy connection-pool artifact, unrelated
// to this story's own logic. `warmUpSession` below polls for the real session cookie rather than
// a generic network-idle heuristic specifically to avoid the OTHER, genuinely-ours cold-start
// race (the data fetch firing before the session Server Action's cookie exists at all).
import { expect, test } from '@playwright/test';

test.skip(
  !process.env.BIOLOG_ATT003_LIVE_TESTS,
  'requires BIOLOG_ATT003_LIVE_TESTS=1 and a real Biolog.Api backend reachable through the LIVE-built Next.js server, with WebSessionBridge:SyntheticTenantId configured - see this file\'s own header comment',
);

async function warmUpSession(page: import('@playwright/test').Page, roleId: string): Promise<void> {
  await page.goto(`/attendance?as=${roleId}`);
  await expect
    .poll(async () => (await page.context().cookies()).some((c) => c.name === 'biolog-session-token'), { timeout: 10_000 })
    .toBe(true);
}

test.describe('BIO-ATT-003-AC05 the workforce shift console completes a real authorized workflow through the merged web-session bridge', () => {
  test('a signed admin session loads shifts and creates a new one through the real backend', async ({ page }) => {
    // Unlike BIO-TEN-003's console, this page never auto-fetches on mount - every load is an
    // explicit "Load shifts" click - so there is no cold-start session/fetch race here at all;
    // warmUpSession still establishes the session cookie first for a cleaner first click.
    await warmUpSession(page, 'admin');

    const [listGet] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/workforce-attendance/shifts') && r.request().method() === 'GET'),
      page.getByRole('button', { name: 'Load shifts' }).click(),
    ]);
    expect(listGet.status()).toBe(200);

    const [createResponse] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/workforce-attendance/shifts') && r.request().method() === 'POST'),
      page.getByRole('button', { name: 'Create shift' }).click(),
    ]);

    // A real authorized write - the shift_manage permission this spec's own setup seeded for
    // the TenantAdmin role - genuinely creates the shift, not a SIMULATION-mode optimistic one.
    expect(createResponse.status()).toBe(201);

    // Confirms the write genuinely persisted server-side via a real follow-up read.
    const [confirmGet] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/workforce-attendance/shifts') && r.request().method() === 'GET'),
      page.getByRole('button', { name: 'Load shifts' }).click(),
    ]);
    expect(confirmGet.status()).toBe(200);
    await expect(page.getByRole('table', { name: 'Workforce shifts' })).toContainText('Day');
  });

  test('a signed viewer session (no shift_manage grant) is correctly denied creating a shift - a real 403, no false success', async ({ page }) => {
    await warmUpSession(page, 'viewer');

    const [createResponse] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/workforce-attendance/shifts') && r.request().method() === 'POST'),
      page.getByRole('button', { name: 'Create shift' }).click(),
    ]);

    expect(createResponse.status()).toBe(403);
    await expect(page.getByText(/could not create the shift/i)).toBeVisible();

    // No silent partial success: a real follow-up read never shows the rejected shift.
    const [listResponse] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/api/v1/workforce-attendance/shifts') && r.request().method() === 'GET'),
      page.getByRole('button', { name: 'Load shifts' }).click(),
    ]);
    expect(listResponse.status()).toBe(200);
  });
});
