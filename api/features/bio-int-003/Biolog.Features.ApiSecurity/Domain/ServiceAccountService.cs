namespace Biolog.Features.ApiSecurity;

public enum TokenIssueOutcomeKind
{
    Success,
    InvalidClient,
    AccountRevoked,
    InvalidScope,
    RateLimited,
}

public sealed record TokenIssueOutcome
{
    public required TokenIssueOutcomeKind Kind { get; init; }
    public ApiAccessToken? Token { get; init; }
    public int RetryAfterSeconds { get; init; }

    public static TokenIssueOutcome Success(ApiAccessToken token) => new() { Kind = TokenIssueOutcomeKind.Success, Token = token };
    public static TokenIssueOutcome InvalidClient() => new() { Kind = TokenIssueOutcomeKind.InvalidClient };
    public static TokenIssueOutcome AccountRevoked() => new() { Kind = TokenIssueOutcomeKind.AccountRevoked };
    public static TokenIssueOutcome InvalidScope() => new() { Kind = TokenIssueOutcomeKind.InvalidScope };
    public static TokenIssueOutcome RateLimited(int retryAfterSeconds) => new() { Kind = TokenIssueOutcomeKind.RateLimited, RetryAfterSeconds = retryAfterSeconds };
}

/// <summary>
/// BIO-INT-003's own orchestration seam - every acceptance criterion (AC01 tenant scoping,
/// AC02 OAuth2 scopes, AC03 rate limits + audit, AC04 hashed/rotatable secrets) is exercised
/// through this one service, the same shape BIO-INT-001's <c>AuthenticationService</c> already
/// establishes for its own provider-dispatch/MFA/audit orchestration.
///
/// <b>Security-review fix (Zainab, ZB-INT003-02, Critical).</b> <see cref="CreateAsync"/>,
/// <see cref="ListAsync"/>, <see cref="RotateSecretAsync"/> and <see cref="RevokeAsync"/> now
/// all require a resolved, provenance-verified <see cref="AuditActor"/> and check it holds
/// <see cref="ManagePermission"/>/<see cref="ReadPermission"/> via <see cref="IPermissionGateway"/>
/// (wrapping BIO-IDN-006's live <c>PermissionService.ActorHasPermissionAsync</c>) before doing
/// anything else - closing the gap her review found (any caller reaching a tenant plane could
/// create/read/rotate/revoke ANY account, including a SuperAdmin-scoped one).
///
/// <b>Security-review fix (Zainab, ZB-INT003-04, High).</b> Create/rotate/revoke now write their
/// audit event BEFORE the repository mutation, not after - if the audit sink genuinely fails
/// (verified via <see cref="IApiAuditSink.ExistsAsync"/>, never assumed from a bare exception),
/// NOTHING was mutated: the old secret stays valid, no account was created, no account was
/// revoked. This is the opposite of the prior ordering, where a failed post-mutation audit write
/// could invalidate a secret the caller never received, or revoke an account whose revocation
/// was never recorded. The repository mutation that follows a successful audit write uses
/// <see cref="CancellationToken.None"/> - once the audit record exists, a caller's cancellation
/// must not interrupt the one write that record is now a correct account of.
///
/// <b>Security-review fix (Zainab, ZB-INT003-03, High - round 2, re-opened).</b>
/// <see cref="AuthorizeAsync"/> no longer trusts a token's embedded claims as permanently true -
/// it re-reads the account's CURRENT state from <see cref="IServiceAccountRepository"/> on every
/// call: a revoked (or deleted) account's tokens are rejected immediately, not merely at their
/// next refresh, and the token's effective scopes are recomputed as the intersection of what the
/// token presents and what the account is CURRENTLY granted - a scope removed from the account
/// after the token was issued stops being honored on the very next request, without needing the
/// token itself to expire or be reissued. A required-permission check demonstrates this against a
/// real protected resource (<c>/whoami</c>): a token missing it is rejected with
/// <see cref="AuthorizationDenialReason.InsufficientScope"/>, never silently accepted. This
/// closes the two concrete, exploitable symptoms her review named (a revoked account's token
/// still working; an empty/insufficient-scope token still accepted) - but round 3 found this was
/// necessary, not sufficient (see the ZB-INT003-03 round-3 remark below).
///
/// <b>Security-review fix (Zainab, ZB-INT003-03, High - round 3, "round-1 framing was wrong,
/// not just round-2's depth").</b> A role label is NOT itself a domain permission -
/// <see cref="AuthorizeAsync"/> previously checked a live scope LABEL against itself
/// (<c>effectiveScopes.Contains("ReadOnly")</c>), which proves the label survives
/// revocation/narrowing but never consults whether "ReadOnly" actually CARRIES the permission a
/// protected resource requires - BIO-IDN-006's own <c>RolePermissionGrant</c> is explicitly a
/// per-TENANT, independently-configurable fact, fail-closed by default. <paramref
/// name="requiredPermission"/> is now a namespaced domain permission key (<see
/// cref="WhoamiPermission"/>), resolved via <see cref="IRolePermissionGateway"/> against EVERY
/// one of the token's live effective scopes (role labels) - the same tenant a scope is checked
/// against is the token's own tenant, never ambient. A tenant can grant/deny this permission to
/// any role independently of what BIO-INT-003 itself hardcodes, exactly as the real RBAC model
/// requires.
///
/// <b>Security-review fix (Zainab, ZB-INT003-06, Medium - round 2).</b>
/// <see cref="RotateSecretAsync"/>/<see cref="RevokeAsync"/> now look up the account and check
/// its expected version BEFORE writing any audit event - a 404 (account not found) or 409
/// (stale version) is detected and returned/thrown with NO audit record at all, never a
/// "completed" event for a mutation that never happened. A narrow race remains between this
/// pre-check and the repository's own re-check under its internal lock (two truly concurrent
/// requests), the same "no shared transactional substrate across stores" residual BIO-INT-002's
/// own remediation history already disclosed and god accepted as proportionate - this fix closes
/// the common, exploitable case (a caller probing nonexistent ids or replaying a stale version),
/// not a theoretical simultaneous-request race.
/// </summary>
internal sealed class ServiceAccountService(
    IServiceAccountRepository repository,
    IApiSecretHasher secretHasher,
    IApiAccessTokenCodec tokenCodec,
    IApiRateLimiter rateLimiter,
    IApiAuditSink auditSink,
    IPermissionGateway permissionGateway,
    IRolePermissionGateway rolePermissionGateway)
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    public const string ManagePermission = "integrations.service_account_manage";
    public const string ReadPermission = "integrations.service_account_read";
    /// <summary>BIO-INT-003-ZB-03 (round 3): the domain permission <c>/whoami</c> requires -
    /// resolved against the token's live effective scopes via <see cref="IRolePermissionGateway"/>,
    /// never a bare role-label string match.</summary>
    public const string WhoamiPermission = "integrations.service_account_whoami";

    public async Task<(ServiceAccount Account, string PlaintextSecret)> CreateAsync(
        string tenantId, AuditActor actor, string displayName, IReadOnlySet<string> scopes, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, ManagePermission, cancellationToken);

        var invalidScope = scopes.FirstOrDefault(s => !KnownRoles.IsKnown(s));
        if (invalidScope is not null)
        {
            throw new ArgumentException($"'{invalidScope}' is not a known BIOLOG scope.", nameof(scopes));
        }

        var id = Guid.NewGuid().ToString();
        var plaintextSecret = ApiSecretGenerator.Generate();
        var secretHash = secretHasher.Hash(plaintextSecret);

        await AuditAsync(tenantId, ApiAuditAction.ServiceAccountCreated, id, actor.Id, detail: null, cancellationToken);

        var account = await repository.CreateAsync(tenantId, id, displayName, scopes, secretHash, CancellationToken.None);
        return (account, plaintextSecret);
    }

    public async Task<IReadOnlyList<ServiceAccount>> ListAsync(string tenantId, AuditActor actor, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, ReadPermission, cancellationToken);
        return await repository.ListAsync(tenantId, cancellationToken);
    }

    public async Task<(ServiceAccount Account, string PlaintextSecret)?> RotateSecretAsync(
        string tenantId, AuditActor actor, string id, int expectedVersion, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, ManagePermission, cancellationToken);

        // ZB-INT003-06: detect 404/409 BEFORE ever writing an audit event for them.
        var existing = await repository.GetAsync(tenantId, id, cancellationToken);
        if (existing is null)
        {
            return null;
        }
        if (existing.Version != expectedVersion)
        {
            throw new ServiceAccountConcurrencyConflictException(existing.Version);
        }

        var plaintextSecret = ApiSecretGenerator.Generate();
        var secretHash = secretHasher.Hash(plaintextSecret);

        await AuditAsync(tenantId, ApiAuditAction.SecretRotated, id, actor.Id, detail: null, cancellationToken);

        var updated = await repository.UpdateSecretHashAsync(tenantId, id, secretHash, expectedVersion, CancellationToken.None);
        return updated is null ? null : (updated, plaintextSecret);
    }

    public async Task<ServiceAccount?> RevokeAsync(string tenantId, AuditActor actor, string id, int expectedVersion, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, ManagePermission, cancellationToken);

        // ZB-INT003-06: detect 404/409 BEFORE ever writing an audit event for them.
        var existing = await repository.GetAsync(tenantId, id, cancellationToken);
        if (existing is null)
        {
            return null;
        }
        if (existing.Version != expectedVersion)
        {
            throw new ServiceAccountConcurrencyConflictException(existing.Version);
        }

        await AuditAsync(tenantId, ApiAuditAction.ServiceAccountRevoked, id, actor.Id, detail: null, cancellationToken);

        return await repository.RevokeAsync(tenantId, id, expectedVersion, CancellationToken.None);
    }

    /// <summary>OAuth2 client_credentials grant (BIO-INT-003-AC02). Rate-limited per
    /// (tenant, client, route) BEFORE the secret is even checked (AC03) - a credential-stuffing
    /// attempt against the token endpoint itself must not get unlimited guesses just because
    /// each guess is a different client_id. Unlike the management operations above, this grant
    /// has no human actor to resolve - the client_id/secret pair IS the credential, the same
    /// shape every OAuth2 client_credentials implementation uses.</summary>
    public async Task<TokenIssueOutcome> IssueTokenAsync(
        string clientId, string plaintextSecret, IReadOnlySet<string>? requestedScopes, CancellationToken cancellationToken)
    {
        const string route = "oauth:token";

        var account = await repository.GetByClientIdAsync(clientId, cancellationToken);
        if (account is null)
        {
            await AuditAsync("unknown", ApiAuditAction.TokenIssueFailed, clientId, actorId: null, "unknown_client", cancellationToken);
            return TokenIssueOutcome.InvalidClient();
        }

        var rateLimit = rateLimiter.TryAcquire(account.TenantId, clientId, route);
        if (!rateLimit.Allowed)
        {
            await AuditAsync(account.TenantId, ApiAuditAction.RateLimitExceeded, clientId, actorId: null, route, cancellationToken);
            return TokenIssueOutcome.RateLimited(rateLimit.RetryAfterSeconds);
        }

        if (account.Status == ServiceAccountStatus.Revoked)
        {
            await AuditAsync(account.TenantId, ApiAuditAction.TokenIssueFailed, clientId, actorId: null, "account_revoked", cancellationToken);
            return TokenIssueOutcome.AccountRevoked();
        }

        if (!secretHasher.Verify(plaintextSecret, account.SecretHash))
        {
            await AuditAsync(account.TenantId, ApiAuditAction.TokenIssueFailed, clientId, actorId: null, "invalid_secret", cancellationToken);
            return TokenIssueOutcome.InvalidClient();
        }

        var grantedScopes = requestedScopes is null or { Count: 0 }
            ? account.Scopes
            : requestedScopes;

        if (!grantedScopes.All(account.Scopes.Contains))
        {
            await AuditAsync(account.TenantId, ApiAuditAction.TokenIssueFailed, clientId, actorId: null, "invalid_scope", cancellationToken);
            return TokenIssueOutcome.InvalidScope();
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime);
        var accessToken = tokenCodec.Issue(account.TenantId, clientId, grantedScopes, expiresAt);
        await AuditAsync(account.TenantId, ApiAuditAction.TokenIssued, clientId, actorId: null, string.Join(',', grantedScopes.OrderBy(s => s, StringComparer.Ordinal)), cancellationToken);
        return TokenIssueOutcome.Success(new ApiAccessToken(account.TenantId, clientId, grantedScopes, accessToken, expiresAt));
    }

    /// <summary>Validates a bearer token, applies the rate limiter against the protected
    /// resource route it is being presented to (BIO-INT-003-AC03), and re-checks the token
    /// against LIVE account state (BIO-INT-003-ZB-03) - never trusts the token's own embedded
    /// claims as permanently true. <paramref name="requiredPermission"/>, when given, is a
    /// namespaced domain permission key (e.g. <see cref="WhoamiPermission"/>) - NOT a role label -
    /// resolved against the token's live effective scopes via <see cref="IRolePermissionGateway"/>
    /// (BIO-IDN-006's own tenant-scoped, independently-configurable <c>RolePermissionGrant</c>),
    /// never a bare string match against the scope itself. If no granted scope carries the
    /// permission, the call is denied with
    /// <see cref="AuthorizationDenialReason.InsufficientScope"/>.</summary>
    public async Task<RateLimitAuthorization> AuthorizeAsync(string? bearerToken, string route, string? requiredPermission, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(bearerToken) || !tokenCodec.TryValidate(bearerToken, out var token) || token is null)
        {
            return new RateLimitAuthorization(false, null, null, null);
        }

        var decision = rateLimiter.TryAcquire(token.TenantId, token.ClientId, route);
        if (!decision.Allowed)
        {
            await AuditAsync(token.TenantId, ApiAuditAction.RateLimitExceeded, token.ClientId, actorId: null, route, cancellationToken);
            return new RateLimitAuthorization(false, token, decision, AuthorizationDenialReason.RateLimited);
        }

        var account = await repository.GetByClientIdAsync(token.ClientId, cancellationToken);
        if (account is null || account.TenantId != token.TenantId)
        {
            await AuditAsync(token.TenantId, ApiAuditAction.AuthorizationDenied, token.ClientId, actorId: null, "account_not_found", cancellationToken);
            return new RateLimitAuthorization(false, null, decision, AuthorizationDenialReason.AccountNotFound);
        }

        if (account.Status == ServiceAccountStatus.Revoked)
        {
            await AuditAsync(token.TenantId, ApiAuditAction.AuthorizationDenied, token.ClientId, actorId: null, "account_revoked", cancellationToken);
            return new RateLimitAuthorization(false, null, decision, AuthorizationDenialReason.AccountRevoked);
        }

        var effectiveScopes = new HashSet<string>(token.Scopes.Where(account.Scopes.Contains), StringComparer.Ordinal);
        var effectiveToken = token with { Scopes = effectiveScopes };

        if (requiredPermission is not null)
        {
            var permitted = false;
            foreach (var role in effectiveScopes)
            {
                if (await rolePermissionGateway.RoleHasPermissionAsync(token.TenantId, role, requiredPermission, cancellationToken))
                {
                    permitted = true;
                    break;
                }
            }

            if (!permitted)
            {
                await AuditAsync(token.TenantId, ApiAuditAction.AuthorizationDenied, token.ClientId, actorId: null, $"insufficient_permission:{requiredPermission}", cancellationToken);
                return new RateLimitAuthorization(false, effectiveToken, decision, AuthorizationDenialReason.InsufficientScope);
            }
        }

        return new RateLimitAuthorization(true, effectiveToken, decision, null);
    }

    private async Task RequirePermissionAsync(string tenantId, AuditActor actor, string permissionKey, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, permissionKey, cancellationToken))
        {
            throw new PermissionRequiredException(permissionKey);
        }
    }

    /// <summary>Write-or-throw with an existence-recheck retry (BIO-INT-001's own
    /// <c>IAuthAuditSink</c> discipline): on a write exception, check whether the event was
    /// durably recorded anyway (via a non-cancellable recheck) before raising
    /// <see cref="ApiAuditUnavailableException"/> - a transient failure that actually landed
    /// must not be reported as lost, and a genuine failure must never be silently swallowed.</summary>
    private async Task AuditAsync(string tenantId, ApiAuditAction action, string clientId, string? actorId, string? detail, CancellationToken cancellationToken)
    {
        var auditEvent = new ApiAuditEvent(Guid.CreateVersion7().ToString(), SchemaVersion: 1, tenantId, action, clientId, actorId, detail, Guid.CreateVersion7().ToString(), DateTimeOffset.UtcNow);
        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw new ApiAuditUnavailableException();
            }
        }
    }
}

/// <summary>Why <see cref="ServiceAccountService.AuthorizeAsync"/> denied a call whose bearer
/// token was at least structurally valid - lets the endpoint pick the right HTTP status
/// (BIO-INT-003-ZB-03) instead of collapsing every denial into "rate limited."</summary>
public enum AuthorizationDenialReason
{
    RateLimited,
    AccountNotFound,
    AccountRevoked,
    InsufficientScope,
}

public sealed record RateLimitAuthorization(bool Allowed, ApiAccessToken? Token, RateLimitDecision? RateLimit, AuthorizationDenialReason? DenialReason);
