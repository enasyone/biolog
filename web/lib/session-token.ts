/**
 * Signed, revocable demo session token (TD-WEB-PROVENANCE-BRIDGE, Emeka's half, round 2 -
 * ZB-WEBPROV-01, Critical).
 *
 * Replaces the plain, non-`HttpOnly`, client-writable `biolog-demo-role` cookie Zainab's
 * security review correctly failed: any caller could set that cookie directly and have
 * `proxy.ts` mint a real, valid `X-Actor-Provenance` proof for it. Kemi's scope ruling
 * (narrower than full browser authentication, which stays out of scope for this TD): make
 * the EXISTING demo mechanism tamper-evident and confined to synthetic data, not build real
 * sign-in. This is that fix - "fixing a forgeable-session implementation bug," per Kemi's own
 * framing, not a new ADR decision.
 *
 * Framework-agnostic (no Next.js/React import), same reasoning as `demo-identity.ts` and
 * `actor-provenance-routes.ts` - `proxy.ts` and the issuance server action (Damilola's half)
 * both import this directly, and it is unit-tested on its own.
 *
 * **Format**: `base64url(JSON(claims)) + "." + hex(HMACSHA256(secret, payloadB64))` - a
 * hand-rolled construction matching this codebase's own established convention (no JWT
 * library anywhere - see BIO-INT-001's `HmacSessionTokenCodec`, BIO-INT-003's
 * `HmacApiAccessTokenCodec`), not a new one invented here. The signature covers the RAW
 * base64url payload string (not a re-parsed/re-serialized form of it), so verification never
 * depends on JSON key ordering being stable.
 *
 * **Claims are bound together by one signature** (Kemi's explicit requirement): `roleId` and
 * `tenantId` cannot be independently forged or swapped without invalidating the whole token -
 * a caller who tampers with either field breaks the HMAC over the full payload.
 *
 * **Revocation**: file-backed (`./session-revocation-store.ts`), not an in-memory
 * `Set`. **Correction, found by Damilola hand-driving the real sign-out/replay flow against a
 * real dev server**: an in-memory module-scope `Set` was the original design here, reasoned
 * to be safe because both `proxy.ts` and the issuance/sign-out Server Action run on the
 * Node.js runtime and so "share one process's memory." That turned out to be necessary but
 * NOT sufficient - Turbopack compiles the middleware/proxy bundle and the Server Action
 * bundle as separate module graphs even within one Node.js process, so each got its own copy
 * of the Set; a revocation written by sign-out was never visible to the proxy's own copy, so
 * sign-out silently revoked nothing and a replayed pre-sign-out cookie still produced a valid
 * proof. "Same process" does not mean "same module instance" once a bundler is involved - see
 * `./session-revocation-store.ts`'s own header for the full account and the fix (the
 * filesystem as the shared state layer, the same shape `FileBackedPendingGalleryCleanupStore.cs`
 * - BIO-BIO-009 round 4 - already established in this exact codebase for an identical need).
 *
 * **Secret**: its own, brand-new `WEB_SESSION_TOKEN_SECRET` - NOT reused from any
 * `ActorProvenanceSecret` or `ApiSecurity:*` key (a leaked key here must not compromise any
 * backend secret, and vice versa). Same `MINIMUM_SESSION_TOKEN_SECRET_LENGTH` (32) fail-closed
 * discipline as every other secret in this codebase (TD-PROVENANCE-DUMMYKEY-SWEEP) - Kemi's
 * own explicit warning not to repeat that mistake a fourth time in a brand-new mechanism.
 *
 * **ZB-WEBPROV-02 (Critical, round 2) - signing a caller-selected claim does not make the
 * claim trustworthy.** Round 2 made the TOKEN tamper-evident but left `issueSessionToken`
 * itself callable by anyone, for any allow-listed role - including `admin` - with no
 * authentication, authorization, or deployment-mode check at all. An attacker never needed to
 * forge a cookie; they could just ask the (public, unauthenticated) issuance path to sign
 * `admin` legitimately, and `proxy.ts` would mint a real backend provenance proof for it. A
 * signature only proves THIS issuer signed the claims presented to it - it says nothing about
 * whether presenting those claims should have been allowed, which is the actual trust
 * decision this whole bridge exists to get right.
 *
 * **Fix, converged on after Emeka and Damilola independently built two conflicting gates in
 * parallel and reconciled them.** Two things both needed to be right: WHERE the check lives,
 * and WHAT it checks.
 *
 * *What it checks*: a first version gated on `process.env.NODE_ENV === 'production'`
 * (mirroring `feature-mode.ts`'s own `setPortModeOverride`) - Damilola caught that this was
 * actively WRONG, not just redundant with her own separately-built gate: this whole app is a
 * synthetic demo/showcase product whose tenant/role switcher (BIO-UX-002, an approved, core
 * feature) is MEANT to keep working in a real, optimised, `NODE_ENV=production` deployment -
 * that is how a demo actually gets shown to anyone. Gating on `NODE_ENV` would have silently
 * broken the switcher the moment this app was ever deployed for its own intended purpose,
 * while leaving it "working" only in `next dev`, never how a real demo is served. `NODE_ENV`
 * describes build optimisation, not "this deployment has real tenant data" - a distinct axis
 * this app had never needed to express before. `isDemoSessionIssuanceAllowed()` below instead
 * gates a new, explicit, default-OFF `BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE` env var (Damilola's
 * own design) - unset (this repo's real default) means refuse, exactly like every other
 * secret/capability in this codebase that must fail closed when unconfigured.
 *
 * *Where it lives*: Damilola's first version put this check in `session-actions.ts`'s
 * `startDemoSession` (the one Server Action that currently calls `issueSessionToken`). Moved
 * here instead, into the lower-level primitive itself, once we agreed on the semantic - this
 * is the single fundamental choke point every issuance path goes through (including this
 * file's own test suite and the live Playwright spec, which both call `issueSessionToken`
 * directly), so it protects any future direct caller too, not only the one Action that exists
 * today. `session-actions.ts` no longer has its own separate copy of this check - one
 * enforcement point, not two that could drift apart.
 *
 * The other half of Kemi's own requirement - "bind it to a dedicated synthetic tenant/data
 * plane with no route to real tenant grants" - is only PARTIALLY enforceable here:
 *
 * The other half of Kemi's own requirement - "bind it to a dedicated synthetic tenant/data
 * plane with no route to real tenant grants" - is only PARTIALLY enforceable here:
 * `isDemoTenantId` already restricts which tenant a token's own claim can name (`alpha`/`beta`
 * only), but that restriction is not currently load-bearing on the backend, since (per this
 * story's own disclosed, unresolved gap) the .NET backend's only implemented tenant channel
 * resolves the REAL tenant from the HTTP `Host` header, not from anything this token claims -
 * whichever tenant a deployment's `Host` routing actually resolves demo traffic to is the real
 * exposure surface, and ensuring that tenant never carries real grants is a deployment/ops
 * responsibility this code cannot enforce from inside itself.
 */

