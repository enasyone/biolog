# TD-WEB-PROVENANCE-BRIDGE — web-side trusted edge for ActorProvenanceGate

**Status: round 5 PASSed and merged into `develop` (commit `7ac4876`, 2026-10-07); round 6
lands the actor-id half of the same demo-session shape, folded into the ADR-0002 amendment
round 5 already owes.** Five prior rounds: round 1 FAILed critical (ZB-WEBPROV-01, forgeable
cookie) and was reverted to a no-op; round 2 built a signed session token but FAILed critical
again (ZB-WEBPROV-02, no restriction on who could self-select which role) plus high
(ZB-WEBPROV-03, reissue/revocation gaps); round 3 closed ZB-WEBPROV-03 but ZB-WEBPROV-02
remained (downgraded to High) plus a new fail-closed gap; round 4's planned fix (Host-header
rewrite) was found mechanically unworkable before being built (Next.js's own rewrite proxy
hardcodes `changeOrigin: true`) and was replaced by round 5's actual fix (backend-side tenant
confinement); round 5 PASSed and merged. Round 6 — see "Round 6" below — fixes a blocker
Ngozi's reverify work surfaced AFTER round 5 merged: the demo actor id round 1 minted was never
a well-formed canonical BIOLOG identifier, so no story could get a real "granted" result
through this bridge even with round 5's tenant confinement in place.

## The gap

`docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md` Decision 3 names a trusted
edge that must strip any caller-supplied `X-Actor-Id` and inject a verified one plus an
HMAC proof (`X-Actor-Provenance`), calling the edge/gateway half "an infrastructure concern...
Emeka's domain." No such edge has ever existed on the web surface — Tunde hit it finishing
BIO-TEN-003, and it's latent, unexercised, in merged BIO-INT-002. God/Kemi gave direction
2026-10-06 (Next.js middleware, joint ownership) and later bumped it to next-up priority
(already hit 3 stories: BIO-TEN-003, BIO-BIO-009, BIO-INT-002).

## Split (agreed with Emeka, 2026-10-06)

Damilola: session → middleware identity bridge, route integration. Emeka: secret storage/
access, route-prefix → secret mapping, HMAC computation. Both sign off on the final
`web/proxy.ts`.

## What's built (Damilola's half)

- **`web/lib/demo-identity.ts`** (new) — framework-agnostic constants shared by the client
  session context and the server-only proxy file (deliberately separate from
  `session-context.tsx` so a server file never pulls in a `'use client'`/React bundle across
  that boundary): the cookie name, the known demo role ids, and `deriveDemoActorId()`.
- **`deriveDemoActorId()`** produces a stable, **role-only** actor id (`demo-actor-admin` /
  `demo-actor-staff` / `demo-actor-viewer`) — not tenant-scoped. Agreed explicitly with Emeka:
  tenant scoping already happens inside `PermissionService.ActorHasPermissionAsync`'s own
  `tenantId` parameter, so a tenant-scoped id would double the id space for no provenance
  benefit. Deliberately not the free-text display name (`DEMO_USER_BY_ROLE`) — this id is the
  literal UTF-8 input to the HMAC proof, so it has to stay a clean machine key.
- **`web/context/session-context.tsx`** — `SessionProvider` now mirrors the selected role into
  a `biolog-demo-role` cookie (`document.cookie`, `path=/; samesite=lax`) whenever `roleId`
  changes, including the existing `?as=` override path (same effect, no separate code path).
  This is the app's **first** cookie-based state — `theme-script.tsx`'s `THEME_STORAGE_KEY` is
  `localStorage`, not server-readable, so there was no existing pattern to reuse; noting that
  explicitly rather than implying continuity with something that doesn't apply.
- **`web/proxy.ts`** (new) — reads the cookie, derives the actor id, strips any
  caller-supplied `X-Actor-Id`/`X-Actor-Provenance` (closes the hardcoded `'web-ui'` literal
  gap in `web/features/integrations/live.ts`, `bio-crd-008/live.ts`, `reporting/live.ts`,
  `bio-rpt-003/live.ts`), injects the derived `X-Actor-Id`, and calls
  `computeActorProvenance()` — **Emeka's seam, currently a documented stub that always
  returns `null`**, so no `X-Actor-Provenance` header is ever injected yet. Fails safe: a
  request reaching a gated endpoint today is rejected by `ActorProvenanceGate` exactly as it
  is without this file at all, never a false pass. `config.matcher` scopes this to
  `/api/v1/:path*` only — page navigations are untouched.

### Named `proxy.ts`, not `middleware.ts`

The original task dispatch said "concretely `web/middleware.ts`." This app runs Next.js
16.3.5, which deprecates the `middleware.ts` file convention in favour of `proxy.ts` (same
underlying feature; exported function renamed `middleware` → `proxy` to match) — confirmed
directly via this version's own build-time warning (`next build` output), not assumed from
general Next.js knowledge.

## What's built (Emeka's half)

`computeActorProvenance(actorId, pathname)` is implemented. Full design in
`web/lib/actor-provenance-routes.ts`'s own header; condensed here.

### The route table is genuinely non-trivial, confirmed, not assumed

