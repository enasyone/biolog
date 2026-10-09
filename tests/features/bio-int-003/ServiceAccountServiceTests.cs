using Biolog.Features.ApiSecurity;
using Xunit;

namespace Biolog.Features.ApiSecurity.Tests;

/// <summary>Grants every permission by default; add a key to <see cref="DeniedPermissionKeys"/>
/// to simulate an actor who lacks it (BIO-INT-003-ZB-02 regression tests).</summary>
internal sealed class FakePermissionGateway : IPermissionGateway
{
    public HashSet<string> DeniedPermissionKeys { get; } = [];

    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(!DeniedPermissionKeys.Contains(permissionKey));
}

/// <summary>Grants nothing by default (fail-closed, matching BIO-IDN-006's own
/// <c>RolePermissionGrant</c> default) - explicitly <see cref="Grant"/>/<see cref="Revoke"/> a
/// (tenant, role, permission) triple to simulate a live tenant-scoped RBAC change
/// (BIO-INT-003-ZB-03 round-3 regression tests). No <c>SuperAdmin</c> short-circuit (removed in
/// the real <c>RolePermissionGatewayAdapter</c> too, BIO-INT-003-ZB-07 round 4 - see
/// <see cref="KnownRoles"/>'s own remarks for why).</summary>
internal sealed class FakeRolePermissionGateway : IRolePermissionGateway
{
    private readonly HashSet<(string TenantId, string Role, string PermissionKey)> _grants = [];

    public void Grant(string tenantId, string role, string permissionKey) => _grants.Add((tenantId, role, permissionKey));
    public void Revoke(string tenantId, string role, string permissionKey) => _grants.Remove((tenantId, role, permissionKey));

    public Task<bool> RoleHasPermissionAsync(string tenantId, string role, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_grants.Contains((tenantId, role, permissionKey)));
}

/// <summary>Always fails the write, matching BIO-INT-001's own
/// <c>AuditUnavailableException</c>-recheck test pattern (BIO-INT-003-ZB-04 regression tests).</summary>
internal sealed class ThrowingApiAuditSink : IApiAuditSink
{
    public Task WriteAsync(ApiAuditEvent auditEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("simulated audit sink failure");
    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken) => Task.FromResult(false);
    public Task<IReadOnlyList<ApiAuditEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ApiAuditEvent>>([]);
}

public sealed class ServiceAccountServiceTests
{
    private const string TestPepper = "test-secret-hash-pepper-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TestTokenSecret = "test-access-token-secret-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly AuditActor TestActor = new(ActorType.User, "per_test_actor");

    private static ServiceAccountService Build(
        int rateLimitPerMinute = 60, IServiceAccountRepository? repository = null, IApiAuditSink? auditSink = null,
        IPermissionGateway? permissionGateway = null, IRolePermissionGateway? rolePermissionGateway = null) => new(
        repository ?? new InMemoryServiceAccountRepository(),
        new HmacApiSecretHasher(TestPepper),
        new HmacApiAccessTokenCodec(TestTokenSecret),
        new InMemoryApiRateLimiter(limitPerWindow: rateLimitPerMinute, windowSeconds: 60),
        auditSink ?? new InMemoryApiAuditSink(),
        permissionGateway ?? new FakePermissionGateway(),
        rolePermissionGateway ?? new FakeRolePermissionGateway());

