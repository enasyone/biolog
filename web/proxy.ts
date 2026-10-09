import { NextResponse, type NextRequest } from 'next/server';
import { deriveDemoActorId } from './lib/demo-identity';
import {
  MINIMUM_ACTOR_PROVENANCE_SECRET_LENGTH,
  actorProvenanceEnvVarName,
  computeHmacHex,
  resolveActorProvenanceConfigurationKey,
} from './lib/actor-provenance-routes';
import { SESSION_TOKEN_COOKIE, verifySessionToken } from './lib/session-token';

/**
 * Trusted edge for `ActorProvenanceGate`-protected endpoints (TD-WEB-PROVENANCE-BRIDGE),
 * round 2 - rebuilt 2026-10-07 on a signed session token after ZB-WEBPROV-01 (Critical,
 * `evidence/TD-WEB-PROVENANCE-BRIDGE/codex/SECURITY-REVIEW-ZAINAB.md`) failed the first
 * version for trusting a plain, client-writable `biolog-demo-role` cookie.
 *
 * `./lib/session-token.ts`'s revocation check is file-backed (`./lib/session-revocation-store.ts`),
 * not in-memory - an in-memory module-scope `Set` was tried first and found insufficient even
 * though this file and the issuance/sign-out Server Action both run on Node.js: Turbopack
 * compiles them as separate module graphs, each with its own copy of any module-scope state,
 * so a revocation written by sign-out was never visible here. See that store's own header for
 * the full account.
 *
 * **This file genuinely runs on Node.js, confirmed by the compiled build artifact, not just
 * dev-mode behavior or this file's own inability to declare `export const runtime` (Damilola's
 * own question, 2026-10-07 - worth settling with evidence, not re-assertion):** a production
 * `next build`'s own `.next/server/functions-config-manifest.json` records
 * `"/_middleware": { "runtime": "nodejs", ... }` - the actual configuration Next.js will use to
 * SERVE this file, not an inference from an error message or a guess that dev mode happens to
 * be more permissive than a real deployment. `./lib/session-revocation-store.ts`'s `node:fs`/
 * `node:path` imports (which the Edge runtime does not support at all) are therefore safe here.
 *
 * JOINT OWNERSHIP (standing shared-shell-edit rule — both must sign off on this file):
 * Damilola owns session issuance/sign-out (the server action that calls
 * `issueSessionToken`/`revokeSession` and sets/clears the `SESSION_TOKEN_COOKIE` cookie as
 * `HttpOnly` - never `document.cookie` from client JS, which was round 1's defect). Emeka
 * owns the token format, signing/verification, and this file's own validate-then-derive flow.
 */

const GATED_PATH_PREFIX = '/api/v1/';

/**
 * `X-Tenant-Id`/`X-Web-Session-Token` forwarding — disclosed, not fully closed (Kemi's ruling,
 * tightened after reading ADR-0002 Decision 1.1/Decision 7). Nothing on the .NET backend
 * currently reads either header - the only implemented tenant-resolution channel is ADR-0002's
 * own `host` channel (`TenantContextMiddleware.cs`), resolved from the literal HTTP `Host`
 * header, which this proxy does not rewrite (real new architecture - Host/DNS-level tenant
 * routing - explicitly out of this TD's scope).
 *
 * **Why both headers, not just `X-Tenant-Id` alone**: Decision 1.1's `principal` channel
 * requires a claim that arrives AUTHENTICATED, not merely asserted; Decision 7's own realization
 * of that channel (device-secret auth) never trusts a request-supplied `tenant_id` directly -
 * the tenant is resolved from something independently authenticated FIRST, and the request's own
 * claim is only ever cross-validated against that, never consumed as the resolution mechanism
 * itself. A bare `X-Tenant-Id` read on its own by some future hurried implementation is exactly
 * the "body/header field trusted as a channel" anti-pattern Decision 1.1 forbids - the fact that
 * THIS proxy derived it from a verified token does not make it safe for the BACKEND to trust
 * directly, since the backend has no way to confirm that derivation happened honestly; it would
 * just be trusting Next.js as an uncertified boundary, not verifying a signature itself.
 *
 * So the RAW signed token travels alongside the derived value, as `X-Web-Session-Token` - a
 * future `principal`-channel implementation MUST verify this token's own HMAC signature
 * server-side (a securely-provisioned copy of `WEB_SESSION_TOKEN_SECRET`) and extract `tenantId`
 * from the VERIFIED claims; `X-Tenant-Id` alone is a convenience/display value only, never a
 * trusted resolution source by itself. Making the verifiable path trivially available alongside
 * the derived one is meant to make verification the obvious thing to build, not a step skipped
 * under time pressure. Per Decision 7's own closing obligation, whichever future story actually
 * implements this needs Kemi's approval before landing, same as every other tenant-resolution
 * mechanism in this codebase - this comment is a disclosure, not a pre-approval.
 */
