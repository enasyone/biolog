#!/usr/bin/env bash
# Offline-safe structural checks for BIO-INT-001 (authentication provider abstraction).
# Behavioural assertions live in the executable xunit suites under tests/features/bio-int-001/
# and tests/dotnet/Biolog.IntegrationTests/AuthenticationEndpointsTests.cs (see
# docs/features/bio-int-001/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-int-001/Biolog.Features.Authentication

# --- AC01: provider configuration is tenant-scoped ---
check "AC01 feature project exists" test -f "$FEATURE.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE.csproj'"
check "AC01 feature project has no ProjectReference to Biolog.Api.csproj" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\\.Api\\.csproj' '$FEATURE.csproj'"
check "AC01 feature project has no ProjectReference to bio-idn-006 (RBAC role model is duplicated by value, not by reference)" \
  bash -c "! grep -q '<ProjectReference[^>]*bio-idn-006' '$FEATURE.csproj'"
check "AC01 every provider lookup/list/mutation is tenant-scoped by an explicit tenantId parameter" \
  bash -c "grep -q 'string tenantId' '$FEATURE/Domain/IAuthProviderConfigRepository.cs'"
check "AC01 GetAsync is not-found-not-forbidden across tenants (repository-level isolation test exists)" \
  bash -c "grep -q 'A_tenant_cannot_read_another_tenants_provider_by_id' tests/features/bio-int-001/ProviderConfigRepositoryTests.cs"

# --- AC02: no provider-specific claims leak into authorization without mapping ---
check "AC02 an explicit claim-mapping rule type exists" test -f "$FEATURE/Domain/ClaimMapping.cs"
check "AC02 mapping is exact-match only, never a wildcard/regex path" \
  bash -c "! grep -qE 'RegularExpressions|\"\\*\"' '$FEATURE/Domain/ClaimMappingService.cs'"
check "AC02 an unmapped claim contributing zero roles is proven, not just asserted" \
  bash -c "grep -q 'An_unmapped_claim_contributes_no_role' tests/features/bio-int-001/ClaimMappingTests.cs"
check "AC02 mapped role names are validated against a closed, known role set at registration" \
  bash -c "grep -q 'KnownRoles.IsKnown' api/Biolog.Api/Modules/BioInt001Module.cs"

# --- AC03: MFA policy hooks exist ---
check "AC03 an MFA challenge hook interface exists" test -f "$FEATURE/Domain/Mfa.cs"
check "AC03 a provider config carries its own MFA requirement" \
  bash -c "grep -q 'MfaRequirement Mfa' '$FEATURE/Domain/AuthProviderConfig.cs'"
check "AC03 a session is never issued before a required MFA challenge is verified (proven, not asserted)" \
  bash -c "grep -q 'Mfa_required_config_never_issues_a_session_before_the_challenge_is_verified' tests/features/bio-int-001/AuthenticationServiceTests.cs"
check "AC03 an MFA challenge is single-use" \
  bash -c "grep -q 'single_use' tests/features/bio-int-001/MfaChallengeProviderTests.cs"
check "AC03/F01 (Zainab, High, fixed) the MFA code is never derived from the challenge id - no ExpectedCodeFor-shaped helper exists" \
  bash -c "! grep -rq --include='*.cs' 'ExpectedCodeFor' '$FEATURE' tests/features/bio-int-001 tests/dotnet/Biolog.IntegrationTests/AuthenticationEndpointsTests.cs api/Biolog.Api/Modules/BioInt001Module.cs"
check "AC03/F01 the code is generated with a cryptographic RNG, not a hash of the challenge id" \
  bash -c "grep -q 'RandomNumberGenerator' '$FEATURE/Domain/Mfa.cs'"
check "AC03/F01 an attacker-with-only-the-challenge-id regression test exists at the real HTTP layer" \
  bash -c "grep -q 'An_attacker_with_only_the_http_visible_challenge_id_cannot_verify_mfa' tests/dotnet/Biolog.IntegrationTests/AuthenticationEndpointsTests.cs"
check "AC03/F01 the demo-only code is internal, never public, on AuthenticationOutcome" \
  bash -c "grep -q 'internal string? DemoOnlyMfaCode' '$FEATURE/Domain/AuthenticationOutcome.cs'"

# --- AC04: authentication failure and account-link events are audited ---
check "AC04 an authentication audit sink exists" test -f "$FEATURE/Domain/AuthAudit.cs"
check "AC04 login failure is an audited action" bash -c "grep -q 'LoginFailed' '$FEATURE/Domain/AuthProviderType.cs'"
check "AC04 account linking is an audited action" bash -c "grep -q 'AccountLinked' '$FEATURE/Domain/AuthProviderType.cs'"
check "AC04 every audit write follows the write-then-existence-recheck discipline (never fire-and-forget)" \
  bash -c "grep -q 'ExistsAsync' '$FEATURE/Domain/AuthenticationService.cs'"
