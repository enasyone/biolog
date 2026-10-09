# BIO-INT-001 — Implement authentication provider abstraction

As tenants, we need selectable BIOLOG password, Entra ID/External ID, SAML2, OIDC and optional
Google/LDAP adapters.

**Feature project:** `api/features/bio-int-001/Biolog.Features.Authentication` (namespace
`Biolog.Features.Authentication`), the same separate-project, one-way-dependency convention
`BIO-DEV-003`/`BIO-RPT-001` already established. Registered into the host via
`api/Biolog.Api/Modules/BioInt001Module.cs`.

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — provider configuration is tenant-scoped | `IAuthProviderConfigRepository`, every method takes an explicit `tenantId`; not-found-not-forbidden across tenants | `ProviderConfigRepositoryTests.cs` (6 tests), `AuthenticationEndpointsTests.cs` |
| AC02 — no provider-specific claims leak into authorization without mapping | `ClaimMappingService.MapClaims` - exact-match rules only, no wildcard/default; role names validated against a closed, known set at registration | `ClaimMappingTests.cs` (9 tests), `AuthenticationServiceTests.cs`, `AuthenticationEndpointsTests.cs` |
| AC03 — MFA policy hooks exist | `IMfaChallengeProvider` interface + `AuthProviderConfig.Mfa`; `AuthenticationService` never issues a session before a required challenge is verified | `MfaChallengeProviderTests.cs` (5 tests), `AuthenticationServiceTests.cs`, `AuthenticationEndpointsTests.cs` |
| AC04 — authentication failure and account-link events are audited | `IAuthAuditSink`; every branch of `AuthenticationService` writes exactly one audit event before returning; first-time account link audited once, reused silently thereafter | `AuthenticationServiceTests.cs`, `AuthenticationEndpointsTests.cs` |
| AC05 — `/administration/integrations`, `/settings/authentication` connected via `IntegrationService` or documented HYBRID | Real, live `POST /api/v1/integrations/primary-action` — the exact path `web/features/integrations/live.ts` already targets | See "AC05" below |

## Why a separate feature project

Mirrors `BIO-RPT-001`'s own reasoning: this project cannot reference `Biolog.Api.csproj`
(one-way dependency, `scripts/check-module-project-references.sh`), so every method that needs
a tenant takes `string tenantId` as a plain parameter - `BioInt001Module` resolves the real
`ITenantContextAccessor` and passes it down.

It also cannot reference `bio-idn-006`'s own feature project (BIO-IDN-006's RBAC role model) -
only `Biolog.Api` may hold a `ProjectReference` to a feature project, and even the host can
only reach BIO-IDN-006's `PermissionService`/`RoleAssignmentService` because they are
`internal` with `InternalsVisibleTo` scoped to `Biolog.Api` and BIO-IDN-006's own tests, not to
a sibling feature project. `Domain/KnownRoles.cs` is this project's own closed, documented copy
of BIO-IDN-006's eleven `RoleType` wire strings by VALUE - a deliberate duplication, not a live
reference, the same "duplicate the minimal shape" boundary pattern `BIO-DEV-003`'s
`DeviceDescriptor` and `BIO-RPT-001`'s `DomainEventEnvelope` already established. A
`ClaimMappingRule`'s `MappedRole` is rejected at registration time (`BioInt001Module`, HTTP 400
`unknown_role`) unless it names one of these eleven strings.

## No JWT/session ADR or dependency exists yet - confirmed, not assumed

