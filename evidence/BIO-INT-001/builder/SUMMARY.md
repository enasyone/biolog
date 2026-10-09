# BIO-INT-001 — Builder evidence

**Story:** Implement authentication provider abstraction
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-INT-001`, branch `feature/BIO-INT-001` off `develop`
**Dependencies:** BIO-IDN-006 (RBAC role model, accepted)
**Commit:** `ff0aa17` (original builder), `03cba7b` (security remediation, see below)

## Remediation: BIO-INT-001-F01 (Zainab, High) — MFA challenge-code bypass, fixed

Zainab's security review FAILed commit `ff0aa17`: `InMemoryMfaChallengeProvider`'s public
`ExpectedCodeFor` derived the MFA code as a fixed SHA-256 prefix of the challenge id, and every
legitimate `mfa_required` HTTP response hands the caller that exact challenge id - so any
caller, not only the real subject, could compute the code and complete required MFA with no
second factor at all. Full finding: `evidence/BIO-INT-001/codex/SECURITY-REVIEW-ZAINAB.md`.

Fixed in commit `03cba7b`: the code is now generated with `RandomNumberGenerator`, independent
of the challenge id and every other externally-observable value, and is returned only to the
direct in-process caller of `IssueChallengeAsync` - never through any HTTP response.
`AuthenticationOutcome.DemoOnlyMfaCode` is `internal` (visible only to this assembly and
`Biolog.Api`), used solely by this story's own AC05 demo endpoint to complete a challenge it
just issued in the same request. **Disclosed consequence, not a regression**: a real
MFA-required sign-in cannot complete through the live `/api/v1/auth/...` endpoints until a real
out-of-band transport adapter is plugged in behind `IMfaChallengeProvider` - no such transport
exists anywhere in this codebase yet, and that was already true before this fix; the fix only
removes the false sense that MFA was actually enforceable via those endpoints today.

Regression tests added, both proving the exact fix Zainab's review asked for:
- `MfaChallengeProviderTests.An_attacker_who_knows_only_the_challenge_id_cannot_derive_the_code` -
  the old vulnerable formula no longer reconstructs the real code.
- `AuthenticationEndpointsTests.An_attacker_with_only_the_http_visible_challenge_id_cannot_verify_mfa` -
  the same proof at the real HTTP layer, the exact integration test her review requested.
- `AuthenticationServiceTests.DemoOnlyMfaCode_is_only_ever_populated_on_the_MfaRequired_outcome` -
  the demo-only code never rides along on Success or Failure outcomes.

Full regression after the fix: 70/70 feature unit tests (was 68), 175/175 integration tests
(was 174), 200/200 unit tests (unchanged), 35/35 structural checks (was 31, four new checks
added specifically for this fix) - all clean; module-reference/traceability/secret-scan/
vulnerability audits all still PASS.

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — provider configuration is tenant-scoped | `IAuthProviderConfigRepository`, every method takes an explicit `tenantId`; not-found-not-forbidden across tenants | `ProviderConfigRepositoryTests.cs` (6/6), `AuthenticationEndpointsTests.cs` |
| AC02 — no provider-specific claims leak into authorization without mapping | `ClaimMappingService.MapClaims` (exact-match only); role names validated against a closed known-role set at registration | `ClaimMappingTests.cs` (9/9), `AuthenticationServiceTests.cs`, `AuthenticationEndpointsTests.cs` |
| AC03 — MFA policy hooks exist | `IMfaChallengeProvider` + `AuthProviderConfig.Mfa`; no session issued before a required challenge is verified; challenge is single-use | `MfaChallengeProviderTests.cs` (5/5), `AuthenticationServiceTests.cs`, `AuthenticationEndpointsTests.cs` |
| AC04 — authentication failure and account-link events are audited | `IAuthAuditSink`; every terminal branch audited exactly once; account link audited once, reused silently thereafter | `AuthenticationServiceTests.cs`, `AuthenticationEndpointsTests.cs` |
| AC05 — `/administration/integrations`, `/settings/authentication` connected via `IntegrationService` | Real, live `POST /api/v1/integrations/primary-action` — the exact path `web/features/integrations/live.ts` already targets; zero changes to BIO-UX-005's owned frontend files | `AuthenticationEndpointsTests.cs`, `docs/features/bio-int-001/README.md`'s "AC05" section |

## Developer tests

- `tests/features/bio-int-001/ClaimMappingTests.cs` — 9 tests (AC02: exact-match mapping, unmapped claim contributes nothing, hostile privileged-looking claim never leaks a role, all 11 known roles recognized).
- `tests/features/bio-int-001/ProviderConfigRepositoryTests.cs` — 6 tests (AC01: cross-tenant isolation on read/list/status-change, concurrency conflict on stale version).
- `tests/features/bio-int-001/MfaChallengeProviderTests.cs` — 5 tests (AC03: correct code verifies, wrong code fails, single-use even after a wrong attempt, unknown challenge id fails).
- `tests/features/bio-int-001/SessionTokenTests.cs` — 5 tests (round-trip, wrong secret rejected, tampered payload rejected, expired token rejected, unconfigured-secret fail-closed behavior).
- `tests/features/bio-int-001/ProviderAdapterTests.cs` — 15 tests (the 5 federated adapters: correct provider type, missing subject rejected, default/invalid_assertion/provider_unavailable simulate outcomes, asserted claims passed through unchanged).
- `tests/features/bio-int-001/AuthenticationServiceTests.cs` — 11 tests (the orchestrator end to end: password login with/without MFA, wrong password, disabled vs. missing provider parity, MFA wrong-code and single-use-after-failure, account-link create-once/reuse, explicit `link_person_id`, federated dispatch, one audit event per attempt).
- `tests/dotnet/Biolog.IntegrationTests/AuthenticationEndpointsTests.cs` — 10 tests against the real composed host: AC05 primary-action end-to-end (including MFA) and its idempotent demo-provider seeding, tenant isolation on provider visibility, AC02 end-to-end role mapping, unknown-role-mapping rejection, AC03 MFA verify endpoint, AC04 closed-reason-code 401 body, concurrency conflict, disabled-provider authentication rejection, tenant-missing fail-closed.

Reproduce with:
```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj
dotnet test tests/features/bio-int-001/Biolog.Features.Authentication.Tests.csproj
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj
bash tests/features/bio-int-001/BIO-INT-001.test.sh
bash scripts/check-module-project-references.sh
python3 scripts/check-traceability.py --all
bash scripts/secret-scan.sh --all
bash scripts/check-dotnet-vulnerabilities.sh api/features/bio-int-001/Biolog.Features.Authentication.csproj tests/features/bio-int-001/Biolog.Features.Authentication.Tests.csproj
```
(exact commands in `metadata.json`'s `test_command`.)

## Results

- `dotnet build api/Biolog.Api/Biolog.Api.csproj`: PASS, 0 warnings, 0 errors — full host (all 45
  feature projects) builds clean with the new project wired in.
- `Biolog.Features.Authentication.Tests`: 68/68 PASS.
- `Biolog.IntegrationTests` (full suite, not just this story's new file): 174/174 PASS — 0
  pre-existing failures reintroduced.
- `Biolog.UnitTests` (full suite): 200/200 PASS.
- `BIO-INT-001.test.sh`: 31/31 PASS.
- `check-module-project-references.sh`: PASS.
- `check-traceability.py --all`: PASS (32 implemented stories) — no `tests/foundation/BIO-INT-001.test.sh`
  or `governance/stories/BIO-INT-001.json` needed, matching the established precedent for every
  other `api/features/`-convention story with no mobile component.
- `check-dotnet-vulnerabilities.sh` (both new projects): PASS, no vulnerable packages.
- `secret-scan.sh --all`: clean.

## Why a separate feature project, and why it cannot reference BIO-IDN-006 either

Mirrors `BIO-RPT-001`'s own reasoning for `Biolog.Api.csproj`: this project cannot reference the
host, so every tenant-scoped method takes `string tenantId` explicitly, resolved by
`BioInt001Module` from the real `ITenantContextAccessor`.

It additionally cannot reference `bio-idn-006`'s own feature project — confirmed by direct
inspection before designing: `PermissionService`/`RoleAssignmentService` are `internal` with
`InternalsVisibleTo` scoped to `Biolog.Api` and BIO-IDN-006's own tests only, and only
`Biolog.Api` may hold a `ProjectReference` to a feature project at all
(`scripts/check-module-project-references.sh`). `Domain/KnownRoles.cs` is this project's own
closed, documented duplicate of BIO-IDN-006's eleven `RoleType` wire strings by VALUE — the
same "duplicate the minimal shape" pattern `BIO-DEV-003`'s `DeviceDescriptor` and `BIO-RPT-001`'s
`DomainEventEnvelope` already established for this exact class of cross-project boundary.

## No JWT/session ADR or dependency existed anywhere in this codebase — confirmed by search

Searched `contracts/`, `docs/adr/` and every existing `.csproj` before designing this story: no
JWT/OIDC/SAML/session contract or NuGet dependency exists anywhere. `docs/features/bio-crd-007/README.md`
already states this explicitly for its own signed-package feature. This story's session token
(`Domain/SessionToken.cs`) is therefore a hand-rolled HMAC construction — the same shape
BIO-IDN-006's own `ActorProvenanceGate` already uses (config-only secret, fails closed to a
dummy key when unconfigured, constant-time comparison) — introducing no new library dependency
or vulnerability-scan surface for a foundation story.

## AC02 proven with a hostile input, not just the happy path

`ClaimMappingTests.A_hostile_claim_type_that_looks_like_a_privileged_group_never_leaks_a_role_without_a_rule`
injects claims shaped to look privileged (`("group", "super-admin")`, `("role", "SuperAdmin")`,
`("SuperAdmin", "true")`) against a rule set that maps none of them, and confirms zero roles
result — the adversarial case ADR-0003 Decision 3's own framing names ("what would a caller have
to control for this to be false?"), applied here to authorization rather than redaction.
`AuthenticationEndpointsTests.AC02_end_to_end_authentication_only_grants_explicitly_mapped_roles`
proves the same discipline through the real HTTP pipeline with a genuine successful login that
also carries one deliberately unmapped claim in the same request.

## AC03: a session is provably never issued before MFA completes

`Mfa_required_config_never_issues_a_session_before_the_challenge_is_verified` asserts
`outcome.Principal` is `null` on the `MfaRequired` branch, not merely that a challenge object
exists alongside a session. `A_challenge_is_single_use_even_when_the_first_attempt_was_wrong`
proves the one-time-use guarantee holds even on a failed first attempt, closing the obvious
gap a naive "consume on success only" implementation would leave open.

## AC04: exactly one audit event per outcome, and a proven idempotent account link

`Every_login_attempt_writes_exactly_one_audit_event_whether_it_succeeds_or_fails` and
`First_successful_login_creates_exactly_one_account_link_and_a_second_login_reuses_it` prove
both counting properties directly rather than asserting a single call succeeded. Account
linking's own scope boundary — `PersonId` is caller-supplied or synthetic, never resolved
against BIO-IDN-001's real person registry (this project cannot reference that feature project
either) — is stated plainly in `docs/features/bio-int-001/README.md`, the same
disclosed-not-assumed shape `BIO-RPT-001` used for "no domain event publisher exists yet."

## AC05: a real live connection, built for exactly this seam, with honest stated limits

`web/features/integrations/live.ts` (BIO-UX-005) already targets
`/api/v1/integrations/primary-action` — its own doc comment names it as "the real backend
endpoint this domain's story wires up." Built for real, backed by the actual
AC01-04 mechanism (provider config lookup, real dispatch, real claim mapping, a real MFA
challenge issued AND completed, real audit) — not a hardcoded response — with ZERO changes to
`web/features/integrations/**` or `web/app/{administration/integrations,settings/authentication}/**`,
confirmed by the structural harness's own `git diff` check. `AC05_primary_action_is_idempotent_about_the_demo_provider_it_seeds`
proves the endpoint does not register a duplicate demo provider on a second call. Honestly
documented limit: both pages remain placeholder-only (BIO-UX-002/003 shell scope) with no
dedicated provider-selection/credential-entry/claim-mapping form yet — `IntegrationsPort` is
still the unextended generic `ModulePort` shape — that UI is each page's own future work, not
redesigned here.

## ADR-0006 scope discipline, stated explicitly rather than silently narrowed

ADR-0006 Decision 5 names this story as the closing story for the asserted-actor accommodation,
but its own text frames upgrading each existing `X-Actor-Id`-based story
(`BIO-TEN-001`/`BIO-TEN-002`, `BIO-IDN-001` through `BIO-IDN-006`) to a real authenticated
principal, and retiring `BIO-IDN-006`'s interim `ActorProvenanceGate`, as "a named follow-up on
each story" — not a single retrofit this story performs. No other story's feature project was
touched (confirmed by the structural harness's own `git diff` check spanning all of
`api/features/`). This reading is flagged to god for visibility in the builder-complete report,
not treated as unilaterally decided.

## Scope boundaries, documented rather than silently assumed

1. **No changes to `web/features/integrations/**`/`web/app/{administration/integrations,settings/authentication}/**`**
   (BIO-UX-005's own owned files) — the live backend was built to the ALREADY-EXISTING contract
   those files define.
2. **No retrofit of `BIO-TEN-001`/`BIO-TEN-002`/`BIO-IDN-001` through `BIO-IDN-006`'s own
   endpoints**, and no retirement of `BIO-IDN-006`'s `ActorProvenanceGate` — each ADR-0006's own
   text names as that story's own future follow-up.
3. **No real out-of-band MFA transport (SMS/TOTP/push)** — the hook (`IMfaChallengeProvider`)
   exists and is proven end to end; a real transport adapter is future, pluggable work.
4. **No resolution of `AccountLink.PersonId` against BIO-IDN-001's real person registry** — each
   consuming surface's own future integration step.