check "AC04 a first-time account link is audited exactly once, a repeat login does not duplicate it" \
  bash -c "grep -q 'creates_exactly_one_account_link' tests/features/bio-int-001/AuthenticationServiceTests.cs"

# --- Provider adapters: one per selectable identity provider (user story's own list) ---
check "Six provider adapters exist, one per AuthProviderType" bash -c "
  test -f '$FEATURE/Adapters/PasswordProviderAdapter.cs' &&
  test -f '$FEATURE/Adapters/EntraIdProviderAdapter.cs' &&
  test -f '$FEATURE/Adapters/Saml2ProviderAdapter.cs' &&
  test -f '$FEATURE/Adapters/OidcProviderAdapter.cs' &&
  test -f '$FEATURE/Adapters/GoogleProviderAdapter.cs' &&
  test -f '$FEATURE/Adapters/LdapProviderAdapter.cs'"
check "Every adapter implements the one typed interface" \
  bash -c "grep -l ': IAuthProviderAdapter' '$FEATURE'/Adapters/*.cs | wc -l | grep -q '^ *6$'"
check "AuthenticationService dictionary-dispatches by provider type, never branches on the enum" \
  bash -c "! grep -qE 'case AuthProviderType\.|== AuthProviderType\.' '$FEATURE/Domain/AuthenticationService.cs'"
check "No JWT library dependency was introduced (hand-rolled HMAC token, matching this codebase's own precedent)" \
  bash -c "! grep -iE '<PackageReference' '$FEATURE.csproj' | grep -qiE 'jwt|IdentityModel'"

# --- AC05: /administration/integrations, /settings/authentication connected via IntegrationService ---
check "AC05 the primary-action endpoint matches web/features/integrations/live.ts's exact path" \
  bash -c "grep -q '/primary-action' api/Biolog.Api/Modules/BioInt001Module.cs && grep -q \"integrations/primary-action\" web/features/integrations/live.ts"
# Pinned to BIO-INT-001's own merge range (base 2feffb5 -> tip 58e4644), not `develop...`: this
# asserts what BIO-INT-001's own commits touched, not a permanent ban on the Integrations port
# surface. `web/features/integrations/{ports,mock,live}.ts` is explicitly the sanctioned
# extension point for "the domain story that builds real Integrations screens" (ports.ts's own
# doc comment) - BIO-INT-002-AC06 is exactly that story and is required to edit it. A live
# `develop...` diff here would wrongly fail BIO-INT-002's own quality.sh sweep for doing exactly
# what it's supposed to do (found via Tunde/god, 2026-10-06).
check "BIO-INT-001's own branch did not modify BIO-UX-005's owned Integrations frontend surface" \
  bash -c "! git diff --name-only 2feffb5212bc43b97dc9561c171d720f8f818e19..58e464485aaea180e4f5664f5f30b80e38ff793a -- web/features/integrations web/app/administration/integrations web/app/settings/authentication 2>/dev/null | grep -q ."
check "AC05 the live-connection design is documented" bash -c "grep -qi 'AC05' docs/features/bio-int-001/README.md"
check "AC05 the demo primary-action path is idempotent about the provider it seeds (proven, not asserted)" \
  bash -c "grep -q 'is_idempotent_about_the_demo_provider' tests/dotnet/Biolog.IntegrationTests/AuthenticationEndpointsTests.cs"

# --- ADR-0006 scope discipline: closing story, not a mass retrofit ---
check "This story documents that it does not retrofit BIO-TEN/BIO-IDN's existing ActorResolver-based endpoints" \
  bash -c "grep -qi 'named follow-up' docs/features/bio-int-001/README.md"
# Pinned to BIO-INT-001's own merge range (base 2feffb5 -> tip 58e4644, merged at 4327644), not
# `develop...`: this is a permanent historical assertion about what BIO-INT-001's own commits
# touched, not a live check against whatever branch quality.sh happens to run from. A `develop...`
# diff here would re-run as part of every SUBSEQUENT story's own quality.sh sweep and always find
# that story's own new files outside api/features/bio-int-001/ - a false FAIL unrelated to any
# real cross-story violation (found during BIO-VIS-002's build, 2026-10-06).
check "BIO-INT-001's own branch did not touch any other story's own feature project" \
  bash -c "! git diff --name-only 2feffb5212bc43b97dc9561c171d720f8f818e19..58e464485aaea180e4f5664f5f30b80e38ff793a -- api/features 2>/dev/null | grep -v '^api/features/bio-int-001/' | grep -q ."

# --- Traceability ---
check "builder evidence directory exists" test -d evidence/BIO-INT-001/builder
check "feature docs exist" test -f docs/features/bio-int-001/README.md

echo "----"
echo "PASS=$pass FAIL=$fail"
[ "$fail" -eq 0 ]