**Fifteen** distinct `*ActorProvenanceSecret` configuration keys exist as of this story
(`grep -rn "ConfigurationKey = " api/features` across the whole repo), not the original task
dispatch's "12" - BIO-RPT-004, BIO-BIO-009 and BIO-INT-003 all landed after that count was
made. `route-manifest.ts`'s own `ServicePort` categorisation was checked and rejected as the
mapping: it groups several DIFFERENT `ActorProvenanceSecret`-bearing features under one
coarse label (`AccessPolicy`/`AccessDecisions`/`AccessCache` all fall under one
`AccessService`, for instance) - too coarse to pick the one correct secret a specific gated
route needs. `web/lib/actor-provenance-routes.ts` instead matches the real `/api/v1/*`
backend path every gated endpoint maps to, read directly from each feature's own
`Api/*Endpoints.cs` (or `BioInt003Module.cs`).

Several routes sit under a path prefix owned by one feature while a sibling feature's own doc
comments disclose "taking over" that same path. **An earlier version of this table mapped
three of these as if they were real provenance-gated handoffs, which Damilola's review
(2026-10-07) caught by reading the actual handler bodies rather than trusting the doc-comment
framing** - a route-OWNERSHIP collision (which feature's demo bridge answers this URL) is not
the same claim as a route-PROVENANCE-GATING collision (which feature's secret verifies a proof
on this URL). Each case below was re-checked against whether the handler actually takes an
`ActorProvenanceGate` parameter and calls `.Require()`, not just against the surrounding doc
comment, and each is proven in `tests/frontend/unit/td-web-provenance-bridge-route-table.test.ts`:

- `/api/v1/visitors/{id}/credential` -> `VisitorCredentials` (BIO-VIS-002's `GrantAsync`,
  confirmed `.Require()`'d) - the only real gated handoff.
- `/api/v1/visitors/{id}/check-out` (`CheckOutAsync`), `/api/v1/attendance/primary-action` and
  `/api/v1/attendance/flag-exception` (bio-att-002's own bridge), and `/api/v1/access/
  primary-action`/`/api/v1/access/view-access-events` (bio-acc-002/bio-acc-003's own demo
  bridges) are all PLAIN DEMO-BRIDGE handlers with **no `ActorProvenanceGate` parameter at
  all** - no table entry needed for any of them. The first two correctly fall through to
  their surrounding feature's own prefix entry (`Visitors`/`Attendance` - harmless, since
  nothing there checks the proof either); the `/api/v1/access/*` pair correctly resolve to no
  match at all (`null`), since that bare prefix has no entry of its own.
- `/api/v1/integrations/service-accounts/whoami` is explicitly excluded from the
  `ApiSecurity:ActorProvenanceSecret` prefix match (bearer-token authorized via BIO-INT-003's
  own OAuth2 `AuthorizeAsync`, never `ActorProvenanceGate`-gated) - also Damilola's review,
  fixed for precision rather than left to match-and-be-harmless.

`/api/v1/persons/{personId}/*` deliberately has NO blanket entry: most of that path space
(profiles, contact-point, lifecycle, duplicate-candidates, card-issues) belongs to features
with no `ActorProvenanceGate` at all - only BIO-IDN-006's own `roles`/`permissions` sub-paths
are matched.

### HMAC computation

`web/lib/actor-provenance-routes.ts`'s `computeHmacHex(secret, message)` uses Web Crypto
(`crypto.subtle`), not Node's `crypto` module - identical on Edge and Node.js runtimes, so
`proxy.ts` does not need `export const runtime = 'nodejs'`. Mirrors
`HmacActorProvenanceStore.Verify()` exactly: UTF-8 secret bytes as the HMAC-SHA256 key, UTF-8
actor id alone as the signed message (no tenant/timestamp/path mixed in), hex output (case
doesn't matter - `Convert.FromHexString` is case-insensitive). Verified against a golden
vector cross-checked via `openssl dgst -sha256 -hmac` independently of this code, not just
self-consistency. A secret shorter than `MINIMUM_ACTOR_PROVENANCE_SECRET_LENGTH` (32,
mirroring `HmacActorProvenanceStore.MinimumSecretLength`) is treated as unconfigured and never
used as a real key - the same TD-PROVENANCE-DUMMYKEY-SWEEP fail-closed discipline the server
side already applies, so a weak/typo'd secret on the web side can't produce a verifiable proof
the server itself would have rejected as unconfigured.

### Secret distribution tradeoff (Kemi asked this stated, not resolved)

Centralising all fifteen secrets into one edge component (this proxy) means the web server's
own deployment environment must hold a copy of every one of them, even though any single
request only ever needs one. This is a REAL weakening of web-path secret isolation relative to
the mobile/operator paths (each of which presumably only needs its own narrower secret set) -
a compromise of the web deployment's environment exposes all fifteen secrets at once, not just
the one the compromised request happened to use. ASP.NET Core's own env-var convention
(`Section:Key` -> `Section__Key`) is reused as-is (`actorProvenanceEnvVarName()`) rather than
inventing a new one, which means a colocated deployment can share literally the same env var
values between the API and the web server; a non-colocated deployment must provision its own
copy of all fifteen values for the web server's own environment - an operational/secret-
distribution concern, not something this story resolves.

### Verification (Emeka's half)

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — same 34 routes + ƒ Proxy (Middleware), no deprecation warning
corepack pnpm --dir tests/frontend test:unit -- td-web-provenance-bridge   # 15/15 pass (route-table.test.ts, new)
```

Full `test:unit` run: 516/518 pass. The 2 failures
(`bio-ux-005-feature-mode.test.ts`, `bio-ux-005-module-registry.test.ts`) are a **pre-existing,
unrelated** gap - BIO-UX-005's own module-registry fixture was never updated for BIO-INT-003's
new `IntegrationService` port keys. Confirmed by reproducing the identical failure on a clean
`develop` checkout (`d58665f`) with none of this story's changes applied. Reported to god as
an FYI, not fixed here - BIO-UX-005's own fixture, outside this story's ownership.

## Also found, explicitly out of scope here

- `next.config.mjs`'s `rewrites()` has no entry for `/api/v1/integrations/*` at all — a
  second, independent wiring gap on the one live code path that actually reaches a gated
  endpoint today. Needs fixing before `IntegrationService` can go live, but it's a routing
  gap, not a provenance one — flagging, not fixing, here.
- No tenant header reaches the backend from web at all today (checked `live.ts`/
  `module-port.ts`) — a separate, pre-existing gap this task does not take on.
- **Unrelated pre-existing test defect found during verification**: `bio-ux-013-breakpoints.spec.ts`'s
  "nav pattern" tests (`below bpTablet`, `at bpTablet and above`) fail with a Playwright
  strict-mode violation — `getByRole('link', { name: 'People' })` resolves to both the sidebar's
  own "People" link AND the Dashboard's "View people" drilldown link (`data-testid=
  "dashboard-drilldown-identity"`), because Playwright's default substring/case-insensitive
  name matching treats "View **people**" as a match for `name: 'People'`. **Confirmed this is
  NOT caused by this story**: reproduced identically against a clean `develop` checkout with
  none of this story's changes applied. Reported to god as an FYI, not fixed here — out of
  this story's scope/ownership.

## Verification (Damilola's half, when it was still standalone)

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — 34 routes + ƒ Proxy (Middleware), no deprecation warning
corepack pnpm --dir tests/frontend test:unit   # 476/476 pass (6 new: td-web-provenance-bridge-identity.test.ts)
corepack pnpm --dir tests/frontend test:e2e    # full run: 403 passed, 23 failed/18 skipped/15 did not run —
                                                # re-ran a sample standalone (not full-parallel) and confirmed
                                                # the overlapping failures reproduce byte-identically on a
                                                # clean develop checkout; not a regression from this change
```

## Sign-off (2026-10-07)

Both halves reviewed and independently re-verified by Damilola before sign-off — not a
rubber-stamp of Emeka's own report. Re-ran `typecheck`/`build` clean. Re-ran `test:unit`
directly: 517/519, same 2 pre-existing `bio-ux-005-*` failures, confirmed unrelated (both about
`CredentialService`/BIO-INT-003's own port-registry fixture, a subsystem neither half of this
story touches). Independently traced the fixed route-table entries against the actual `.cs`
handler bodies rather than trusting the commit message: confirmed `GrantAsync` (the one real
handoff) takes an `ActorProvenanceGate` parameter and calls `.Require()`; confirmed
`CheckOutAsync`, `PrimaryActionAsync`/`FlagExceptionAsync` (bio-att-002), and the
bio-acc-002/bio-acc-003 demo bridges all take none — matches the corrected table exactly.
**Both Damilola and Emeka sign off on the combined `web/proxy.ts` as of commit `d644c5a`.**
**This sign-off was subsequently superseded — see the next section.**

## Security review FAIL and revert (2026-10-07)

Zainab's required security review (Kemi's ruling that this story needs one, not a formal
BIOLOG-story lifecycle gate) caught what the two of us, and each other's review passes, both
missed: **ZB-WEBPROV-01, Critical, verdict FAIL / DO NOT MERGE**
(`evidence/TD-WEB-PROVENANCE-BRIDGE/codex/SECURITY-REVIEW-ZAINAB.md`).

`session-context.tsx`'s `SessionProvider` has always and explicitly documented itself as "a
placeholder session, not authentication or RBAC" (BIO-FND-007-AC04) — the role is selectable
through the UI and the `?as=` query parameter, by design, for anyone. Mirroring that selection
into a `biolog-demo-role` cookie and then having `web/proxy.ts` trust that cookie to select
which actor's real server secret to sign an `X-Actor-Provenance` proof with promotes an
explicitly-untrusted, caller-controllable value into the exact role ADR-0006 Decision 3's
trusted edge exists to prevent any caller-supplied value from playing. Any caller could send
`Cookie: biolog-demo-role=admin` to a same-origin `/api/v1/*` route and receive a
cryptographically valid, real `X-Actor-Provenance` proof for `demo-actor-admin` — genuine
privilege escalation if that demo role carries real grants in some future LIVE configuration,
and (Zainab's own framing) a bridge that can never produce its one required positive
authorized-LIVE proof if it doesn't. The flaw survived sign-out (`signedOut` is React-only
state; the cookie and the proxy never checked anything server-side) and — worth being honest
about — existed from this story's very first commit (`7f4994e`), not only once Emeka's HMAC
implementation gave it a working secret to sign with. Unsigned `X-Actor-Id` injection alone
was already asserting an identity for audit-attribution purposes based on nothing the edge had
actually verified.

**Route mapping, the 32-character secret floor, the Web Crypto HMAC construction, and
unconditional stripping of caller-supplied headers were all confirmed sound by Zainab's own
review** — the defect is specifically and only the identity SOURCE, not Emeka's HMAC/routing
work or the mechanism of stripping-and-injecting itself.

**Remediation taken**: reverted rather than patched around.
- `web/proxy.ts`'s `proxy()` is now a documented no-op (`NextResponse.next()` unconditionally)
  — it strips nothing and injects nothing. A request reaching a gated endpoint today behaves
  exactly as it would with no edge file present at all: rejected by that feature's own
  `ActorProvenanceGate`, never a false pass, never a forgeable one either.
- `session-context.tsx`'s `biolog-demo-role` cookie write (and its import of
  `demo-identity.ts`) was removed entirely — there is no remaining consumer that needs it, and
  leaving it in place would misleadingly imply something still trusts it.
- `web/lib/demo-identity.ts` and `web/lib/actor-provenance-routes.ts` are **left in place,
  unchanged** — their logic (role-id helpers, route-to-secret mapping, HMAC computation) was
  confirmed correct and is ready to be wired to a real identity source the moment one exists,
  without needing to be re-derived. Their own unit tests still pass (they test pure functions,
  independent of the reverted trust decision).

**Why not patch in a "better" fake mechanism instead**: Zainab's own remediation explicitly
says to resolve identity from "a server-authenticated, integrity-protected session or verified
token that the browser cannot forge," and to fail closed otherwise. **No such session exists
anywhere in this web app today** — confirmed during this story's own original research
(`session-context.tsx` is the only identity-adjacent state in `web/`, and it is genuinely just
a demo picker). Inventing a second, differently-shaped fake mechanism under time pressure would
very likely just relocate the same category of defect rather than fix it. God has asked a
researcher whether any real authenticated session mechanism exists anywhere in the app that
this could bind to instead; if not, this becomes an architectural question for Kemi about
whether real web authentication needs to land before this bridge can be rebuilt — not something
either of us should decide or route around unilaterally.

### Verification after the revert

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — same 34 routes + ƒ Proxy (Middleware)
corepack pnpm --dir tests/frontend test:unit   # 517/519 — same 2 pre-existing bio-ux-005 failures, unaffected
```

**Status is FAIL / reverted, not done, not merge-ready.** Do not report this story complete
until a real identity source exists and a fresh security review passes against it.

## Round 2 — Kemi's scope ruling and the signed session token (2026-10-07)

God relayed Kemi's ruling after reading Zainab's own review file directly: full real browser
authentication (a genuine sign-in flow, server-side session issuance, how it coexists with the
tenant-role-preview UX across the whole web shell) **stays out of scope** — a genuine
cross-cutting feature, its own future story. **In scope**: Zainab's own review already named
the right-sized fix as her third remediation option — "keep demo role switching separate from
real authorization... confine it to an explicitly non-production mode and synthetic backend
tenant/data with no real grants." Concretely: replace the unsigned, client-writable cookie
with a **server-issued, signed, revocable session token**, carrying role AND tenant bound
together by one signature, genuinely invalidated server-side on sign-out, `proxy.ts` validates
and fails closed. **This is fixing a forgeable-session implementation bug, not adding real
authentication or a new ADR decision** (Kemi's own framing) — the disclosed ADR-0006 boundary
is unchanged.

### `web/lib/session-token.ts` (Emeka's half)

Full design in that file's own header; condensed here. Format:
`base64url(JSON(claims)) + "." + hex(HMACSHA256(secret, payloadB64))` — hand-rolled, matching
this codebase's own established convention (no JWT library anywhere; same shape as
BIO-INT-001's `HmacSessionTokenCodec`, BIO-INT-003's `HmacApiAccessTokenCodec`). Claims:
`{ sid, roleId, tenantId, issuedAtMs, expiresAtMs }` — `roleId` and `tenantId` are bound
together by the ONE signature over the whole payload, so neither can be independently forged
or swapped without invalidating the token (Kemi's explicit requirement).

**Its own, brand-new secret** (`WEB_SESSION_TOKEN_SECRET`) — NOT reused from any
`ActorProvenanceSecret` or `ApiSecurity:*` key, same `MINIMUM_SESSION_TOKEN_SECRET_LENGTH`
(32) fail-closed discipline as every other secret in this codebase
(TD-PROVENANCE-DUMMYKEY-SWEEP) — Kemi's own explicit warning not to repeat that mistake a
fourth time in a brand-new mechanism.

**Revocation is file-backed (`web/lib/session-revocation-store.ts`), not in-memory — corrected
after shipping the in-memory version.** The original design used an in-memory module-scope
`Set<string>` of revoked session ids, reasoned to be safe because both `proxy.ts` and the
issuance/sign-out Server Action run on Node.js and so "share one process's memory." **Damilola
found, by hand-driving the real sign-out → replay flow against a real dev server (not just unit
tests), that this was necessary but not sufficient**: she minted a session, confirmed a gated
route returned a correct real proof, signed out through the real UI, then replayed the EXACT
pre-sign-out cookie value directly — it still produced a full valid proof. Root cause, confirmed
by tagging the module with a random id and logging it from both code paths: Turbopack compiles
the middleware/proxy bundle and the Server Action bundle as SEPARATE module graphs even within
one Node.js process, so each got its own copy of the `Set` — "same process" does not mean "same
module instance" once a bundler is involved. Neither of us caught this from code-reading alone;
it needed the actual round-trip.

**Fix**: the filesystem is now the shared state layer — write-to-temp-then-rename, read fresh on
every call, the same shape `FileBackedPendingGalleryCleanupStore.cs` (BIO-BIO-009 round 4)
already established in this exact codebase for an identical cross-instance need, not a new
pattern invented here. Fails closed on a store that exists but can't be read/parsed (`isRevoked`
returns `true` — "cannot prove this sid is NOT revoked" — rather than silently treating
corruption as "nothing is revoked"). A regression test
(`tests/frontend/unit/td-web-provenance-bridge-revocation-store.test.ts`) uses
`vi.resetModules()` + a fresh dynamic import to force two genuinely separate module instances
within one test file — the closest a vitest test can get to reproducing the exact
same-process-different-module-graph shape — and proves a revocation recorded through one
instance is visible through the other, which the original `Set`-based design would have failed.

**`proxy.ts`** now calls `verifySessionToken()` against the `biolog-session-token` `HttpOnly`
cookie (never the old `biolog-demo-role` — that cookie is no longer read anywhere). Any
non-`valid` result (missing, malformed, bad signature, expired, revoked, or the secret itself
unconfigured) strips every caller-supplied `X-Actor-Id`/`X-Actor-Provenance`/`X-Tenant-Id`/
`X-Web-Session-Token` header and injects nothing — the same fail-closed shape as before, now
gated on a real cryptographic check instead of an unsigned cookie read.

**`X-Tenant-Id`/`X-Web-Session-Token` forwarding — disclosed, not fully closed, tightened after
Kemi's own ADR-0002 reading.** Kemi's ruling asked the token to carry tenant id "bound together"
with role, closing Zainab's separate note that no tenant identity reaches the backend from web
today either. Checked how the .NET backend actually resolves tenant
(`TenantContextMiddleware.cs`): **only** ADR-0002's `host` channel is implemented, resolved from
the literal HTTP `Host` header via `TenantDomainService` — the `principal`/`service` channels
that could consume an explicit header are unimplemented by any story yet. Rewriting the outgoing
`Host` header to spoof a tenant-specific domain would be real new architecture (DNS/subdomain
correctness) — exactly the "full session wiring" territory Kemi ruled out. God relayed initial
approval for forwarding `tenantId` as a bare `X-Tenant-Id` header, then — after Kemi read
ADR-0002 Decision 1.1 and Decision 7 before ruling — a required tightening: Decision 1.1's
`principal` channel needs a claim that arrives AUTHENTICATED, not merely asserted; Decision 7's
own realization of that channel (device-secret auth) never trusts a request-supplied tenant
claim directly, only ever cross-validates it against an independently, authenticated resolution.
A bare `X-Tenant-Id`, read on its own by some future hurried implementation, is exactly the
"header field trusted as a channel" anti-pattern Decision 1.1 forbids — this proxy deriving it
from a verified token does not make it safe for the BACKEND to trust directly, since the backend
has no way to confirm that derivation happened honestly.

**Fix**: the RAW signed token now travels alongside the derived value, as a new
`X-Web-Session-Token` header. A future `principal`-channel implementation MUST verify this
token's own HMAC signature server-side (a securely-provisioned copy of `WEB_SESSION_TOKEN_SECRET`)
and extract `tenantId` from the VERIFIED claims; `X-Tenant-Id` alone stays a convenience/display
value only, never a trusted resolution source by itself. Per Decision 7's own closing
obligation, whichever future story actually implements this needs Kemi's approval before
landing, same as every other tenant-resolution mechanism in this codebase — this is a
disclosure, not a pre-approval of a design nobody has built yet.

### `web/context/session-actions.ts` (Damilola's half)

This app's first Server Actions (`'use server'`) — `startDemoSession(tenantId, roleId)` mints
a fresh token via `issueSessionToken` and sets it as the `HttpOnly` session cookie (never
`document.cookie` from client JS — that was round 1's entire defect); `endDemoSession()`
genuinely revokes by `sid` server-side before clearing the cookie, called from real sign-out
rather than the old `setSignedOut(true)`-only React state. `session-context.tsx`'s setters
re-issue the token on every role/tenant change, since the two are bound together in one
signature.

### Verification (round 2, Emeka's half)

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — same route count + ƒ Proxy (Middleware)
corepack pnpm --dir tests/frontend test:unit   # 534/536 — same 2 pre-existing bio-ux-005 failures, unaffected
```

`tests/frontend/unit/td-web-provenance-bridge-session-token.test.ts` (13 tests) and
`td-web-provenance-bridge-revocation-store.test.ts` (4 tests, new after the revocation fix —
including the `vi.resetModules()` cross-instance regression test described above): genuine
issue/verify round-trip, unique `sid` per issuance, unconfigured/too-short secret fails closed
and never issues a token, missing/garbage/tampered-payload/wrong-secret tokens all rejected
(tamper test confirms role and tenant are genuinely bound by one signature — forging just the
role claim while keeping the original signature fails), expired tokens rejected, revoked
tokens rejected even with an otherwise-valid signature and expiry, and revoking one session
does not affect a different live one.

Also added `tests/frontend/playwright/td-web-provenance-bridge-live.spec.ts` (opt-in,
`BIOLOG_TDWEBPROV_LIVE_TESTS=1`, same shape as `bio-doc-010-identify-live.spec.ts` — requires
a real running `Biolog.Api` backend) proving against the real backend, through the real proxy:
a missing session, a forged legacy `biolog-demo-role=admin` cookie, caller-supplied
`X-Actor-Id`/`X-Actor-Provenance` headers, and a garbage session cookie are all rejected
identically at `actor_required` (the actor/provenance layer never even reached); a genuinely
signed session passes that layer and fails one step further in, at `tenant_context_missing`
(the separate, disclosed tenant-channel gap above) — proof the signed session's provenance
proof is genuinely verified, not skipped, without overclaiming a permission-check-level proof
this setup cannot honestly produce. Confirmed the exact `actor_required`/`tenant_context_missing`
status/title values directly against `BioInt003Module.cs`'s own `ResolveActor`/`RequireTenant`
code before writing the assertions, and live-probed the real backend's `actor_required` path
with `curl` to confirm it independently of reading the source. A fifth test proves the
revocation fix through the real HTTP layer: a genuine session is proven live
(`tenant_context_missing`, same as the positive case above), revoked via `revokeSession`
(simulating real sign-out), then the EXACT same pre-revocation cookie value is replayed and
now gets `actor_required` — the same rejection a request with no session at all gets, not
merely a stricter one — proving the stolen-cookie-after-sign-out bug Damilola found is
genuinely fixed, not just passing its own unit test in isolation. Not run end-to-end as a
dual-server session in this round (a second `next dev` was already running in this shared
worktree for manual verification — starting a competing one risked disrupting it); documented
with the exact manual run recipe in the spec's own header for whoever runs it next.

## Round 5 — backend-side demo-session tenant confinement (2026-10-07)

Round 3's review (ZB-WEBPROV-02, downgraded to High) confirmed what round 1 and round 2 both
missed from a different angle: three rounds hardened HOW the session token is signed, gated,
and revoked, but never restricted WHO may self-select WHICH role — real web authentication is
explicitly out of scope for this TD, so nothing stops a caller from legitimately asking the
(gated-by-deployment-flag, but not gated-by-caller) issuance action for `admin`. The token's own
`tenantId` claim was also never backend-enforced: the only implemented ADR-0002 channel (`host`)
resolves the REAL tenant from the HTTP `Host` header regardless of anything the token claims, so
"confined to synthetic data" was a doc-comment promise, not a backend guarantee.

**Kemi's first round-4 ruling (Host-header rewrite) was found mechanically unworkable before
being built, not after.** Her proposed fix — `proxy.ts` rewrites the outgoing request's Host
header to one dedicated, pre-provisioned synthetic tenant domain whenever a verified demo
session is present, using ADR-0002's own already-reviewed `host` channel for its intended
purpose — was architecturally sound (`TenantDomainService.ResolveAsync` genuinely is a plain
string lookup, no DNS/cert involvement). But verifying the actual mechanism (reading Next.js's
own compiled source, then an empirical probe) found that `next.config.mjs`'s `rewrites()` AND
an in-code `NextResponse.rewrite()` call both go through the same underlying `httpxy` proxy with
`changeOrigin: true` HARDCODED — it unconditionally resets the outgoing Host header to the
rewrite destination's own address, clobbering anything `proxy.ts` sets. Making the Host-rewrite
idea work at all would have required `proxy.ts` to become a hand-rolled reverse proxy (via
`node:http`, not Next.js's managed forwarding) for the demo-session-confined subset of traffic —
real new attack surface of its own. Reported this to Kemi before building anything, rather than
guessing through a fifth round; **Kemi independently re-verified the finding herself and
reversed her own ruling**, crediting the discipline of checking rather than assuming.

**The actual fix**: a small, narrow addition directly in `api/Biolog.Api/Tenancy/` (not a
feature project — `TenantContext`'s own constructor is `internal` to `Biolog.Api`'s assembly,
BIO-TEN-002-QA-03, so this has to live here, mirroring
`Biolog.Api.Features.DeviceRegistry.ICrossTenantDeviceLookup`'s own placement precedent, ADR-0002
Decision 7). `DemoSessionTenantResolver` independently verifies the SAME signed session token's
HMAC signature server-side — the same cross-stack TS-signer/C#-verifier pattern already proven
for the actor-provenance proof itself, new call site — via the `X-Web-Session-Token` header
(already forwarded by `proxy.ts` since round 2's tightened `X-Tenant-Id` disclosure). If the
signature verifies and the token is unexpired, `TenantContextMiddleware` resolves tenant to ONE
HARDCODED, pre-provisioned synthetic tenant id — never anything derived from the token's own
`roleId`/`tenantId` claims, never the Host header — checked FIRST, unconditionally, before host-
channel resolution, so a verified demo session can never reach a real Host-resolved tenant even
when the request also carries one. Anything without a verified marker resolves exactly as it did
before this resolver existed, untouched.

**Deliberately narrower than a general ADR-0002 Decision 1.1 `principal` channel.** This does
NOT resolve a per-caller tenant from the token's claims — it verifies the signature only as a
yes/no gate ("is this a genuine, unexpired demo session"), then always resolves to the SAME one
tenant regardless of what role or tenant the token itself claims. Self-selecting `admin` is still
possible for demo purposes, but that identity can now never reach anywhere but the one
quarantined tenant — the round-1-through-4 role-selection freedom could never safely restrict is
made harmless by confinement instead. Tagged with its own `"demo-session"` channel name, not
`"principal"`, so a future reader doesn't mistake this for a general capability it isn't.

**No revocation check needed here, and why.** `DemoSessionTenantResolver` has no access to the
web side's own file-backed revocation store (a different process, a different stack) — it
doesn't need one: `web/proxy.ts`'s own `verifySessionToken` already performs the full check
(signature, expiry, revocation, the issuance-enabled flag) before ever forwarding
`X-Web-Session-Token` at all; a revoked or disabled-flag token is stripped web-side and never
reaches this header. The backend's independent check exists so the backend never has to trust
Next.js's own forwarding behaviour as an uncertified boundary, not to duplicate the cross-stack
revocation problem.

**Config**: `WebSessionBridge:TokenSecret` (own, brand-new secret — MUST match the web side's
`WEB_SESSION_TOKEN_SECRET` exactly, same shared-secret shape every `*ActorProvenanceSecret` key
already has; 32-character fail-closed floor, same `TD-PROVENANCE-DUMMYKEY-SWEEP` discipline)
and `WebSessionBridge:SyntheticTenantId` (any syntactically well-formed `ten_` id — confirmed
nothing downstream requires a tenant id to be pre-registered anywhere via BIO-TEN-001/BIO-TEN-002
to function as an ordinary, currently-ungranted tenant). Both unconfigured means this resolver
never activates — every request falls through to host-channel resolution exactly as before this
file existed.

**Needs a new ADR-0002 decision** (cross-cutting, contract-freeze-adjacent territory, same as
Decision 7) — Kemi is drafting the text now that the concrete shape above is settled; this
story's own code does not touch anything under `contracts/` itself, so the JSON-schema
contract-freeze snapshot mechanism needs no bump for this change.

**Damilola's own BIO-TEN-002 domain-registration research (built for the now-abandoned
Host-rewrite approach) is not needed under this design** — host-channel resolution is bypassed
entirely for verified demo-session traffic, so the domain registry is never consulted for this
path. Told her as soon as this became clear; she reverted her own now-unneeded commit cleanly.

### Verification (round 5)

```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj                                    # clean
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj                # 210/210
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj  # 221/223
```

The 2 integration failures (`AuthenticationEndpointsTests.AC02`/`AC05`) are a **pre-existing,
unrelated** gap (`Authentication:SessionTokenSecret` not configured in this ambient shell
environment, BIO-INT-001's own concern, nothing to do with tenancy) — confirmed by reproducing
the identical failure on a clean `develop` checkout with none of this round's changes applied.

11 new unit tests (`Biolog.UnitTests/Tenancy/TenantContextMiddlewareTests.cs`): a genuinely
signed token resolves the configured synthetic tenant with its own `"demo-session"` channel; a
verified demo marker confines resolution EVEN WHEN the request also carries a real verified
Host (the core security property); the token's own claimed `tenantId` has no effect on which
tenant is resolved; a tampered/expired/malformed/wrong-secret token all fall through to host-
channel resolution, never a false confinement; the resolver being unconfigured (or a too-short
secret) never activates regardless of how well-formed a presented token looks.

6 new integration tests (`Biolog.IntegrationTests/DemoSessionTenantResolverEndpointsTests.cs`),
through the real packaged host, hitting BIO-INT-003's own `POST
/api/v1/integrations/service-accounts` (chosen because its exact behaviour at every layer —
`actor_required`/`tenant_context_missing`/`permission_required` — is already fully
characterised, and because no test project has `InternalsVisibleTo` access to seed a real
permission grant anywhere in this codebase, the same disclosed limitation
`ApiSecurityEndpointsTests` already names): no demo session token resolves exactly as before
(`tenant_context_missing`); a genuinely signed token now reaches the REAL permission check
(`permission_required`, 403 — tenant resolution succeeded, never a bare success, never a
privilege escalation); `admin`-claimed and `viewer`-claimed demo sessions land in the identical
confined outcome, proving the self-selected role has no backend effect; tampered/expired/wrong-
secret tokens all fall back to `tenant_context_missing`, never a false confinement.

## Round 6 — the actor-id half: fixed canonical person ids + seed data (2026-10-07)

Ngozi's reverify work on BIO-RPT-004/BIO-TEN-003 (following
`REVERIFY-Seyi-BIO-RPT-004-BIO-TEN-003.md`'s required-closure list) surfaced a blocker round 5
didn't close: `web/lib/demo-identity.ts`'s `deriveDemoActorId(roleId)` minted
`demo-actor-{roleId}` — not a well-formed canonical BIOLOG identifier (ADR-0001 Decision 1).
`CanonicalId.IsPersonId` (bio-idn-001's own copy, consulted by `PersonRoleAssignment`'s
constructor AND by `PermissionService.ActorHasPermissionAsync`/`HasPermissionAsync` before any
RBAC grant lookup ever runs) rejects that shape outright. So even with round 5's tenant
confinement genuinely working, no story could ever get a real "granted" result through this
bridge — only ever the same baked-in rejection, regardless of what grants existed.

Kemi's ruling: a smaller fix than it sounds, no new backend mechanism, folded into the same
ADR-0002 amendment round 5 already owes (this settles the actor-id half of that shape).

**Fix point 1 — `web/lib/demo-identity.ts`'s `deriveDemoActorId`.** Changed from
`` `demo-actor-${roleId}` `` to a lookup against three FIXED, hardcoded `per_<UUIDv7>` literals
(`DEMO_PERSON_ID_BY_ROLE`), one per demo role (admin/staff/viewer). `computeActorProvenance` is
unchanged — it signs whatever this function returns, exactly as before.

**Fix point 2 — seed data, not provisioning logic.** `api/Biolog.Api/Tenancy/
DemoSessionIdentitySeed.cs` (new), wired via a new tiny `DemoSessionIdentitySeedModule`
(`api/Biolog.Api/Modules/`, `DependsOn: ["Tenant", "PersonRegistry", "Rbac"]` — a separate
module rather than folded into `TenantModule`, since `TenantModule` itself has no `DependsOn`
edge and several other modules depend ON it; adding one here would invert that). At host
startup, if `WebSessionBridge:SyntheticTenantId` is configured (the same config key
`DemoSessionTenantResolver` reads — inert otherwise, same fail-closed discipline as every piece
of this TD), seeds three fixed Person rows (bio-idn-001) and three matching
PersonRoleAssignment rows (bio-idn-006) into that one tenant, each person id matching the web
side's own `DEMO_PERSON_ID_BY_ROLE` value, each role chosen by the demo role's own intended
privilege: `admin` → `TENANT_ADMIN` (deliberately NOT `SUPER_ADMIN`, which bypasses the real
grant lookup entirely and would prove nothing), `staff` → `ENROLMENT`, `viewer` → `READ_ONLY`.
Writes directly to the repositories (bypassing `PersonService`/`RoleAssignmentService`, which
both expect an ambient request-scoped tenant and produce an audit trail neither fits a fixed
startup fixture) in exactly the initial state each repository's own `CreateAsync` already
requires — no lifecycle transition needed, idempotent (checks existence first).

**No `RolePermissionGrant` rows seeded here — deliberately out of scope.** That's Ngozi/Tunde's
own reverify obligation on BIO-RPT-004/BIO-TEN-003: provision the minimum grants each story's
own reverify needs against these same three person/role rows, prove authorized workflow
succeeds, prove unprivileged denial, drop the stale 401-only negative.

**Quarantined to the one synthetic tenant only**, same as round 5's own tenant id — confirmed
by a new test proving a request with no genuine demo session token sees nothing (falls through
to `tenant_context_missing` exactly as before this round).

### Verification (round 6)

```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj                                    # clean
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj                # 210/210
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj  # 226/227
npx vitest run (tests/frontend)                                                  # 542/544
npx tsc --noEmit (web)                                                           # clean
```

The 1 integration failure (`ObservabilityTests.AC01`) and the 2 frontend failures
(`bio-ux-005-feature-mode`/`bio-ux-005-module-registry`) are **pre-existing, unrelated** —
the former a known `Console.Out`-redirect race against concurrent `WebApplicationFactory`
hosts (confirmed non-reproducing in isolation), the latter `TD-BIO-UX-005-MODULE-REGISTRY-
FIXTURE-GAP`.

Updated `tests/frontend/unit/td-web-provenance-bridge-identity.test.ts` (the existing
`deriveDemoActorId` test asserted the OLD `demo-actor-{roleId}` shape and would otherwise now
fail): added a direct proof against the REAL contract validator
(`contracts/js/identifiers.mjs`'s `isCanonicalId`/`parseId`), not a reinvented regex, so this
test fails loudly if a future change reintroduces a non-canonical actor id.

4 new integration tests (`Biolog.IntegrationTests/DemoSessionIdentitySeedEndpointsTests.cs`),
through the real packaged host, hitting BIO-IDN-006's own `GET /api/v1/persons/{personId}/
roles` (chosen because it requires no actor/provenance headers at all, only a resolved tenant —
proves the seeded rows exist directly without the "no test project has `InternalsVisibleTo`
access to seed a real permission grant" limitation round 5's own tests disclose): each of the
three seeded person/role pairs is visible through the real host under a genuine demo session;
with no demo session token, the same person id is unreachable (`tenant_context_missing`),
proving confinement.