import { isDemoRoleId, isDemoTenantId, type DemoRoleId, type DemoTenantId } from './demo-identity';
import { computeHmacHex } from './actor-provenance-routes';
import { isRevoked, recordRevocation } from './session-revocation-store';

export const SESSION_TOKEN_COOKIE = 'biolog-session-token';

export const MINIMUM_SESSION_TOKEN_SECRET_LENGTH = 32;

/** 24 hours - a demo convenience session, reissued on every role/tenant change (extends on each reissue), not a long-lived credential. */
export const DEFAULT_SESSION_TOKEN_TTL_MS = 24 * 60 * 60 * 1000;

export interface SessionTokenClaims {
  /** Random, unique per issuance - the sole unit of revocation. */
  sid: string;
  roleId: DemoRoleId;
  tenantId: DemoTenantId;
  issuedAtMs: number;
  expiresAtMs: number;
}

export type SessionTokenVerification =
  | { valid: true; claims: SessionTokenClaims }
  | { valid: false; reason: 'malformed' | 'bad_signature' | 'expired' | 'revoked' | 'unconfigured' | 'issuance_disabled' };

export { __clearRevocationStoreForTests as __clearRevokedSessionsForTests } from './session-revocation-store';

export async function revokeSession(sid: string): Promise<void> {
  await recordRevocation(sid);
}

function isSessionTokenSecretConfigured(secret: string | undefined): secret is string {
  return !!secret && secret.length >= MINIMUM_SESSION_TOKEN_SECRET_LENGTH;
}

function toBase64Url(value: string): string {
  const base64 = typeof Buffer !== 'undefined' ? Buffer.from(value, 'utf8').toString('base64') : btoa(value);
  return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function fromBase64Url(value: string): string {
  const base64 = value.replace(/-/g, '+').replace(/_/g, '/').padEnd(value.length + ((4 - (value.length % 4)) % 4), '=');
  return typeof Buffer !== 'undefined' ? Buffer.from(base64, 'base64').toString('utf8') : atob(base64);
}

/** Constant-time string comparison - avoids a timing side-channel on the signature check. */
function timingSafeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) {
    return false;
  }
  let diff = 0;
  for (let i = 0; i < a.length; i += 1) {
    diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  }
  return diff === 0;
}

function getSecret(): string | undefined {
  return process.env.WEB_SESSION_TOKEN_SECRET;
}

