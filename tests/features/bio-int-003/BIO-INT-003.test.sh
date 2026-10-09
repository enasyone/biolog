#!/usr/bin/env bash
# Offline-safe structural checks for BIO-INT-003 (API service accounts, OAuth2 and rate limits).
# Behavioural assertions live in the executable xunit suites under tests/features/bio-int-003/
# and tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs (see
# docs/features/bio-int-003/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-int-003/Biolog.Features.ApiSecurity

# --- AC01: service accounts are tenant-scoped ---
check "AC01 feature project exists" test -f "$FEATURE.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE.csproj'"
check "AC01 feature project has no ProjectReference to Biolog.Api.csproj" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\\.Api\\.csproj' '$FEATURE.csproj'"
check "AC01 feature project has no ProjectReference to bio-idn-006 (RBAC role model is duplicated by value, not by reference)" \
  bash -c "! grep -q '<ProjectReference[^>]*bio-idn-006' '$FEATURE.csproj'"
check "AC01 feature project has no ProjectReference to bio-int-001 (role catalogue is its own third duplicate, not a shared reference)" \
  bash -c "! grep -q '<ProjectReference[^>]*bio-int-001' '$FEATURE.csproj'"
check "AC01 every service-account lookup/list/mutation is tenant-scoped by an explicit tenantId parameter" \
  bash -c "grep -q 'string tenantId' '$FEATURE/Domain/ServiceAccount.cs'"
check "AC01 cross-tenant isolation is proven, not just asserted" \
  bash -c "grep -q 'A_created_account_is_only_visible_to_its_own_tenant' tests/features/bio-int-003/ServiceAccountServiceTests.cs"

# --- AC02: OAuth2/token scopes map to permissions ---
check "AC02 a closed scope catalogue exists (this project's own duplicate of BIO-IDN-006's role model)" test -f "$FEATURE/Domain/KnownRoles.cs"
check "AC02 an OAuth2 client_credentials token endpoint exists" \
  bash -c "grep -q '\"/token\"' api/Biolog.Api/Modules/BioInt003Module.cs"
check "AC02 granted scopes are validated against the closed catalogue at creation time" \
  bash -c "grep -q 'KnownRoles.IsKnown' '$FEATURE/Domain/ServiceAccountService.cs'"
check "AC02 a token cannot be granted a scope the account itself was never granted (proven, not asserted)" \
  bash -c "grep -q 'A_grant_cannot_request_a_scope_the_account_was_never_granted' tests/features/bio-int-003/ServiceAccountServiceTests.cs"
check "AC02 a real client_credentials grant is proven at the real HTTP layer" \
  bash -c "grep -q 'AC02_a_valid_client_credentials_grant_issues_a_bearer_token' tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs"

# --- AC03: rate limits and API audit exist ---
check "AC03 a rate limiter exists" test -f "$FEATURE/Domain/RateLimiting.cs"
check "AC03 the rate limit bucket key is a value-equality type, not a joined string (BIO-INT-002's own collision lesson applied)" \
  bash -c "grep -q 'readonly record struct RateLimitKey' '$FEATURE/Domain/RateLimiting.cs'"
check "AC03 an API audit sink exists" test -f "$FEATURE/Domain/ApiAudit.cs"
check "AC03 rate-limit rejection is itself an audited action" \
  bash -c "grep -q 'RateLimitExceeded' '$FEATURE/Domain/ApiAudit.cs'"
check "AC03 exceeding the limit is proven to return 429 with Retry-After, not just asserted" \
  bash -c "grep -q 'AC03_exceeding_the_oauth_token_rate_limit_returns_429_with_a_retry_after_header' tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs"
check "AC03 a protected resource endpoint validates a bearer token end to end" \
  bash -c "grep -q 'AC03_a_well_formed_token_reaches_real_authorization_and_a_garbage_token_fails_signature_validation_first' tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs"

