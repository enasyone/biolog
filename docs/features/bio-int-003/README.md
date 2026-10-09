# BIO-INT-003 — Implement API service accounts, OAuth2 and rate limits

As integrators, we need service authentication and authorization for BIOLOG REST APIs.

**Feature project:** `api/features/bio-int-003/Biolog.Features.ApiSecurity` (namespace
`Biolog.Features.ApiSecurity`), the same separate-project, one-way-dependency convention
`BIO-DEV-003`/`BIO-RPT-001`/`BIO-INT-001` already established. Registered into the host via
`api/Biolog.Api/Modules/BioInt003Module.cs`.

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — service accounts are tenant-scoped | `IServiceAccountRepository`, every management method takes an explicit `tenantId`; not-found-not-forbidden across tenants | `ServiceAccountServiceTests.cs`, `ApiSecurityEndpointsTests.cs` |
| AC02 — OAuth2/token scopes map to permissions | `POST /api/v1/oauth/token` (client_credentials grant); scopes are BIOLOG's own closed 10-role vocabulary (`KnownRoles`, `SuperAdmin` deliberately excluded - see "Round 4" below), validated at creation and at grant time | `ServiceAccountServiceTests.cs`, `ApiAccessTokenCodecTests.cs`, `ApiSecurityEndpointsTests.cs` |
| AC03 — rate limits and API audit exist | `IApiRateLimiter` (tenant+client+route fixed-window limiter, 429 + `Retry-After`); `IApiAuditSink` (every token issuance attempt, rotation, revocation and rate-limit rejection audited) | `RateLimitingTests.cs`, `ApiAuditTests.cs`, `ServiceAccountServiceTests.cs`, `ApiSecurityEndpointsTests.cs` |
| AC04 — API keys, if enabled, are hashed/rotatable and never logged | `IApiSecretHasher` (HMAC-pepper hash, never the plaintext, stored); `POST .../rotate-secret` issues a fresh secret and invalidates the old one; the plaintext is returned exactly once, in the create/rotate HTTP response only | `ApiAccessTokenCodecTests.cs`, `ServiceAccountServiceTests.cs`, `ApiSecurityEndpointsTests.cs` |
| AC05 — `/administration/integrations`, `/settings/authentication` connected via `IntegrationService` or documented HYBRID | Real, live service-account lifecycle (`ServiceAccountsPanel`) mounted on `/administration/integrations`, additive alongside BIO-INT-002's own `ImportConsole` | See "AC05" below |

## Why a separate feature project

Mirrors BIO-INT-001's own reasoning: this project cannot reference `Biolog.Api.csproj`
(one-way dependency, `scripts/check-module-project-references.sh`), so every method that needs
a tenant takes `string tenantId` as a plain parameter - `BioInt003Module` resolves the real
`ITenantContextAccessor` and passes it down.

It also cannot reference `bio-idn-006`'s own feature project (BIO-IDN-006's RBAC role model),
nor `bio-int-001`'s (its own `KnownRoles.cs`) - only `Biolog.Api` may hold a `ProjectReference`
to a feature project. `Domain/KnownRoles.cs` is this project's own closed, documented copy of
BIO-IDN-006's eleven `RoleType` wire strings by VALUE - a THIRD independent duplication (after
BIO-INT-001's own), the same "duplicate the minimal shape" boundary pattern `BIO-DEV-003`'s
`DeviceDescriptor` and `BIO-RPT-001`'s `DomainEventEnvelope` already established. A service
account's granted scopes - and any scope requested at grant time - are rejected unless every
one names one of these eleven strings.

## OAuth2 scopes are role-shaped labels, mapped to domain permissions via BIO-IDN-006's own live grants

