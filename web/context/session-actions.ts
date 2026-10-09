'use server';

import { cookies } from 'next/headers';
import { deriveDemoActorId, isDemoRoleId, isDemoTenantId, type DemoRoleId, type DemoTenantId } from '../lib/demo-identity';
import { SESSION_TOKEN_COOKIE, issueSessionToken, revokeSession, verifySessionToken } from '../lib/session-token';

/**
 * Server Actions for the demo session token (TD-WEB-PROVENANCE-BRIDGE, post-ZB-WEBPROV-01).
 *
 * This app's first Server Action — no `'use server'` file existed anywhere in `web/` before
 * this (confirmed by grep), noting that explicitly rather than implying an existing pattern.
 * The whole reason this exists: only server-side code can set an `HttpOnly` cookie. The
 * reverted `biolog-demo-role` cookie was client-written (`document.cookie`) specifically
 * because no server-side issuance path existed yet — that is the gap this file closes.
 *
 * `session-context.tsx`'s `SessionProvider` (a Client Component) imports and calls these
 * directly; Next.js handles the client → server RPC under the hood. Neither function touches
 * React state itself — `SessionProvider` owns that, these only own the server-side session
 * token's lifecycle (issue / revoke / cookie).
 *
 * **ZB-WEBPROV-02 (Critical, round 2 re-review)**: round 2's fix made the token itself
 * unforgeable (signed, `HttpOnly`), but `startDemoSession` still signed WHATEVER role/tenant
 * its caller asked for, with no restriction on who could ask. An attacker didn't need to forge
 * the cookie anymore — they could just ask this action for `admin` and receive a genuinely
 * valid, correctly-signed proof for it. A signature proves "the server signed this," never
 * "this caller was entitled to claim this." Fixed by `issueSessionToken` itself refusing
 * outright unless `isDemoSessionIssuanceAllowed()` (`session-token.ts`) is true — a new,
 * explicit, default-OFF `BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE` opt-in, deliberately NOT
 * `NODE_ENV === 'production'` (see that file's own header for the full account of why a first
 * version of this check, written there, was actively wrong, not just differently placed — it
 * would have disabled this app's own approved BIO-UX-002 tenant/role switcher the moment this
 * demo was ever deployed for its own intended purpose). This function has no copy of that
 * check of its own on purpose — `issueSessionToken` is the single enforcement point every
 * issuance path goes through, and this function's own existing `!token` handling below already
 * does the right thing (clear the cookie, don't establish a session) when it refuses.
 */

const COOKIE_OPTIONS = {
  httpOnly: true,
  path: '/',
  sameSite: 'lax' as const,
  secure: process.env.NODE_ENV === 'production',
};

const DEMO_SESSION_CONTEXT_AUDIT_PATH = '/api/v1/tenancy/demo-session/context-change';

function resolveApiBaseUrl(): string {
  // Same env var, same default, as `next.config.mjs`'s own `rewrites()` - the one place this
  // base URL is already established in this codebase.
  return process.env.BIOLOG_API_BASE_URL ?? 'http://127.0.0.1:5080';
}

/**
 * OPS002-F02 fix (BIO-TEN-002, 2026-10-07) - audits a tenant/role context switch through the
 * new backend boundary `api/Biolog.Api/Tenancy/DemoSessionContextAuditEndpoints.cs`. Called
 * from `startDemoSession` below, right after it has revoked the old token (if any) and minted
 * the new one but BEFORE committing the new session cookie - the one place that unambiguously
 * knows both the old and the new (tenant, role) pair, and the point `startDemoSession` treats
 * as the actual commit of the switch.
 *
 * **Round 2 (SY-OPS002-04, High) - reversed from round 1's fire-and-forget design.** Round 1
 * called this AFTER `jar.set(...)` and did not await it, swallowing any failure - Seyi's
 * round-2 review correctly rejected that: a successful switch (cookie committed) could leave
 * ZERO audit trail if the write failed, the opposite of what AC04 asks for. Fixed to the same
 * audit-before-commit discipline already established elsewhere on this floor (e.g.
 * `DeviceRemoteJobService`/BIO-DEV-004: audited through the real shared sink BEFORE the
 * action, never best-effort after) - `startDemoSession` now awaits this, and a failure here
 * fails the switch itself (see that function's own updated header), exactly like every other
 * failure mode it already handles by clearing the cookie rather than committing one.
 *
 * This DOES mean a web deployment with no reachable `Biolog.Api` backend can no longer
 * establish a demo session cookie at all - an accepted, disclosed consequence of ADR-0003
 * Decision 1's hard-dependency rule applying here after all, not a gap being reintroduced:
 * nothing backend-dependent would have worked in that deployment anyway, and no SIMULATION-mode
 * test (the default everywhere) reads this `HttpOnly` cookie, so this has no effect on the bulk
 * of this app's own test suite - only the LIVE, backend-required specs
 * (`BIOLOG_TDWEBPROV_LIVE_TESTS`/`BIOLOG_TEN003_LIVE_TESTS`/`BIOLOG_ATT003_LIVE_TESTS`) ever
 * exercise this cookie at all, and all three already require a real reachable backend to run.
 */