# --- AC04: API keys hashed/rotatable, never logged ---
check "AC04 a secret hasher exists" bash -c "grep -q 'interface IApiSecretHasher' '$FEATURE/Domain/ApiAccessTokenCodec.cs'"
check "AC04 the stored field is named SecretHash, never Secret/PlaintextSecret" \
  bash -c "grep -q 'byte\[\] SecretHash' '$FEATURE/Domain/ServiceAccount.cs' && ! grep -qE 'string .*PlaintextSecret|string .*Secret ' '$FEATURE/Domain/ServiceAccount.cs'"
check "AC04 ApiAuditEvent has no field capable of holding a raw secret or token (proven, not asserted)" \
  bash -c "grep -q 'No_audit_event_ever_carries_a_secret_or_token_shaped_field' tests/features/bio-int-003/ApiAuditTests.cs"
check "AC04 a rotate-secret endpoint exists" \
  bash -c "grep -q 'rotate-secret' api/Biolog.Api/Modules/BioInt003Module.cs"
check "AC04 rotation invalidates the old secret (proven, not asserted)" \
  bash -c "grep -q 'Rotating_the_secret_invalidates_the_old_one_and_issues_a_new_one' tests/features/bio-int-003/ServiceAccountServiceTests.cs"
check "AC04 a revoked account cannot obtain a new token (proven, not asserted)" \
  bash -c "grep -q 'A_revoked_account_cannot_obtain_a_new_token' tests/features/bio-int-003/ServiceAccountServiceTests.cs"
check "AC04 secrets are cryptographically random, never derived from a public value (BIO-INT-001-F01's own lesson applied)" \
  bash -c "grep -q 'RandomNumberGenerator' '$FEATURE/Domain/ApiAccessTokenCodec.cs'"

# --- Separate HMAC keys: a leaked key in one must not compromise the others ---
check "The access-token signing key and the secret-hash pepper use distinct configuration keys" \
  bash -c "grep -q 'ApiSecurity:AccessTokenSecret' '$FEATURE/Domain/ApiAccessTokenCodec.cs' && grep -q 'ApiSecurity:SecretHashPepper' '$FEATURE/Domain/ApiAccessTokenCodec.cs'"
check "No JWT library dependency was introduced (hand-rolled HMAC token, matching this codebase's own precedent)" \
  bash -c "! grep -iE '<PackageReference' '$FEATURE.csproj' | grep -qiE 'jwt|IdentityModel'"

# --- AC05: /administration/integrations connected via IntegrationService, additive to BIO-INT-002 ---
check "AC05 a new, additive API-credentials panel exists" test -f web/features/bio-int-003/ServiceAccountsPanel.tsx
check "AC05 the panel is mounted on /administration/integrations" \
  bash -c "grep -q 'ServiceAccountsPanel' web/app/administration/integrations/page.tsx"
check "AC05 BIO-INT-002's own ImportConsole is still mounted, not replaced" \
  bash -c "grep -q 'ImportConsole' web/app/administration/integrations/page.tsx"
check "AC05 four new, independently mode-switchable IntegrationService port keys are registered" \
  bash -c "grep -q 'listServiceAccounts.*createServiceAccount.*rotateServiceAccountSecret.*revokeServiceAccount' web/lib/feature-mode.ts"
check "AC05 the live adapter calls this story's own real backend path" \
  bash -c "grep -q '/api/v1/integrations/service-accounts' web/features/bio-int-003/live.ts"
check "AC05 the live-connection design is documented" bash -c "grep -qi 'AC05' docs/features/bio-int-003/README.md"
check "/settings/authentication is documented as deliberately untouched (not this story's domain)" \
  bash -c "grep -qi 'settings/authentication.* is deliberately left untouched\|deliberately left untouched' docs/features/bio-int-003/README.md"

# --- ADR-0006 scope discipline: named follow-up, not a mass retrofit ---
check "This story documents that real RBAC enforcement of token scopes is a named follow-up" \
  bash -c "grep -qi 'named follow-up' docs/features/bio-int-003/README.md"

# --- Security-review fixes (Zainab, round 1) ---
check "ZB01 the access-token codec has no dummy-key fallback - Issue()/TryValidate() fail closed when unconfigured" \
  bash -c "! grep -q 'DummyKey' '$FEATURE/Domain/ApiAccessTokenCodec.cs'"