AC02 asks for "OAuth2/token scopes map to permissions." A service account's scopes are drawn
from BIO-IDN-006's own closed 11-role catalogue (`KnownRoles`, duplicated by value - see above)
- a granted/requested scope string `"Gate"` IS the role `Gate`, the same vocabulary a human
sign-in's `ClaimMappingRule.MappedRole` already targets (BIO-INT-001-AC02). **Round 2 treated
the role label itself AS the permission** (`effectiveScopes.Contains("ReadOnly")`) - Zainab's
round-3 review correctly rejected this: a role label is not a domain permission, and BIO-IDN-006's
own `RolePermissionGrant` makes which permissions a role carries an explicit, PER-TENANT,
independently-configurable fact (fail-closed by default - an absent grant means NOT granted).

**Round 3 fix: scopes map TO domain permissions via BIO-IDN-006's real, live, tenant-scoped
grant store - never a label match.** `/api/v1/integrations/service-accounts/whoami` requires a
namespaced domain permission (`ServiceAccountService.WhoamiPermission`,
`"integrations.service_account_whoami"`), resolved by checking EVERY one of the token's live
effective scopes (role labels) against `IRolePermissionGateway` - which `BioInt003Module.cs`
implements by calling BIO-IDN-006's own `IRolePermissionGrantRepository.GetAsync(tenantId,
roleType, permissionKey, ct)` DIRECTLY. A tenant can grant or deny this exact permission to
`ReadOnly`, `Gate`, or any other role independently of what this story hardcodes - proven by
tests showing the SAME role label produce different allow/deny results purely because the
tenant's own grant changed, and that a grant in one tenant never satisfies a token for another.

**Why `IRolePermissionGrantRepository` directly, not `PermissionService.GetGrantAsync`:** the
SERVICE-layer method resolves its tenant AMBIENTLY via `ICurrentTenantProvider` (the Host-header
channel every browser/admin request uses) - an OAuth2 bearer-token caller never goes through
that channel at all; the TOKEN's own embedded tenant id is the tenant, by design. ADR-0002
Decision 3 makes `ITenantContextAccessor.Context` read-only specifically so no consumer can swap
the ambient tenant mid-request to work around this, so that path was never available. The
REPOSITORY layer, unlike the service layer, already takes `tenantId` as an explicit parameter -
exactly the shape this non-Host-channel consumer needs, with zero changes to BIO-IDN-006's own
code. **Round 4 update:** `RoleType.SuperAdmin`'s own domain invariant (never needs or has a
grant record) was briefly mirrored in the adapter after round 3; Zainab's round-4 review
(ZB-INT003-07) found that unsafe for a non-human credential and it has since been removed - see
"Round 4" below.

**Disclosed test limitation, not a shortcut.** This project (like BIO-ACC-001/BIO-IDN-006's own
host-channel tests) has no `InternalsVisibleTo` access to seed a real `RolePermissionGrant` row
from any test project - a tenant's first privileged grant is, by BIO-IDN-006's own design, an
out-of-band operation no public endpoint performs (`RoleAssignmentService`'s own header: "a
tenant's very first SUPER_ADMIN can therefore never be created through this endpoint at all - a
deliberate, disclosed consequence"). So: the full allow/deny/same-role-different-grant/cross-
tenant matrix is proven at the UNIT level via a test-only `IRolePermissionGateway` double
(`FakeRolePermissionGateway`); at the REAL HTTP layer, the DEFAULT-DENY path is proven honestly
(a scope with no real grant in a fresh tenant is rejected - meaningfully different from round
2's behavior, since `"ReadOnly"` no longer automatically passes). **Round 4 update:** rounds 1-3
also proved the real-HTTP ALLOW path using `SuperAdmin`'s own implicit-grant domain invariant;
Zainab's round-4 review found that shortcut itself was the vulnerability (see "Round 4" below),
so that proof has been removed and not replaced - the real-HTTP ALLOW path for a genuine tenant
grant remains undemonstrated end-to-end pending a narrow, production-real seeding fixture this
project does not yet have (same disclosed `InternalsVisibleTo` wall, now with no safe shortcut
around it). The ALLOW path stays proven only at the unit level.

What remains a genuinely separate, narrower, still-undone piece: calling
`PermissionService.HasPermissionAsync` from inside `BioInt003Module.cs` (visible via
`InternalsVisibleTo`) to cross-check a scope against a HUMAN actor's own live permission grants
- relevant only if a future story wants a service account's effective authority to be bounded by
the creating admin's own current permissions, not merely by what was granted at creation time.
No consuming story has asked for that; it is not part of AC02's text.

## No JWT/session ADR or dependency exists yet - confirmed, not assumed

Same precedent BIO-INT-001 already established (searched the whole repository before
designing this story, same result): no ADR, no `contracts/schemas/` file, and no NuGet
dependency anywhere mentions JWT as a concrete mechanism. This story's own access token
(`Domain/ApiAccessTokenCodec.cs`, `HmacApiAccessTokenCodec`) is a hand-rolled HMAC construction,
the same shape BIO-INT-001's `HmacSessionTokenCodec` and BIO-IDN-006's own `ActorProvenanceGate`
use - config-only secret, constant-time comparison - with ONE deliberate difference (see
"Security-review fixes" below): no dummy-key fallback. It is a SEPARATE HMAC key from
BIO-INT-001's session-token secret, and a separate key again from `HmacApiSecretHasher`'s
pepper - a leaked key in one must not compromise the other two.

## Rate limiting: genuinely new ground

Searched the whole repository before designing (`grep -ri ratelimit` across `api/` and
`docs/`): no ASP.NET Core `RateLimiter` usage, no existing `RateLimit*` class, no precedent
anywhere. `Domain/RateLimiting.cs`'s `InMemoryApiRateLimiter` is a fixed-window limiter keyed
by `RateLimitKey` - a value-equality `record struct` of `(TenantId, ClientId, Route)`, not a
joined string. BIO-INT-002's own remediation history (`docs/features/bio-int-002/README.md`)
found that a bucket/fingerprint key built by naively joining fields without a collision-proof
separator can let two different logical buckets collide; a record struct's value equality has
no such ambiguity. Applied to `POST /api/v1/oauth/token` (BEFORE the secret is even checked -
a credential-stuffing attempt must not get unlimited guesses just because each guess uses a
different `client_id`) and to `GET .../whoami` (so the same token can be rate-limited
independently per protected route).

Default limit: 60 requests/minute per `(tenant, client, route)`, configurable via
`ApiSecurity:RateLimitPerMinute`. A rejected request returns HTTP 429 with a `Retry-After`
header (ADR-0004's problem-response envelope) and is itself an audited event
(`ApiAuditAction.RateLimitExceeded`).

## API keys: hashed, rotatable, never logged (AC04)

A service account's secret is generated once (`ApiSecretGenerator`, `RandomNumberGenerator`,
never derived from the account id or any other public value - the same lesson BIO-INT-001-F01
established: a secret must never be computable from something the response itself also
reveals). Only its HMAC-pepper hash (`HmacApiSecretHasher`) is ever persisted
(`ServiceAccount.SecretHash`); the plaintext is returned to the caller exactly once, in the
create or rotate HTTP response body, and is never written to any audit event, log statement,
or the list-accounts response. Rotation (`POST .../rotate-secret`) generates a fresh secret and
hash under the same optimistic-concurrency (`ExpectedVersion`) discipline BIO-INT-001's
`AuthProviderConfig.UpdateStatusAsync` already established, invalidating the old secret
atomically. Revocation (`POST .../revoke`) is terminal - a revoked account is never
reactivated; integrators create a new account instead, the simpler and safer shape for a
foundation story.

## AC05: the live web connection

`/administration/integrations` (route-manifest.ts's `IntegrationService` mapping) already
renders BIO-INT-002's own `ImportConsole` as the page's full content. Rather than redesigning
that approved surface, this story adds a new, additive "API Credentials" panel
(`web/features/bio-int-003/ServiceAccountsPanel.tsx`) mounted ALONGSIDE it (never replacing
it) - the same "new keys, new panel, existing panel untouched" shape `BIO-RPT-003`'s own
`AuditInvestigationPanel` already established for `/audit` next to `BIO-RPT-001`/`BIO-RPT-002`'s
existing content.

Four new, independently mode-switchable `IntegrationService` port keys back the panel:
`listServiceAccounts`, `createServiceAccount`, `rotateServiceAccountSecret`,
`revokeServiceAccount` (`web/lib/feature-mode.ts`'s own `MODULE_PORT_KEYS`/
`PER_PORT_ENV_DEFAULTS`). Each genuinely calls this story's own real
`/api/v1/integrations/service-accounts/*` backend in LIVE mode - a human can create a service
account, see its one-time secret, rotate it, or revoke it, end to end, exactly as `/administration/
integrations` already demonstrates for BIO-INT-002's import workflow.

**`/settings/authentication` is deliberately left untouched.** Its own primary actions ("Save
authentication policy", "Test sign-in") are interactive human sign-in/session-policy concerns -
BIO-INT-001's own domain (already disclosed as out of scope by BIO-INT-002-AC06's own HYBRID
note), not this story's API service-account/OAuth2 concern. This story's own mapped surface is
`/administration/integrations` alone; `/settings/authentication` staying SIMULATION is a
pre-existing, already-disclosed boundary this story does not need to re-justify or close.

The OAuth2 client_credentials grant and per-route rate limiting (AC02/AC03) have no UI action of
their own - `route-manifest.ts` names no "get a token"/"test rate limit" primary or secondary
action for either mapped route - so they are exercised directly against
`/api/v1/oauth/token` and `/api/v1/integrations/service-accounts/whoami`
(`tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs`), not through any page.

## Security-review fixes (Zainab, round 1 - `evidence/features/bio-int-003/codex/SECURITY-REVIEW-ZAINAB.md`)

**ZB-INT003-01 (Critical) - dummy-key fallback enabled token/secret forgery.** Before this fix,
`HmacApiAccessTokenCodec`/`HmacApiSecretHasher` fell back to a deterministic key derived from a
public source string when unconfigured - Zainab's own test proved two independently-unconfigured
instances accepted each other's tokens, meaning anyone who read this source could forge a valid
token (or secret hash) for any tenant/client/scope with no configuration mistake beyond "forgot to
set a secret." **Fixed**: both types now fail closed - `Issue()`/`Hash()` throw
`InvalidOperationException`, `TryValidate()`/`Verify()` always return `false`, whenever the
configured value is missing OR shorter than 32 characters (`MinimumSecretLength`/
`MinimumPepperLength`). No secret value is committed to source anywhere (checked-in
`appsettings*.json` declare neither `ApiSecurity:AccessTokenSecret` nor
`ApiSecurity:SecretHashPepper`) - a real deployment supplies both via environment variable or
secret store, the same out-of-band provisioning path BIO-ACC-001's/BIO-IDN-006's own
`ActorProvenanceSecret` already established; a test host supplies synthetic values via
`ConfigureAppConfiguration`, never a checked-in file.

**ZB-INT003-02 (Critical) - service-account administration had no actor or authorization gate.**
Create/list/rotate/revoke previously required only ambient tenant context - any caller reaching a
tenant plane could create a SuperAdmin-scoped account, read any account's metadata, rotate another
integrator's secret, or revoke it. **Fixed**: every one of those four operations now resolves a
verified actor (`Api/ActorResolver.cs` + `Api/ActorProvenanceGate.cs`, the exact ADR-0006 Decision
3 seam BIO-ACC-001/BIO-IDN-006 already established, copied here rather than shared - this project
can reference neither sibling feature project) and checks a live tenant permission via a new
`IPermissionGateway` seam, wrapping BIO-IDN-006's own `PermissionService.ActorHasPermissionAsync`
inside `BioInt003Module.cs` (the one place this project's host can see that internal type). Two
new permission keys: `integrations.service_account_manage` (create/rotate/revoke),
`integrations.service_account_read` (list). Unauthenticated, forged-provenance and
unprivileged-actor negatives are proven at the real HTTP layer
(`tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs`); this project has no
`InternalsVisibleTo` access to seed a real permission grant (same disclosed limitation as
BIO-ACC-001's/BIO-IDN-006's own host-channel tests), so the POSITIVE/granted path is proven at the
unit level (`ServiceAccountServiceTests.cs`, via a test-only `IPermissionGateway` that grants).

**ZB-INT003-04 (High) - a failed audit write after a mutation could invalidate a secret the
caller never received.** Create/rotate/revoke previously mutated the repository FIRST and wrote
audit afterward; an audit failure (or cancellation) between those two steps could leave a secret
rotated with the caller never receiving the new plaintext, a true lockout. **Fixed**: the audit
event is now written BEFORE the repository mutation, using BIO-INT-001's own
write-or-throw-with-`ExistsAsync`-recheck discipline; a genuine audit failure now means NOTHING
was mutated (old secret stays valid, no account created, no account revoked) rather than a
mutation with no record of it. The repository call that follows a successful audit write uses
`CancellationToken.None` - once an audit record exists, a caller's cancellation must not interrupt
the one write that record is now a correct account of.

**ZB-INT003-03 (High, round 1) - token scopes still don't enforce live permissions.** Round 1
left this as a documented, named follow-up per ADR-0006 Decision 5's own precedent for
BIO-INT-001. **Round 2: Zainab explicitly rejected that framing** - an approved acceptance
criterion cannot be waived by an ADR citation alone. Actually fixed in round 2; see below.

## Round 2 (Zainab, round-2 review - `evidence/features/bio-int-003/codex/SECURITY-REVIEW-ZAINAB-ROUND2.md`)

**ZB-INT003-05 (Critical) - the ZB-01 fix's own copied code carried the SAME bug forward.**
`HmacActorProvenanceStore` (the `Api/ActorProvenanceGate.cs` seam copied from BIO-ACC-001/
BIO-IDN-006) used `SHA256("no-provenance-secret-configured")` as its dummy key when
unconfigured - a literal string checked into source, confirmed copy-pasted across ~12 other
already-merged stories (escalated floor-wide separately). Anyone could compute
`HMAC-SHA256(dummyKey, anyPrivilegedActorId)`, pass this gate while asserting that actor's id,
and have the application query that actor's real permission grants - recreating the exact
impersonation bypass the gate exists to close. **Fixed**: the same fail-closed discipline as
ZB-01's codec/hasher - NO fallback key at all; `Verify()` always returns `false` below a
32-character configured secret. An adversarial test computes the FORMER dummy-key-derived proof
for a privileged actor id and confirms it is now rejected (401) end to end.

**ZB-INT003-03 (High, round 1 deferral rejected) - partially fixed this round, found incomplete
in round 3 (see "Round 3" below).** `AuthorizeAsync` now re-reads live account state (revocation,
current granted scopes) on every call instead of trusting a token's embedded claims, and
`/whoami` required a literal role label (`"ReadOnly"`) among the token's live effective scopes.
Proven: a revoked account's still-unexpired token is rejected at the real HTTP layer; a token
missing the required scope is rejected with 403 `insufficient_scope`; a scope withdrawn from the
account after token issuance stops being honored on the very next call. **This live re-read
discipline was necessary and remains in the round-3 fix unchanged** - round 3 found it was not
SUFFICIENT: checking a role label against itself is not the same as mapping a scope to a domain
permission via BIO-IDN-006's real tenant-scoped grants.

**ZB-INT003-06 (Medium) - rotate/revoke wrote a "completed" audit event before checking
existence/version, producing a false record on 404/409.** The round-1 audit-before-mutation fix
(ZB-04) correctly stopped losing a mutation's own result to an audit failure, but still audited
UNCONDITIONALLY before the repository's own existence/version check could run - a 404 or 409
left a durable "SecretRotated"/"ServiceAccountRevoked" event for something that never happened.
**Fixed**: `RotateSecretAsync`/`RevokeAsync` now look up the account and compare its version to
`expectedVersion` BEFORE writing any audit event; a 404 or 409 is detected and returned/thrown
with NO audit record at all. A narrow race remains between this pre-check and the repository's
own re-check under its internal lock (genuinely simultaneous requests) - the same "no shared
transactional substrate across stores" residual BIO-INT-002's own remediation history already
disclosed and god accepted as proportionate; this fix closes the common, exploitable case (a
caller probing nonexistent ids or replaying a stale version), not a theoretical simultaneous-
request race.

## Round 3 (Zainab, round-3 review - `evidence/features/bio-int-003/codex/SECURITY-REVIEW-ZAINAB-ROUND3.md`)

Round 3 confirmed ZB-05/ZB-06/the live-revocation-and-rescope-recheck half of ZB-03 all closed
cleanly (Zainab's own words: "Revoked accounts and withdrawn scope labels are re-read on every
protected call"), and separately validated the ZB-05 fix's EXPLICIT-BRANCH design (no fallback
key, `MinimumSecretLength` threshold) as not just safe but preferable to the floor-wide sweep's
random-dummy-key approach - it also closes a weak/typo'd-secret gap the sweep's version would
have missed (a short configured secret silently works as a real HMAC key under a random-key
design; this project's explicit length check rejects it outright). Kemi is normalizing all 13
swept files to this pattern; no action needed from this story on that.

**ZB-INT003-03 (High) - still blocking: role labels are not domain permissions.** See "OAuth2
scopes are role-shaped labels, mapped to domain permissions via BIO-IDN-006's own live grants"
above for the full fix and its disclosed test-seeding limitation. Condensed: declaring a role
label to BE a permission (what round 2 shipped) conflicts with BIO-IDN-006's own authoritative
model - `RolePermissionGrant` explicitly defines which permission keys a role carries as
PER-TENANT configuration, with an absent grant denying by default; this code was ignoring that
mapping and authorizing solely from the role-shaped token string. Fixed by resolving a real
namespaced domain permission (`WhoamiPermission`) against BIO-IDN-006's own
`IRolePermissionGrantRepository`, called directly (not through `PermissionService`'s
ambient-tenant-bound convenience wrapper, which cannot serve a non-Host-channel OAuth2 bearer
token caller at all).

## Round 4 (Zainab, round-4 review - `evidence/features/bio-int-003/codex/SECURITY-REVIEW-ZAINAB-ROUND4.md`)

Round 4 confirmed round 3's `IRolePermissionGateway` fix correct for every ordinary role, and
raised one new blocking finding.

**ZB-INT003-07 (High) - `SuperAdmin` as an issuable service-account scope was itself a
privilege-escalation primitive.** Round 3 mirrored `RoleType.SuperAdmin`'s own BIO-IDN-006 domain
invariant ("never needs or has an explicit grant record") into `RolePermissionGatewayAdapter`,
the same way `PermissionService.HasPermissionAsync` already short-circuits true for it. That is
correct for a HUMAN role assignment, gated by `RoleAssignmentService`'s own tightly-controlled
endpoint - but a service account is mintable by any actor holding only the narrow
`ServiceAccountService.ManagePermission`, with no equivalent gate on WHICH scopes they may
request. An actor who can only manage service accounts could therefore mint a credential scoped
`SuperAdmin`, which would then auto-pass every current and future domain-permission check with
no tenant grant ever consulted - exactly the "blanket tenant-admin token for integrations" the
API architecture's own rule forbids. Zainab's review also noted that the round-3 real-HTTP ALLOW
proof relied on precisely this shortcut, since it was the only production-real way to pass a
permission check without a seeding capability this project lacks.

**Fixed at the root, not after the fact.** `KnownRoles.WireFormats` now excludes `SuperAdmin`
entirely (ten role strings remain) - `IsKnown` rejects it at service-account creation time the
same way it already rejects any string outside this closed set, so the escalation path is closed
before a token can ever be issued, not merely constrained once issued. The adapter's own former
short-circuit for `RoleType.SuperAdmin` was removed as well (unreachable once creation-time
validation holds, but removed rather than left as dead code implying the adapter still honors
that case). BIO-IDN-006's own catalogue and `RoleAssignmentService` are unchanged - a human can
still be assigned `SuperAdmin`; only this project's own closed OAuth2-scope vocabulary excludes
it.

**Consequence for testability, disclosed rather than worked around.** Removing the shortcut also
removes the only production-real way this project had to prove a real-HTTP permission ALLOW
without a seeding fixture (see the updated "Disclosed test limitation" paragraph above) - the
ALLOW path is now proven only at the unit level via `FakeRolePermissionGateway`. Two new tests
replace the removed SuperAdmin-reliant proof: `Creating_an_account_rejects_SuperAdmin_as_a_scope`
(the root fix, unit level) and
`A_service_account_manager_cannot_mint_a_token_for_a_permission_they_were_never_authorised_to_delegate`
(a manager holding only `ManagePermission` cannot mint a token that passes an unrelated
permission check) plus `ZB07_SuperAdmin_is_rejected_as_a_service_account_scope_at_the_real_http_layer`
(the real HTTP layer rejects `SuperAdmin` at creation with `400 unknown_scope`).

Verified: dotnet build clean. 62/62 feature unit tests (was 61, +2/-1 net: removed the
SuperAdmin-free-pass test, added the two above). 207/207 integration tests (re-pointed four
SuperAdmin-reliant tests to real, already-valid scopes or new ZB-07 assertions; one unrelated,
pre-existing `ObservabilityTests` failure reproduced once in a 207-test run and passed cleanly
both in isolation and on a full-suite re-run - a flake, not a regression from this fix). 200/200
unit tests, no regression. 64/64 structural checks (was 59, +5 new ZB-07 checks, 1 repointed to
the renamed AC03 test).

## Scope boundaries

- No other story's feature project, `web/features/integrations/**`, or
  `web/app/{administration/integrations,settings/authentication}/**` content was redesigned -
  only additive files (`web/features/bio-int-003/**`, a two-line mount in
  `web/app/administration/integrations/page.tsx`, new `MODULE_PORT_KEYS`/
  `PER_PORT_ENV_DEFAULTS` entries in `web/lib/feature-mode.ts`).
- Token scopes ARE now mapped to and enforced as real domain permissions (ZB-INT003-03, round 3,
  see "OAuth2 scopes are role-shaped labels..." above) via BIO-IDN-006's own tenant-scoped
  `RolePermissionGrant` - not a bare role-label string match (round 2's own, since-rejected
  shortcut). The one genuinely separate, still-undone piece is cross-checking a scope against a
  HUMAN actor's own BIO-IDN-006 `PersonRoleAssignment` (as opposed to the ROLE-centric grant this
  story now checks) - relevant only if a future story wants a service account's authority bounded
  by its creating admin's own current permissions; not part of AC02's approved text.
- Service-account management endpoints require a verified actor + a live permission grant
  (`X-Actor-Id`/`X-Actor-Provenance`, ADR-0006 Decision 3), run inside the normal tenant-admin web
  session - a DIFFERENT mechanism from the OAuth2 bearer access token the grant/`whoami` endpoints
  consume. A caller never needs both at once for the same request.