function stripAndInjectActorHeaders(
  request: NextRequest,
  actorId: string,
  tenantId: string,
  sessionToken: string,
  proof: string | null,
): Headers {
  const headers = new Headers(request.headers);
  // Never trust a caller-supplied value (ADR-0006 Decision 3). Every `web/features/*/live.ts`
  // that sets `X-Actor-Id` today does so with a hardcoded `'web-ui'` literal, not derived from
  // any real session state — that literal (and anything else a caller sent) is discarded here,
  // unconditionally, before the request is forwarded.
  headers.delete('X-Actor-Id');
  headers.delete('X-Actor-Provenance');
  headers.delete('X-Tenant-Id');
  headers.delete('X-Web-Session-Token');
  headers.set('X-Actor-Id', actorId);
  if (proof) {
    headers.set('X-Actor-Provenance', proof);
  }
  headers.set('X-Tenant-Id', tenantId);
  headers.set('X-Web-Session-Token', sessionToken);
  return headers;
}

async function computeActorProvenance(actorId: string, pathname: string): Promise<string | null> {
  const configurationKey = resolveActorProvenanceConfigurationKey(pathname);
  if (!configurationKey) {
    return null;
  }

  const secret = process.env[actorProvenanceEnvVarName(configurationKey)];
  if (!secret || secret.length < MINIMUM_ACTOR_PROVENANCE_SECRET_LENGTH) {
    return null;
  }

  return computeHmacHex(secret, actorId);
}

export async function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;
  if (!pathname.startsWith(GATED_PATH_PREFIX)) {
    return NextResponse.next();
  }

  const sessionToken = request.cookies.get(SESSION_TOKEN_COOKIE)?.value;
  const verification = await verifySessionToken(sessionToken);
  if (!verification.valid) {
    // Fails closed exactly like every other story's own ActorProvenanceGate: no session, no
    // headers injected, the request proceeds with whatever it already had (which
    // stripAndInjectActorHeaders below never reaches) - every caller-supplied X-Actor-Id/
    // X-Actor-Provenance/X-Tenant-Id/X-Web-Session-Token must still be discarded so a request
    // with no valid session can't smuggle its own forged identity through either.
    const headers = new Headers(request.headers);
    headers.delete('X-Actor-Id');
    headers.delete('X-Actor-Provenance');
    headers.delete('X-Tenant-Id');
    headers.delete('X-Web-Session-Token');
    return NextResponse.next({ request: { headers } });
  }

  const { roleId, tenantId } = verification.claims;
  const actorId = deriveDemoActorId(roleId);
  const proof = await computeActorProvenance(actorId, pathname);
  const headers = stripAndInjectActorHeaders(request, actorId, tenantId, sessionToken!, proof);

  return NextResponse.next({ request: { headers } });
}

export const config = {
  matcher: '/api/v1/:path*',
};
