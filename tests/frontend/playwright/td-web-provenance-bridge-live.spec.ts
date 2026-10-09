// Developer tests for TD-WEB-PROVENANCE-BRIDGE round 2 (ZB-WEBPROV-01, Critical). SYNTHETIC:
// every value below (secrets, session tokens, actor/tenant ids) is a literal test fixture,
// never a real credential.
//
// Requires TWO servers running, same shape as bio-doc-010-identify-live.spec.ts: the Next.js
// app AND the real Biolog.Api (.NET) host, reachable through web/next.config.mjs's own
// /api/v1/* rewrite. Both must share the SAME `ApiSecurity:ActorProvenanceSecret` value (the
// shared-secret HMAC the backend's ActorProvenanceGate verifies against) - the Next.js side
// additionally needs its own `WEB_SESSION_TOKEN_SECRET` (web-only, never touches the .NET
// side at all).
//
// Actively excluded from the default `pnpm --dir tests/frontend test:e2e` full-suite run
// (same BIO-DOC-009-F01-adjacent lesson as bio-doc-010's own live spec: a comment alone is
// not enforcement). BIOLOG_TDWEBPROV_LIVE_TESTS=1 opts in explicitly.
//
// Manual run:
//   ApiSecurity__ActorProvenanceSecret=this-is-a-synthetic-shared-secret-32chars-plus \
//     cd api/Biolog.Api && dotnet run --urls http://127.0.0.1:5080
//   BIOLOG_API_BASE_URL=http://127.0.0.1:5080 \
//     ApiSecurity__ActorProvenanceSecret=this-is-a-synthetic-shared-secret-32chars-plus \
//     WEB_SESSION_TOKEN_SECRET=this-is-a-synthetic-session-secret-32chars-plus \
//     pnpm --dir web dev -p 3010 -H 127.0.0.1
//   BIOLOG_TDWEBPROV_LIVE_TESTS=1 BIOLOG_BASE_URL=http://127.0.0.1:3010 \
//     WEB_SESSION_TOKEN_SECRET=this-is-a-synthetic-session-secret-32chars-plus \
//     BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE=true \
//     pnpm --dir tests/frontend exec playwright test td-web-provenance-bridge-live --workers=1
//
// BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE=true is needed in the PLAYWRIGHT PROCESS's own
// environment (not just the dev server's) - this spec's issueTestSessionToken() imports and
// calls issueSessionToken directly inside the test runner, bypassing session-actions.ts's own
// Server Action entirely (ZB-WEBPROV-02's gate now lives in issueSessionToken itself, so it
// applies here too - this spec would otherwise mint nothing and every "positive" test below
// would fail for the wrong reason).
//
// Target endpoint: POST /api/v1/integrations/service-accounts (BIO-INT-003's own
// create-service-account route). Its handler resolves the actor (ActorResolver +
// ActorProvenanceGate) FIRST, then the ambient tenant (ITenantContextAccessor, the
// `host`-channel-only resolver - TenantContextMiddleware.cs), THEN checks the real permission
// grant. A raw test host (no TenantDomainRegistry entry) never resolves to a real tenant -
// this is exactly the disclosed "no tenant identity reaches the backend's existing resolver"
// gap already escalated to god/Kemi during this TD's round-2 scope ruling, not a new problem
// this spec introduces or needs to solve. So the positive case below does NOT reach a
// permission check (403) - it reaches `tenant_context_missing` (400), the SAME raw status
// code every negative case also gets. What distinguishes them is the Problem response's own
// `title`: every negative case fails at `actor_required` (actor/provenance never verified at
// all), while the genuine-session positive case fails one layer further in, at
// `tenant_context_missing` (actor AND provenance both verified successfully; only the
// separate, already-disclosed tenant-channel gap blocks it next) - proof that the signed
// session's provenance proof is genuinely being verified, not skipped, without overclaiming a
// permission-check-level proof this test's own setup cannot honestly produce.
import { expect, test } from '@playwright/test';

test.skip(
  !process.env.BIOLOG_TDWEBPROV_LIVE_TESTS,
  'requires BIOLOG_TDWEBPROV_LIVE_TESTS=1 and a real Biolog.Api backend - see this file\'s own header comment',
);

const SESSION_TOKEN_COOKIE = 'biolog-session-token';
const CREATE_SERVICE_ACCOUNT_PATH = '/api/v1/integrations/service-accounts';

async function issueTestSessionToken(roleId: 'admin' | 'staff' | 'viewer', tenantId: 'alpha' | 'beta'): Promise<string> {
  // Imports the real production module directly - this test runs in Node (Playwright's test
  // runner process, not a browser page), so a server-only TS module is just as importable here
  // as any other Node module. Requires WEB_SESSION_TOKEN_SECRET to already be set in THIS
  // process's own environment too (the same value the Next.js dev server was started with -
  // see this file's own header comment), so the token this test mints verifies successfully
  // against the live proxy.
  const { issueSessionToken } = await import('../../../web/lib/session-token');
  const token = await issueSessionToken({ roleId, tenantId });
  if (!token) {
    throw new Error('WEB_SESSION_TOKEN_SECRET and BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE=true must both be set in the Playwright process environment to run this spec');
  }
  return token;
}