    [Fact]
    public async Task Creating_an_account_rejects_a_scope_outside_the_known_catalogue()
    {
        var service = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "NotARealScope" }, CancellationToken.None));
    }

    [Fact]
    public async Task A_created_account_is_only_visible_to_its_own_tenant()
    {
        // BIO-INT-003-AC01
        var service = Build();
        await service.CreateAsync("ten_a", TestActor, "Integrator A", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        await service.CreateAsync("ten_b", TestActor, "Integrator B", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        var tenantAAccounts = await service.ListAsync("ten_a", TestActor, CancellationToken.None);
        var tenantBAccounts = await service.ListAsync("ten_b", TestActor, CancellationToken.None);

        Assert.Single(tenantAAccounts);
        Assert.Equal("Integrator A", tenantAAccounts[0].DisplayName);
        Assert.Single(tenantBAccounts);
        Assert.Equal("Integrator B", tenantBAccounts[0].DisplayName);
    }

    [Fact]
    public async Task The_plaintext_secret_is_never_stored_only_its_hash()
    {
        // BIO-INT-003-AC04
        var service = Build();
        var (account, plaintextSecret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        Assert.NotEqual(System.Text.Encoding.UTF8.GetBytes(plaintextSecret), account.SecretHash);
    }

    [Fact]
    public async Task A_valid_client_credentials_grant_issues_a_token_scoped_to_the_account()
    {
        // BIO-INT-003-AC02
        var service = Build();
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly", "Gate" }, CancellationToken.None);

        var outcome = await service.IssueTokenAsync(account.Id, secret, requestedScopes: null, CancellationToken.None);

        Assert.Equal(TokenIssueOutcomeKind.Success, outcome.Kind);
        Assert.Equal(new HashSet<string> { "ReadOnly", "Gate" }, outcome.Token!.Scopes);
    }

    [Fact]
    public async Task A_grant_can_request_a_narrower_scope_subset_than_the_account_was_granted()
    {
        var service = Build();
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly", "Gate" }, CancellationToken.None);

        var outcome = await service.IssueTokenAsync(account.Id, secret, requestedScopes: new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        Assert.Equal(TokenIssueOutcomeKind.Success, outcome.Kind);
        Assert.Equal(new HashSet<string> { "ReadOnly" }, outcome.Token!.Scopes);
    }

    [Fact]
    public async Task A_grant_cannot_request_a_scope_the_account_was_never_granted()
    {
        var service = Build();
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        var outcome = await service.IssueTokenAsync(account.Id, secret, requestedScopes: new HashSet<string> { "Gate" }, CancellationToken.None);

        Assert.Equal(TokenIssueOutcomeKind.InvalidScope, outcome.Kind);
        Assert.Null(outcome.Token);
    }

    [Fact]
    public async Task An_unknown_client_id_is_rejected()
    {
        var service = Build();

        var outcome = await service.IssueTokenAsync("no-such-client", "any-secret", null, CancellationToken.None);

        Assert.Equal(TokenIssueOutcomeKind.InvalidClient, outcome.Kind);
    }

    [Fact]
    public async Task A_wrong_secret_is_rejected()
    {
        var service = Build();
        var (account, _) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        var outcome = await service.IssueTokenAsync(account.Id, "wrong-secret", null, CancellationToken.None);

        Assert.Equal(TokenIssueOutcomeKind.InvalidClient, outcome.Kind);
    }

    [Fact]
    public async Task A_revoked_account_cannot_obtain_a_new_token()
    {
        var service = Build();
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        await service.RevokeAsync("ten_a", TestActor, account.Id, account.Version, CancellationToken.None);

        var outcome = await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);

        Assert.Equal(TokenIssueOutcomeKind.AccountRevoked, outcome.Kind);
    }

    [Fact]
    public async Task Rotating_the_secret_invalidates_the_old_one_and_issues_a_new_one()
    {
        // BIO-INT-003-AC04
        var service = Build();
        var (account, oldSecret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        var rotated = await service.RotateSecretAsync("ten_a", TestActor, account.Id, account.Version, CancellationToken.None);
        Assert.NotNull(rotated);
        var (updatedAccount, newSecret) = rotated!.Value;

        var withOldSecret = await service.IssueTokenAsync(account.Id, oldSecret, null, CancellationToken.None);
        var withNewSecret = await service.IssueTokenAsync(account.Id, newSecret, null, CancellationToken.None);

        Assert.Equal(TokenIssueOutcomeKind.InvalidClient, withOldSecret.Kind);
        Assert.Equal(TokenIssueOutcomeKind.Success, withNewSecret.Kind);
        Assert.Equal(account.Version + 1, updatedAccount.Version);
    }

    [Fact]
    public async Task Rotating_with_a_stale_expected_version_throws_a_concurrency_conflict()
    {
        var service = Build();
        var (account, _) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        await Assert.ThrowsAsync<ServiceAccountConcurrencyConflictException>(() =>
            service.RotateSecretAsync("ten_a", TestActor, account.Id, account.Version + 1, CancellationToken.None));
    }

    // --- Security-review fix (Zainab, ZB-INT003-06, Medium - round 2): no false audit on 404/409 ---

    [Fact]
    public async Task Rotating_a_nonexistent_account_writes_no_audit_event_at_all()
    {
        var auditSink = new InMemoryApiAuditSink();
        var service = Build(auditSink: auditSink);

        var result = await service.RotateSecretAsync("ten_a", TestActor, "no-such-account", 1, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(await auditSink.ListForTenantAsync("ten_a", CancellationToken.None));
    }

    [Fact]
    public async Task Rotating_with_a_stale_version_writes_no_audit_event_before_throwing()
    {
        var auditSink = new InMemoryApiAuditSink();
        var service = Build(auditSink: auditSink);
        var (account, _) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        await Assert.ThrowsAsync<ServiceAccountConcurrencyConflictException>(() =>
            service.RotateSecretAsync("ten_a", TestActor, account.Id, account.Version + 1, CancellationToken.None));

        var events = await auditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.DoesNotContain(events, e => e.Action == ApiAuditAction.SecretRotated);
    }

    [Fact]
    public async Task Revoking_a_nonexistent_account_writes_no_audit_event_at_all()
    {
        var auditSink = new InMemoryApiAuditSink();
        var service = Build(auditSink: auditSink);

        var result = await service.RevokeAsync("ten_a", TestActor, "no-such-account", 1, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(await auditSink.ListForTenantAsync("ten_a", CancellationToken.None));
    }

    [Fact]
    public async Task Revoking_with_a_stale_version_writes_no_audit_event_before_throwing()
    {
        var auditSink = new InMemoryApiAuditSink();
        var service = Build(auditSink: auditSink);
        var (account, _) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        await Assert.ThrowsAsync<ServiceAccountConcurrencyConflictException>(() =>
            service.RevokeAsync("ten_a", TestActor, account.Id, account.Version + 1, CancellationToken.None));

        var events = await auditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.DoesNotContain(events, e => e.Action == ApiAuditAction.ServiceAccountRevoked);
    }

    [Fact]
    public async Task A_token_request_beyond_the_rate_limit_is_rejected_with_a_retry_after()
    {
        // BIO-INT-003-AC03
        var service = Build(rateLimitPerMinute: 2);
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);
        await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);
        var limited = await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);

        Assert.Equal(TokenIssueOutcomeKind.RateLimited, limited.Kind);
        Assert.True(limited.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task Every_token_issuance_attempt_writes_an_audit_event_for_the_account_tenant()
    {
        // BIO-INT-003-AC03
        var auditSink = new InMemoryApiAuditSink();
        var service = Build(auditSink: auditSink);

        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);
        await service.IssueTokenAsync(account.Id, "wrong-secret", null, CancellationToken.None);

        var events = await auditSink.ListForTenantAsync("ten_a", CancellationToken.None);

        Assert.Contains(events, e => e.Action == ApiAuditAction.ServiceAccountCreated);
        Assert.Contains(events, e => e.Action == ApiAuditAction.TokenIssued);
        Assert.Contains(events, e => e.Action == ApiAuditAction.TokenIssueFailed);
    }

    [Fact]
    public async Task A_valid_bearer_token_authorizes_a_protected_resource_call()
    {
        var service = Build();
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        var issued = await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);

        var authorization = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: null, CancellationToken.None);

        Assert.True(authorization.Allowed);
        Assert.Equal("ten_a", authorization.Token!.TenantId);
    }

    [Fact]
    public async Task A_missing_or_garbage_bearer_token_is_denied()
    {
        var service = Build();

        var missing = await service.AuthorizeAsync(null, "service-accounts:whoami", requiredPermission: null, CancellationToken.None);
        var garbage = await service.AuthorizeAsync("not-a-real-token", "service-accounts:whoami", requiredPermission: null, CancellationToken.None);

        Assert.False(missing.Allowed);
        Assert.Null(missing.Token);
        Assert.False(garbage.Allowed);
        Assert.Null(garbage.Token);
    }

    [Fact]
    public async Task A_protected_resource_call_beyond_the_rate_limit_is_denied_even_with_a_valid_token()
    {
        var service = Build(rateLimitPerMinute: 1);
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        var issued = await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);

        var first = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: null, CancellationToken.None);
        var second = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: null, CancellationToken.None);

        Assert.True(first.Allowed);
        Assert.False(second.Allowed);
        Assert.NotNull(second.Token);
        Assert.NotNull(second.RateLimit);
        Assert.Equal(AuthorizationDenialReason.RateLimited, second.DenialReason);
    }

    // --- Security-review fix (Zainab, ZB-INT003-03, High - round 2, reopened): live-state re-check ---

    [Fact]
    public async Task A_revoked_accounts_already_issued_token_is_rejected_immediately_not_only_at_expiry()
    {
        var service = Build();
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        var issued = await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);

        await service.RevokeAsync("ten_a", TestActor, account.Id, account.Version, CancellationToken.None);

        var authorization = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: null, CancellationToken.None);

        Assert.False(authorization.Allowed);
        Assert.Equal(AuthorizationDenialReason.AccountRevoked, authorization.DenialReason);
    }

    [Fact]
    public async Task A_token_whose_only_live_scope_has_no_grant_for_the_required_permission_is_denied()
    {
        var service = Build();
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "Gate" }, CancellationToken.None);
        var issued = await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);

        // Default FakeRolePermissionGateway grants nothing (fail-closed default, matching
        // BIO-IDN-006's own RolePermissionGrant: an absent grant means NOT granted) - "Gate" has
        // no grant for ServiceAccountService.WhoamiPermission here.
        var authorization = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: ServiceAccountService.WhoamiPermission, CancellationToken.None);

        Assert.False(authorization.Allowed);
        Assert.Equal(AuthorizationDenialReason.InsufficientScope, authorization.DenialReason);
    }

    [Fact]
    public async Task A_scope_removed_from_the_account_after_token_issuance_stops_being_honored_on_the_next_call()
    {
        // Simulates a live permission change (a scope withdrawn from the account after the token
        // was already issued) directly against the repository - this project has no public
        // endpoint to mutate an account's granted scopes after creation, so this proves the
        // ENFORCEMENT MECHANISM (live re-read, not trust-the-token) works independent of whether
        // any current endpoint can trigger it.
        var repository = new InMemoryServiceAccountRepository();
        var gateway = new FakeRolePermissionGateway();
        gateway.Grant("ten_a", "Gate", ServiceAccountService.WhoamiPermission); // only Gate is granted
        var service = Build(repository: repository, rolePermissionGateway: gateway);
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly", "Gate" }, CancellationToken.None);
        var issued = await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);
        Assert.Equal(new HashSet<string> { "ReadOnly", "Gate" }, issued.Token!.Scopes);

        var beforeRemoval = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: ServiceAccountService.WhoamiPermission, CancellationToken.None);
        Assert.True(beforeRemoval.Allowed);

        // Re-insert the same account id with "Gate" withdrawn - same secret hash, narrower scopes.
        await repository.CreateAsync("ten_a", account.Id, account.DisplayName, new HashSet<string> { "ReadOnly" }, account.SecretHash, CancellationToken.None);

        var afterRemoval = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: ServiceAccountService.WhoamiPermission, CancellationToken.None);

        Assert.False(afterRemoval.Allowed);
        Assert.Equal(AuthorizationDenialReason.InsufficientScope, afterRemoval.DenialReason);
        Assert.DoesNotContain("Gate", afterRemoval.Token!.Scopes);
        Assert.Contains("ReadOnly", afterRemoval.Token.Scopes);
    }

    // --- Security-review fix (Zainab, ZB-INT003-03, High - round 3): scope -> DOMAIN PERMISSION, not a label match ---

    [Fact]
    public async Task A_role_granted_the_required_permission_in_this_tenant_is_authorized()
    {
        var gateway = new FakeRolePermissionGateway();
        gateway.Grant("ten_a", "ReadOnly", ServiceAccountService.WhoamiPermission);
        var service = Build(rolePermissionGateway: gateway);
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        var issued = await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);

        var authorization = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: ServiceAccountService.WhoamiPermission, CancellationToken.None);

        Assert.True(authorization.Allowed);
    }

    [Fact]
    public async Task The_same_role_label_produces_a_different_result_when_the_tenant_grant_is_revoked()
    {
        // Proves this is a LIVE GRANT lookup, not a cached/static role->permission table: the
        // exact same role string, same tenant, same token - only the grant's existence changes.
        var gateway = new FakeRolePermissionGateway();
        gateway.Grant("ten_a", "ReadOnly", ServiceAccountService.WhoamiPermission);
        var service = Build(rolePermissionGateway: gateway);
        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        var issued = await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);

        var whileGranted = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: ServiceAccountService.WhoamiPermission, CancellationToken.None);
        gateway.Revoke("ten_a", "ReadOnly", ServiceAccountService.WhoamiPermission);
        var afterRevocation = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: ServiceAccountService.WhoamiPermission, CancellationToken.None);

        Assert.True(whileGranted.Allowed);
        Assert.False(afterRevocation.Allowed);
        Assert.Equal(AuthorizationDenialReason.InsufficientScope, afterRevocation.DenialReason);
    }

    [Fact]
    public async Task A_grant_in_one_tenant_does_not_satisfy_a_token_for_a_different_tenant()
    {
        var gateway = new FakeRolePermissionGateway();
        gateway.Grant("ten_a", "ReadOnly", ServiceAccountService.WhoamiPermission); // granted in A only
        var service = Build(rolePermissionGateway: gateway);
        var (accountA, secretA) = await service.CreateAsync("ten_a", TestActor, "Integrator A", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        var (accountB, secretB) = await service.CreateAsync("ten_b", TestActor, "Integrator B", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        var issuedA = await service.IssueTokenAsync(accountA.Id, secretA, null, CancellationToken.None);
        var issuedB = await service.IssueTokenAsync(accountB.Id, secretB, null, CancellationToken.None);

        var authorizationA = await service.AuthorizeAsync(issuedA.Token!.AccessToken, "service-accounts:whoami", requiredPermission: ServiceAccountService.WhoamiPermission, CancellationToken.None);
        var authorizationB = await service.AuthorizeAsync(issuedB.Token!.AccessToken, "service-accounts:whoami", requiredPermission: ServiceAccountService.WhoamiPermission, CancellationToken.None);

        Assert.True(authorizationA.Allowed);
        Assert.False(authorizationB.Allowed);
    }

    // --- Security-review fix (Zainab, ZB-INT003-07, High - round 4): no blanket admin scope ---

    [Fact]
    public async Task Creating_an_account_rejects_SuperAdmin_as_a_scope()
    {
        // Round 3 mirrored BIO-IDN-006's own "SuperAdmin never needs a grant" HUMAN-role
        // invariant into service-account tokens too - Zainab's round-4 review correctly found
        // that unsafe: an actor holding only the narrow ManagePermission could mint a service
        // account scoped SuperAdmin, auto-passing every current and future domain-permission
        // check (a genuine privilege-escalation primitive). SuperAdmin is now rejected at the
        // root, the same way any string outside the known catalogue already is - never issuable
        // as a service-account scope at all, regardless of who creates it or what they hold.
        var service = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "SuperAdmin" }, CancellationToken.None));
    }

    [Fact]
    public async Task A_service_account_manager_cannot_mint_a_token_for_a_permission_they_were_never_authorised_to_delegate()
    {
        // Proves the escalation path itself is closed, not just the symptom: an actor who can
        // create service accounts (holds ManagePermission) - and nothing else - cannot use that
        // authority to mint a credential that passes a permission check the gateway denies.
        var permissionGateway = new FakePermissionGateway(); // grants ManagePermission by default
        var rolePermissionGateway = new FakeRolePermissionGateway(); // no RolePermissionGrant rows at all
        var service = Build(permissionGateway: permissionGateway, rolePermissionGateway: rolePermissionGateway);

        var (account, secret) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);
        var issued = await service.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);

        var authorization = await service.AuthorizeAsync(issued.Token!.AccessToken, "service-accounts:whoami", requiredPermission: ServiceAccountService.WhoamiPermission, CancellationToken.None);

        Assert.False(authorization.Allowed);
        Assert.Equal(AuthorizationDenialReason.InsufficientScope, authorization.DenialReason);
    }

    // --- Security-review fix (Zainab, ZB-INT003-02, Critical): actor provenance + RBAC gate ---

    [Fact]
    public async Task An_actor_without_the_manage_permission_cannot_create_an_account()
    {
        var gateway = new FakePermissionGateway();
        gateway.DeniedPermissionKeys.Add(ServiceAccountService.ManagePermission);
        var service = Build(permissionGateway: gateway);

        await Assert.ThrowsAsync<PermissionRequiredException>(() =>
            service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None));
    }

    [Fact]
    public async Task An_actor_without_the_read_permission_cannot_list_accounts()
    {
        var gateway = new FakePermissionGateway();
        var service = Build(permissionGateway: gateway);
        await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        gateway.DeniedPermissionKeys.Add(ServiceAccountService.ReadPermission);

        await Assert.ThrowsAsync<PermissionRequiredException>(() =>
            service.ListAsync("ten_a", TestActor, CancellationToken.None));
    }

    [Fact]
    public async Task An_actor_without_the_manage_permission_cannot_rotate_or_revoke()
    {
        var gateway = new FakePermissionGateway();
        var service = Build(permissionGateway: gateway);
        var (account, _) = await service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        gateway.DeniedPermissionKeys.Add(ServiceAccountService.ManagePermission);

        await Assert.ThrowsAsync<PermissionRequiredException>(() =>
            service.RotateSecretAsync("ten_a", TestActor, account.Id, account.Version, CancellationToken.None));
        await Assert.ThrowsAsync<PermissionRequiredException>(() =>
            service.RevokeAsync("ten_a", TestActor, account.Id, account.Version, CancellationToken.None));
    }

    [Fact]
    public async Task A_denied_actor_never_reaches_the_repository_no_account_is_created()
    {
        // Proves the permission check runs BEFORE any mutation, not merely that it eventually throws.
        var gateway = new FakePermissionGateway();
        gateway.DeniedPermissionKeys.Add(ServiceAccountService.ManagePermission);
        var repository = new InMemoryServiceAccountRepository();
        var service = Build(repository: repository, permissionGateway: gateway);

        await Assert.ThrowsAsync<PermissionRequiredException>(() =>
            service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None));

        // A differently-permissioned service over the SAME repository sees nothing was created.
        var readable = Build(repository: repository);
        Assert.Empty(await readable.ListAsync("ten_a", TestActor, CancellationToken.None));
    }

    // --- Security-review fix (Zainab, ZB-INT003-04, High): audit-before-commit ---

    [Fact]
    public async Task A_failed_create_audit_leaves_no_account_behind()
    {
        var repository = new InMemoryServiceAccountRepository();
        var service = Build(repository: repository, auditSink: new ThrowingApiAuditSink());

        await Assert.ThrowsAsync<ApiAuditUnavailableException>(() =>
            service.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None));

        var readable = Build(repository: repository);
        Assert.Empty(await readable.ListAsync("ten_a", TestActor, CancellationToken.None));
    }

    [Fact]
    public async Task A_failed_rotation_audit_never_invalidates_the_old_secret()
    {
        // This is the exact lockout Zainab's review found: a post-mutation audit failure used to
        // leave the OLD secret invalidated with no new secret ever returned. Now the audit write
        // happens first, so a genuine audit failure means the rotation never touched the repository
        // at all - the original secret must still work.
        var repository = new InMemoryServiceAccountRepository();
        var workingAuditSink = new InMemoryApiAuditSink();
        var createService = Build(repository: repository, auditSink: workingAuditSink);
        var (account, originalSecret) = await createService.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        var failingRotateService = Build(repository: repository, auditSink: new ThrowingApiAuditSink());
        await Assert.ThrowsAsync<ApiAuditUnavailableException>(() =>
            failingRotateService.RotateSecretAsync("ten_a", TestActor, account.Id, account.Version, CancellationToken.None));

        var tokenService = Build(repository: repository, auditSink: workingAuditSink);
        var outcome = await tokenService.IssueTokenAsync(account.Id, originalSecret, null, CancellationToken.None);
        Assert.Equal(TokenIssueOutcomeKind.Success, outcome.Kind);
    }

    [Fact]
    public async Task A_failed_revoke_audit_leaves_the_account_active()
    {
        var repository = new InMemoryServiceAccountRepository();
        var workingAuditSink = new InMemoryApiAuditSink();
        var createService = Build(repository: repository, auditSink: workingAuditSink);
        var (account, secret) = await createService.CreateAsync("ten_a", TestActor, "Integrator", new HashSet<string> { "ReadOnly" }, CancellationToken.None);

        var failingRevokeService = Build(repository: repository, auditSink: new ThrowingApiAuditSink());
        await Assert.ThrowsAsync<ApiAuditUnavailableException>(() =>
            failingRevokeService.RevokeAsync("ten_a", TestActor, account.Id, account.Version, CancellationToken.None));

        var tokenService = Build(repository: repository, auditSink: workingAuditSink);
        var outcome = await tokenService.IssueTokenAsync(account.Id, secret, null, CancellationToken.None);
        Assert.Equal(TokenIssueOutcomeKind.Success, outcome.Kind);
    }
}