/**
 * ZB-WEBPROV-02's actual gate (see this file's own header for the full "what it checks, where
 * it lives" account). Default-OFF, explicit opt-in - unset means refuse, the same fail-closed
 * discipline every secret/capability in this codebase already applies. Must be set to the
 * exact literal `'true'` in any deployment's environment that is the sanctioned BIOLOG demo/
 * synthetic showcase, and NEVER in any deployment that has, or could ever have, real tenant
 * data or real permission grants - this is a deploy-time operational control, documented here
 * rather than solved in code, the same way this story's own README "Secret distribution
 * tradeoff" section discloses a different operational concern rather than pretending code
 * alone resolves it. Exported so callers/tests can assert on the gate directly.
 */
export function isDemoSessionIssuanceAllowed(): boolean {
  return process.env.BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE === 'true';
}

/**
 * Issues a new signed token for the given role/tenant. Returns `null` (never throws, never
 * falls back to a dummy key) if demo session issuance is not explicitly enabled
 * (`isDemoSessionIssuanceAllowed`, ZB-WEBPROV-02) or if `WEB_SESSION_TOKEN_SECRET` is missing
 * or too short - the caller (the issuance server action) must treat a `null` result as
 * "cannot establish a demo session right now" rather than silently proceeding without one.
 */
export async function issueSessionToken(
  params: { roleId: DemoRoleId; tenantId: DemoTenantId },
  ttlMs: number = DEFAULT_SESSION_TOKEN_TTL_MS,
): Promise<string | null> {
  if (!isDemoSessionIssuanceAllowed()) {
    return null;
  }

  const secret = getSecret();
  if (!isSessionTokenSecretConfigured(secret)) {
    return null;
  }

  const issuedAtMs = Date.now();
  const claims: SessionTokenClaims = {
    sid: crypto.randomUUID(),
    roleId: params.roleId,
    tenantId: params.tenantId,
    issuedAtMs,
    expiresAtMs: issuedAtMs + ttlMs,
  };

  const payloadB64 = toBase64Url(JSON.stringify(claims));
  const signature = await computeHmacHex(secret, payloadB64);
  return `${payloadB64}.${signature}`;
}

/**
 * Verifies a token's signature, expiry, and revocation status. Fails closed (`valid: false`)
 * on anything malformed, unsigned-with-the-real-secret, expired, or revoked - never throws,
 * so `proxy.ts` can treat every non-`valid: true` result identically (no session).
 *
 * **Round 4 fix (Zainab's "additional fail-closed gap")**: also checks
 * `isDemoSessionIssuanceAllowed()`, not just `issueSessionToken`. Previously, disabling
 * `BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE` stopped NEW tokens from being minted but left every
 * already-issued token valid for up to 24h - the deployment "off switch" didn't actually turn
 * anything off for existing sessions. Checking the flag here too means flipping it off
 * instantly invalidates every live session on the very next request, not just future issuance -
 * this IS the "disabling it must revoke the active issuance epoch" Zainab asked for, achieved
 * by re-checking the same gate rather than tracking a separate epoch/generation counter.
 */
export async function verifySessionToken(token: string | undefined | null): Promise<SessionTokenVerification> {
  if (!isDemoSessionIssuanceAllowed()) {
    return { valid: false, reason: 'issuance_disabled' };
  }

  const secret = getSecret();
  if (!isSessionTokenSecretConfigured(secret)) {
    return { valid: false, reason: 'unconfigured' };
  }

  if (!token) {
    return { valid: false, reason: 'malformed' };
  }

  const separatorIndex = token.indexOf('.');
  if (separatorIndex <= 0 || separatorIndex === token.length - 1) {
    return { valid: false, reason: 'malformed' };
  }

  const payloadB64 = token.slice(0, separatorIndex);
  const presentedSignature = token.slice(separatorIndex + 1);
  const expectedSignature = await computeHmacHex(secret, payloadB64);
  if (!timingSafeEqual(presentedSignature, expectedSignature)) {
    return { valid: false, reason: 'bad_signature' };
  }

  let claims: SessionTokenClaims;
  try {
    const parsed = JSON.parse(fromBase64Url(payloadB64)) as Partial<SessionTokenClaims>;
    if (
      typeof parsed.sid !== 'string' || parsed.sid.length === 0 ||
      !isDemoRoleId(parsed.roleId) ||
      !isDemoTenantId(parsed.tenantId) ||
      typeof parsed.issuedAtMs !== 'number' || !Number.isFinite(parsed.issuedAtMs) ||
      typeof parsed.expiresAtMs !== 'number' || !Number.isFinite(parsed.expiresAtMs)
    ) {
      return { valid: false, reason: 'malformed' };
    }
    claims = {
      sid: parsed.sid,
      roleId: parsed.roleId,
      tenantId: parsed.tenantId,
      issuedAtMs: parsed.issuedAtMs,
      expiresAtMs: parsed.expiresAtMs,
    };
  } catch {
    return { valid: false, reason: 'malformed' };
  }

  if (Date.now() > claims.expiresAtMs) {
    return { valid: false, reason: 'expired' };
  }

  if (await isRevoked(claims.sid)) {
    return { valid: false, reason: 'revoked' };
  }

  return { valid: true, claims };
}