check "ZB01 a minimum secret length is enforced for both the token codec and the secret hasher" \
  bash -c "grep -q 'MinimumSecretLength' '$FEATURE/Domain/ApiAccessTokenCodec.cs' && grep -q 'MinimumPepperLength' '$FEATURE/Domain/ApiAccessTokenCodec.cs'"
check "ZB01 two independently-unconfigured codecs can no longer forge/accept each other's tokens (proven, not asserted)" \
  bash -c "grep -q 'Two_independently_unconfigured_codecs_can_no_longer_forge_or_accept' tests/features/bio-int-003/ApiAccessTokenCodecTests.cs"
check "ZB02 an actor resolver and provenance gate exist (same seam BIO-ACC-001/BIO-IDN-006 already established)" \
  bash -c "test -f '$FEATURE/Api/ActorResolver.cs' && test -f '$FEATURE/Api/ActorProvenanceGate.cs'"
check "ZB02 a permission gateway seam exists and service management methods require it before anything else" \
  bash -c "test -f '$FEATURE/Domain/IPermissionGateway.cs' && grep -q 'RequirePermissionAsync' '$FEATURE/Domain/ServiceAccountService.cs'"
check "ZB02 BioInt003Module wires a real PermissionGatewayAdapter over BIO-IDN-006's own PermissionService" \
  bash -c "grep -q 'PermissionGatewayAdapter' api/Biolog.Api/Modules/BioInt003Module.cs && grep -q 'Biolog.Features.Rbac.PermissionService' api/Biolog.Api/Modules/BioInt003Module.cs"
check "ZB02 an unprivileged-but-provenance-verified actor is denied at the real HTTP layer (proven, not asserted)" \
  bash -c "grep -q 'ZB02_a_resolved_actor_with_valid_provenance_but_no_grant_cannot_create_a_service_account' tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs"
check "ZB02 a missing actor header and a forged provenance proof are both proven to fail at the real HTTP layer" \
  bash -c "grep -q 'ZB02_a_request_missing_the_actor_header_is_rejected_with_400' tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs && grep -q 'ZB02_a_request_with_a_forged_or_garbage_provenance_proof_is_rejected_with_401' tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs"
check "ZB04 audit now runs before the repository mutation on create/rotate/revoke (point-of-no-return discipline)" \
  bash -c "grep -q 'A_failed_rotation_audit_never_invalidates_the_old_secret' tests/features/bio-int-003/ServiceAccountServiceTests.cs"
check "ZB04 the repository mutation after a successful audit write is non-cancellable" \
  bash -c "grep -q 'CancellationToken.None' '$FEATURE/Domain/ServiceAccountService.cs'"

# --- Security-review fixes (Zainab, round 2) ---
check "ZB05 the provenance store has no dummy-key fallback - Verify() fails closed when unconfigured" \
  bash -c "! grep -q 'static readonly byte\[\]' '$FEATURE/Api/ActorProvenanceGate.cs'"
check "ZB05 the former public dummy-key proof is proven rejected against an unconfigured host (proven, not asserted)" \
  bash -c "grep -q 'ZB05_the_former_public_dummy_key_proof_no_longer_satisfies_an_unconfigured_provenance_gate' tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs"
check "ZB03 AuthorizeAsync re-reads live account state (revocation + current scopes), not just the token's embedded claims" \
  bash -c "grep -q 'GetByClientIdAsync' '$FEATURE/Domain/ServiceAccountService.cs' && grep -q 'AuthorizationDenialReason' '$FEATURE/Domain/ServiceAccountService.cs'"
check "ZB03 a revoked account's still-unexpired token is proven rejected at the real HTTP layer (proven, not asserted)" \
  bash -c "grep -q 'ZB03_a_revoked_accounts_token_is_rejected_at_whoami_even_though_it_has_not_expired' tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs"
check "ZB03 a scope withdrawn from the account after token issuance stops being honored (proven, not asserted)" \
  bash -c "grep -q 'A_scope_removed_from_the_account_after_token_issuance_stops_being_honored_on_the_next_call' tests/features/bio-int-003/ServiceAccountServiceTests.cs"