Searched the whole repository before designing this story: no ADR, no `contracts/schemas/`
file, and no NuGet dependency anywhere mentions JWT, OIDC, SAML, sessions or tokens as a
concrete mechanism. `docs/features/bio-crd-007/README.md`'s own AC03 note already states this
explicitly for its own signed-package feature ("a hand-rolled HMAC token, not a JWT library...
no JWT dependency exists anywhere in this codebase yet, and no ADR mandates one"). This story's
own session token (`Domain/SessionToken.cs`, `HmacSessionTokenCodec`) follows that exact
precedent rather than introducing a new library dependency and vulnerability-scan surface for a
foundation story - the same HMAC construction (config-only secret, fails closed to a dummy key
when unconfigured, constant-time comparison) BIO-IDN-006's own `ActorProvenanceGate` already
uses for its own interim provenance proof.

## Provider adapters: one per selectable identity provider

`Domain/IAuthProviderAdapter.cs` + one class per `AuthProviderType` in `Adapters/`:
`PasswordProviderAdapter` (BIOLOG's own native provider, backed by a synthetic in-memory
`ICredentialStore` - PBKDF2-hashed, constant-time verified, never plaintext), and
`EntraIdProviderAdapter`/`Saml2ProviderAdapter`/`OidcProviderAdapter`/`GoogleProviderAdapter`/
`LdapProviderAdapter` - all five federated adapters deterministically simulated (no real Entra
ID tenant, SAML endpoint, OIDC discovery document, Google OAuth or LDAP directory exists to
call from this sandboxed codebase), the same precedent `BIO-RPT-001`'s channel adapters and
`BIO-DEV-003`'s vendor SDK simulators already established for "prove the adapter boundary with
a deterministic marker, not a hardcoded always-success stub." `AuthenticationService`
dictionary-dispatches by `AuthProviderType` and never branches on the enum itself.

## AC01: tenant-scoped, not-found-not-forbidden

`IAuthProviderConfigRepository.GetAsync` returns `null` for a provider that does not exist AND
for one that exists in a different tenant (ADR-0002 §4.3) -
`ProviderConfigRepositoryTests.A_tenant_cannot_read_another_tenants_provider_by_id` and
`AuthenticationEndpointsTests.AC01_a_provider_registered_under_one_tenant_is_invisible_to_another`
prove this at both the repository and the real HTTP layer.

## AC02: exact-match claim mapping, a closed role set, and a proven negative

`ClaimMappingService.MapClaims` has no wildcard, regex or default-role path - a raw claim only
ever becomes a role via an exact `(ClaimType, ClaimValue)` match against a rule the tenant's own
config declares.
`ClaimMappingTests.An_unmapped_claim_contributes_no_role` and
`A_hostile_claim_type_that_looks_like_a_privileged_group_never_leaks_a_role_without_a_rule`
prove the negative directly, not just the positive path.
`AuthenticationEndpointsTests.AC02_end_to_end_authentication_only_grants_explicitly_mapped_roles`
proves the same thing through the real HTTP pipeline, including a claim deliberately left
unmapped in the same login.

## AC03: MFA is a real gate, not a decorative field

`AuthProviderConfig.Mfa == MfaRequirement.Required` makes `AuthenticationService.AuthenticateAsync`
return `AuthenticationOutcomeKind.MfaRequired` with **no session token anywhere in that
response** - `Mfa_required_config_never_issues_a_session_before_the_challenge_is_verified`
asserts `outcome.Principal` is `null` on that path. A session is only issued by
`CompleteMfaAsync` after `IMfaChallengeProvider.VerifyAsync` returns `true`, and a challenge is
single-use even when the first attempt at it was itself wrong
(`A_challenge_is_single_use_even_when_the_first_attempt_was_wrong`).

**Security-review fix, BIO-INT-001-F01 (Zainab, High):** an earlier revision derived the MFA
code as a fixed SHA-256 prefix of the challenge id - since the challenge id is exactly what
every legitimate `mfa_required` HTTP response hands to its caller, ANY caller (not just the
real subject) could compute the "second factor" themselves, defeating MFA entirely. Fixed by
making `InMemoryMfaChallengeProvider.IssueChallengeAsync` generate the code with
`RandomNumberGenerator` - independent random state with no formula connecting it to the
challenge id or anything else externally observable - and returning it ONLY to the direct
in-process caller of that method, never through any HTTP response. `AuthenticationOutcome`'s
own `DemoOnlyMfaCode` property is `internal` (visible only to this assembly and `Biolog.Api`
via `InternalsVisibleTo`) and is populated solely so this story's own AC05 demo endpoint - a
fully server-side, same-request flow that issues its own challenge and completes it in the same
call - can finish its demonstration without a real out-of-band transport existing yet; it is
structurally unreachable from any external caller (internal visibility, System.Text.Json's
default non-serialization of non-public members, and `BioInt001Module`'s own explicit
field-whitelisted HTTP mapping are three independent reasons, not one).

`An_attacker_who_knows_only_the_challenge_id_cannot_derive_the_code`
(`MfaChallengeProviderTests.cs`) proves the old vulnerable formula no longer reconstructs the
real code, and `AuthenticationEndpointsTests.An_attacker_with_only_the_http_visible_challenge_id_cannot_verify_mfa`
proves the same thing at the real HTTP layer - the exact regression test Zainab's review asked
for.

**Honest, disclosed consequence of this fix**: a REAL MFA-required sign-in cannot be completed
through the live `/api/v1/auth/{providerId}/authenticate` + `/api/v1/auth/mfa/{challengeId}/verify`
endpoints today, because no real out-of-band transport (SMS/TOTP/push) exists anywhere in this
codebase to deliver the code to the actual subject. This is a disclosed limitation of this
foundation story, not a hidden gap and not a regression from the vulnerable behavior it
replaces - a real transport adapter is future, pluggable work behind the same
`IMfaChallengeProvider` interface, the same "hook exists, real transport is a future adapter"
shape BIO-RPT-001's own channel adapters demonstrate for delivery in general. The AC05 demo
endpoint remains fully exercisable precisely because it is one, single, server-side call chain
that never hands the code to an external party.

## AC04: every branch is audited, exactly once

`AuthenticationService`'s private `AuditAsync` helper is the ONLY call site that writes an
`AuthAuditEvent`, following the write-then-existence-recheck discipline `BIO-IDN-006`'s own
`IRbacAuditSink` already established (never fire-and-forget; on a write exception, check
whether it landed anyway before raising `AuditUnavailableException`). Every terminal branch of
`AuthenticateAsync`/`CompleteMfaAsync` calls it exactly once:
`Every_login_attempt_writes_exactly_one_audit_event_whether_it_succeeds_or_fails`. Account
linking is audited only on first creation, not on every subsequent login for the same
(tenant, provider, subject) -
`First_successful_login_creates_exactly_one_account_link_and_a_second_login_reuses_it`.

**Account linking's own disclosed scope boundary**: `AccountLink.PersonId` is either an explicit
`link_person_id` the caller supplies, or a freshly generated synthetic reference - never
resolved against BIO-IDN-001's own person registry, because this project cannot reference that
feature project any more than it can reference the host. Binding a link's `PersonId` to a real,
existing BIOLOG person record is each consuming surface's own future integration step - the
same honestly-disclosed-rather-than-silently-assumed shape `BIO-RPT-001` already used for "no
domain event publisher exists yet."

## AC05: what "live" means here, and its limits

`web/features/integrations/live.ts` (built by BIO-UX-005) already targets
`/api/v1/integrations/primary-action` - that module's own doc comment says so explicitly: "the
real backend endpoint this domain's story wires up." This story implements exactly that one
path for real, in `BioInt001Module`, **without changing any file under
`web/features/integrations/**` or `web/app/{administration/integrations,settings/authentication}/**`**
(confirmed by the structural harness's own `git diff` check).

`POST /api/v1/integrations/primary-action` runs the REAL pipeline end to end, not a hardcoded
response: it auto-seeds one demo Password provider config (idempotently - a second call reuses
the same seeded provider rather than registering a duplicate, per
`AC05_primary_action_is_idempotent_about_the_demo_provider_it_seeds`) with an MFA-required
policy and a claim-mapping rule, authenticates a demo credential through the same
`AuthenticationService` any real caller uses, completes the MFA challenge the same way a real
client would, and reports the mapped roles and audit count.

**Limit, stated plainly**: both `/administration/integrations` and `/settings/authentication`
are placeholder pages today (BIO-UX-002/003 - shell/placeholder-only, per
`web/features/integrations/ports.ts`'s own doc comment: "no Integrations-specific method exists
yet"). `IntegrationsPort` is the shared, unextended `ModulePort` shape (just `primaryAction`),
so there is no dedicated form for selecting a provider, entering credentials or configuring
claim mappings on either page yet - that is each page's own future UI work, not something this
backend foundation story redesigns. Real tenant resolution still requires a resolved
`Host`-header tenant like every other tenant-plane endpoint in this API - a browser session
without one sees the same fail-closed `tenant_context_missing` any other LIVE module shows
today.

## ADR-0006 scope discipline: this is the closing story, not a mass retrofit

ADR-0006 Decision 5 names `BIO-INT-001` as the story that "ends the asserted-actor
accommodation entirely," but its own text is explicit that upgrading each existing story's
actor resolution from the asserted `X-Actor-Id` header to a real authenticated principal is **"a
named follow-up on each story, not silently left as permanent accommodation coverage"** - the
same shape ADR-0002 Decision 6 already used for `BIO-TEN-002`'s own accommodation-ending
obligation. This story therefore builds the real provider abstraction, claim mapping, MFA hook
and audit trail (AC01-04), and issues a real session for any NEW sign-in through its own
endpoints (AC05) - it does not retrofit `BIO-TEN-001`/`BIO-TEN-002` or
`BIO-IDN-001` through `BIO-IDN-006`'s own already-accepted, already-merged endpoints (still
reading `X-Actor-Id` via their own per-project `ActorResolver` copies), and it does not retire
`BIO-IDN-006`'s interim `ActorProvenanceGate`. Retrofitting six already-accepted stories' own
endpoints across five different feature projects is exactly the kind of cross-story write-scope
expansion `governance/feature-path-ownership.json` exists to prevent, and the ADR itself
frames it as each of those stories' own separate follow-up work, not a single big-bang
migration performed by the closing story. This scoping call is flagged to god for visibility,
not treated as unilaterally settled.

## Local development / verification

```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj
dotnet test tests/features/bio-int-001/Biolog.Features.Authentication.Tests.csproj
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj --filter AuthenticationEndpointsTests
bash tests/features/bio-int-001/BIO-INT-001.test.sh
```

## Scope boundaries

- No changes to `web/features/integrations/**` or
  `web/app/{administration/integrations,settings/authentication}/**` (BIO-UX-005's own owned
  files) - see "AC05" above.
- No changes to any other story's owned backend files - in particular, no changes to
  `BIO-TEN-001`/`BIO-TEN-002`/`BIO-IDN-001` through `BIO-IDN-006`'s own feature projects.
- Retiring `BIO-IDN-006`'s interim `ActorProvenanceGate` and upgrading `ActorResolver`-based
  endpoints across the codebase to a real authenticated principal - each named as "a named
  follow-up on each story" by ADR-0006 Decision 5 itself, not performed here.
- Delivering a real out-of-band MFA code (SMS/TOTP/push) - the hook exists
  (`IMfaChallengeProvider`); a real transport adapter is future, pluggable work, the same shape
  BIO-RPT-001's own channel adapters demonstrate for delivery in general.
- Resolving `AccountLink.PersonId` against BIO-IDN-001's real person registry - each consuming
  surface's own future integration step, honestly disclosed above rather than assumed away.