async function problemTitle(response: Awaited<ReturnType<import('@playwright/test').APIRequestContext['post']>>): Promise<string> {
  const body = (await response.json()) as { title?: string };
  return body.title ?? '';
}

test.describe('TD-WEB-PROVENANCE-BRIDGE round 2 - proxy.ts validates a real signed session end to end', () => {
  test('a request with no session cookie at all never reaches actor resolution (actor_required)', async ({ request }) => {
    const response = await request.post(CREATE_SERVICE_ACCOUNT_PATH, {
      data: { displayName: 'No session', scopes: ['ReadOnly'] },
    });
    expect(response.status()).toBe(400);
    expect(await problemTitle(response)).toBe('actor_required');
  });

  test('a forged legacy biolog-demo-role=admin cookie (round 1\'s own vulnerability) is ignored entirely - proxy.ts no longer reads it at all (actor_required)', async ({ request }) => {
    const response = await request.post(CREATE_SERVICE_ACCOUNT_PATH, {
      headers: { Cookie: 'biolog-demo-role=admin' },
      data: { displayName: 'Forged legacy cookie', scopes: ['ReadOnly'] },
    });
    expect(response.status()).toBe(400);
    expect(await problemTitle(response)).toBe('actor_required');
  });

  test('a caller-supplied X-Actor-Id/X-Actor-Provenance pair is stripped and replaced, never honored directly (actor_required)', async ({ request }) => {
    const response = await request.post(CREATE_SERVICE_ACCOUNT_PATH, {
      headers: { 'X-Actor-Id': 'forged-admin', 'X-Actor-Provenance': 'deadbeef'.repeat(8) },
      data: { displayName: 'Forged headers', scopes: ['ReadOnly'] },
    });
    expect(response.status()).toBe(400);
    expect(await problemTitle(response)).toBe('actor_required');
  });

  test('a garbage session cookie (not a real signed token) is rejected the same way a missing one is (actor_required)', async ({ request }) => {
    const response = await request.post(CREATE_SERVICE_ACCOUNT_PATH, {
      headers: { Cookie: `${SESSION_TOKEN_COOKIE}=not-a-real-token-at-all` },
      data: { displayName: 'Garbage token', scopes: ['ReadOnly'] },
    });
    expect(response.status()).toBe(400);
    expect(await problemTitle(response)).toBe('actor_required');
  });

  test('a genuinely signed, valid session passes actor+provenance verification and fails one layer further in (tenant_context_missing), never actor_required - proof the proxy\'s proof is genuinely verified, not skipped', async ({ request }) => {
    const token = await issueTestSessionToken('admin', 'alpha');
    const response = await request.post(CREATE_SERVICE_ACCOUNT_PATH, {
      headers: { Cookie: `${SESSION_TOKEN_COOKIE}=${token}` },
      data: { displayName: 'Genuine session', scopes: ['ReadOnly'] },
    });
    expect(response.status()).toBe(400);
    expect(await problemTitle(response)).toBe('tenant_context_missing');
  });

  test('a revoked session cannot be replayed - a stolen pre-sign-out cookie value is rejected exactly like no session at all (actor_required), not merely like an unauthorised one', async ({ request }) => {
    // Regression coverage for the real bug Damilola found by hand-driving the actual
    // sign-out -> replay flow against a live dev server: the original in-memory revocation
    // Set was never actually shared between proxy.ts and the issuance/sign-out Server Action
    // (separate Turbopack module graphs), so a stolen cookie kept working after sign-out.
    // Fixed via the file-backed ./lib/session-revocation-store.ts; this proves the fix through
    // the real HTTP layer, not just that store's own unit tests.
    const { issueSessionToken, verifySessionToken, revokeSession } = await import('../../../web/lib/session-token');
    const token = await issueSessionToken({ roleId: 'admin', tenantId: 'alpha' });
    if (!token) {
      throw new Error('WEB_SESSION_TOKEN_SECRET and BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE=true must both be set in the Playwright process environment to run this spec');
    }

    // Prove the token is genuinely live before revoking it - same proof shape as the test above.
    const beforeRevocation = await request.post(CREATE_SERVICE_ACCOUNT_PATH, {
      headers: { Cookie: `${SESSION_TOKEN_COOKIE}=${token}` },
      data: { displayName: 'Before revocation', scopes: ['ReadOnly'] },
    });
    expect(await problemTitle(beforeRevocation)).toBe('tenant_context_missing');

    // Reuse the real, already-tested verification path to get the sid (rather than
    // hand-duplicating the token's own decode logic here), then revoke it - simulating real sign-out.
    const verification = await verifySessionToken(token);
    if (!verification.valid) {
      throw new Error('the token minted above should still be valid at this point in the test');
    }
    await revokeSession(verification.claims.sid);

    // Replay the EXACT stolen pre-revocation cookie value directly.
    const afterRevocation = await request.post(CREATE_SERVICE_ACCOUNT_PATH, {
      headers: { Cookie: `${SESSION_TOKEN_COOKIE}=${token}` },
      data: { displayName: 'Replayed after revocation', scopes: ['ReadOnly'] },
    });
    expect(afterRevocation.status()).toBe(400);
    expect(await problemTitle(afterRevocation)).toBe('actor_required');
  });
});