check "ZB06 rotate/revoke check existence/version BEFORE writing any audit event (no false completion events on 404/409)" \
  bash -c "grep -q 'Rotating_a_nonexistent_account_writes_no_audit_event_at_all' tests/features/bio-int-003/ServiceAccountServiceTests.cs && grep -q 'Revoking_with_a_stale_version_writes_no_audit_event_before_throwing' tests/features/bio-int-003/ServiceAccountServiceTests.cs"

# --- Security-review fix (Zainab, round 3): scope -> DOMAIN PERMISSION, not a role-label match ---
check "ZB03 (round 3) a role-permission gateway seam exists - resolves ROLES against BIO-IDN-006's real tenant-scoped grants" \
  bash -c "test -f '$FEATURE/Domain/IRolePermissionGateway.cs' && grep -q 'IRolePermissionGateway' '$FEATURE/Domain/ServiceAccountService.cs'"
check "ZB03 (round 3) whoami requires a namespaced domain permission key, never a bare role-label string" \
  bash -c "grep -q 'requiredPermission: ServiceAccountService.WhoamiPermission' api/Biolog.Api/Modules/BioInt003Module.cs"
check "ZB03 (round 3) BioInt003Module wires a real adapter over BIO-IDN-006's own IRolePermissionGrantRepository, bypassing PermissionService's ambient tenant resolution" \
  bash -c "grep -q 'RolePermissionGatewayAdapter' api/Biolog.Api/Modules/BioInt003Module.cs && grep -q 'IRolePermissionGrantRepository' api/Biolog.Api/Modules/BioInt003Module.cs"
check "ZB03 (round 3) the same role label produces a different result when the tenant grant is revoked (proven, not asserted)" \
  bash -c "grep -q 'The_same_role_label_produces_a_different_result_when_the_tenant_grant_is_revoked' tests/features/bio-int-003/ServiceAccountServiceTests.cs"
check "ZB03 (round 3) a grant in one tenant does not satisfy a token for a different tenant (proven, not asserted)" \
  bash -c "grep -q 'A_grant_in_one_tenant_does_not_satisfy_a_token_for_a_different_tenant' tests/features/bio-int-003/ServiceAccountServiceTests.cs"
check "ZB03 (round 3) a scope with no real tenant grant is denied at the real HTTP layer, not merely a fake (proven, not asserted)" \
  bash -c "grep -q 'ZB03_a_scope_with_no_real_tenant_grant_for_the_required_permission_is_rejected_with_403' tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs"

# --- Security-review fix (Zainab, round 4): SuperAdmin is not an issuable service-account scope ---
check "ZB07 KnownRoles excludes SuperAdmin from the issuable scope catalogue" \
  bash -c "! grep -q '\"SuperAdmin\"' '$FEATURE/Domain/KnownRoles.cs'"
check "ZB07 RolePermissionGatewayAdapter has no SuperAdmin implicit-grant short-circuit left in production code" \
  bash -c "! grep -q 'roleType == .*RoleType.SuperAdmin' api/Biolog.Api/Modules/BioInt003Module.cs"
check "ZB07 creating an account with scope SuperAdmin is rejected at the root (proven, not asserted)" \
  bash -c "grep -q 'Creating_an_account_rejects_SuperAdmin_as_a_scope' tests/features/bio-int-003/ServiceAccountServiceTests.cs"
check "ZB07 a service-account manager cannot mint a token for a permission they were never authorised to delegate (proven, not asserted)" \
  bash -c "grep -q 'A_service_account_manager_cannot_mint_a_token_for_a_permission_they_were_never_authorised_to_delegate' tests/features/bio-int-003/ServiceAccountServiceTests.cs"
check "ZB07 SuperAdmin is rejected as a service-account scope at the real HTTP layer (proven, not asserted)" \
  bash -c "grep -q 'ZB07_SuperAdmin_is_rejected_as_a_service_account_scope_at_the_real_http_layer' tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs"

# --- Traceability ---
check "builder evidence directory exists" test -d evidence/features/bio-int-003/builder
check "feature docs exist" test -f docs/features/bio-int-003/README.md

echo "----"
echo "PASS=$pass FAIL=$fail"
[ "$fail" -eq 0 ]