async function recordContextChangeAudit(
  newToken: string,
  previous: { tenantId: DemoTenantId; roleId: DemoRoleId } | null,
): Promise<boolean> {
  try {
    const response = await fetch(`${resolveApiBaseUrl()}${DEMO_SESSION_CONTEXT_AUDIT_PATH}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Web-Session-Token': newToken,
      },
      body: JSON.stringify(
        previous
          ? {
              previous_tenant_id: previous.tenantId,
              previous_role_id: previous.roleId,
              previous_actor_id: deriveDemoActorId(previous.roleId),
            }
          : { previous_tenant_id: null, previous_role_id: null, previous_actor_id: null },
      ),
    });
    return response.ok;
  } catch {
    return false;
  }
}

/**
 * Mints a fresh session token for the given (tenant, role) pair and sets it as the
 * `HttpOnly` session cookie, replacing whatever was there before. Called whenever either
 * selection changes — the token binds both together, so a change to just one still needs a
 * fresh token carrying the OTHER's current value too, not a partial update.
 *
 * Fails closed in every one of its own failure modes, never leaving a stale or partial cookie
 * behind: an unrecognised tenant/role id (defense in depth — `SessionProvider` only ever
 * passes known ids today, but this action is a server boundary a caller could in principle
 * invoke directly), `issueSessionToken` returning `null` (issuance disabled at the deployment
 * level, ZB-WEBPROV-02, or `WEB_SESSION_TOKEN_SECRET` unconfigured), or — as of OPS002-F02
 * round 2 (SY-OPS002-04, High) — `recordContextChangeAudit` failing, all clear any existing
 * cookie rather than issue or keep one. The audit write now happens BEFORE the cookie is
 * committed and is awaited, not fire-and-forget after: see that function's own header for why
 * round 1's "commit then best-effort audit" order was wrong (a successful switch could leave
 * zero audit trail) and why gating the commit on it is now the deliberate, accepted trade-off
 * rather than a regression.
 *
 * ZB-WEBPROV-03 (High, round 2 re-review): revokes the PRIOR session (if any) before issuing
 * the new one. Previously, switching from admin to viewer left the old admin token's `sid`
 * un-revoked — a copy made before the switch stayed fully valid for the token's whole 24h
 * lifetime regardless of what the UI now showed. Reissuing is a privilege change exactly like
 * sign-out is a privilege removal; both now genuinely invalidate what came before them, not
 * just stop displaying it.
 */
export async function startDemoSession(tenantId: string, roleId: string): Promise<void> {
  const jar = await cookies();

  if (!isDemoTenantId(tenantId) || !isDemoRoleId(roleId)) {
    jar.delete(SESSION_TOKEN_COOKIE);
    return;
  }

  const previousToken = jar.get(SESSION_TOKEN_COOKIE)?.value;
  let previousContext: { tenantId: DemoTenantId; roleId: DemoRoleId } | null = null;
  if (previousToken) {
    const previousVerification = await verifySessionToken(previousToken);
    if (previousVerification.valid) {
      previousContext = { tenantId: previousVerification.claims.tenantId, roleId: previousVerification.claims.roleId };
      await revokeSession(previousVerification.claims.sid);
    }
  }

  const token = await issueSessionToken({ tenantId, roleId });
  if (!token) {
    jar.delete(SESSION_TOKEN_COOKIE);
    return;
  }

  // OPS002-F02 round 2 (SY-OPS002-04, High) - audit BEFORE commit, awaited: a failed audit
  // write fails the switch, matching this function's own existing fail-closed shape for every
  // other failure mode above, rather than committing a cookie with no audit trail behind it.
  const audited = await recordContextChangeAudit(token, previousContext);
  if (!audited) {
    jar.delete(SESSION_TOKEN_COOKIE);
    return;
  }

  jar.set(SESSION_TOKEN_COOKIE, token, COOKIE_OPTIONS);
}

/**
 * Genuinely invalidates the current session server-side (TD-WEB-PROVENANCE-BRIDGE's required
 * remediation) — not the old `setSignedOut(true)`, which was React-only state that left the
 * cookie (and whatever it authorized) fully valid. Revokes by `sid` before clearing the
 * cookie, so even a copy of the old cookie value made before this call cannot be replayed
 * afterward. A missing/already-invalid token is treated as already signed out, not an error.
 */
export async function endDemoSession(): Promise<void> {
  const jar = await cookies();
  const token = jar.get(SESSION_TOKEN_COOKIE)?.value;
  if (token) {
    const result = await verifySessionToken(token);
    if (result.valid) {
      // Awaited deliberately (Emeka's commit 2f43919 made this async: revocation is now a
      // file write, not an in-memory Set add) - the write must genuinely land before this
      // action returns and the cookie is cleared, not race it.
      await revokeSession(result.claims.sid);
    }
  }
  jar.delete(SESSION_TOKEN_COOKIE);
}
