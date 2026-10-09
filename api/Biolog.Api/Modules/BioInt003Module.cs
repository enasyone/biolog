using Biolog.Api.Tenancy;
using Biolog.Features.ApiSecurity;
using Biolog.Features.ApiSecurity.Api;

namespace Biolog.Api.Modules;

/// <summary>
/// BIO-INT-003's API service-account, OAuth2 and rate-limit infrastructure, registered the
/// same thin-adapter way every sibling separate-project feature is (see
/// <c>Biolog.Features.ApiSecurity.csproj</c>'s own comment for why this project cannot
/// reference <see cref="ITenantContextAccessor"/> or BIO-IDN-006's role model directly).
///
/// Endpoint groups:
/// - <c>/api/v1/integrations/service-accounts</c> - tenant-scoped service-account CRUD (AC01),
///   secret rotation and revocation (AC04). Every route here requires a resolved, provenance-
///   verified actor (<see cref="ActorResolver"/>/<see cref="ActorProvenanceGate"/>) who holds
///   the relevant permission (<c>ServiceAccountService.ManagePermission</c>/
///   <c>ReadPermission</c>, checked via <see cref="Biolog.Features.Rbac.PermissionService"/>) -
///   security-review fix, Zainab, ZB-INT003-02, Critical.
/// - <c>/api/v1/oauth/token</c> - the OAuth2 client_credentials grant (AC02). Deliberately
///   OUTSIDE the tenant-context AND actor-provenance pipeline - a client presents
///   <c>client_id</c>/<c>client_secret</c> with no tenant header or human actor at all; the
///   account looked up by <c>client_id</c> is what reveals which tenant the caller belongs to,
///   the standard shape for a multi-tenant client-credentials grant (see
///   <c>IServiceAccountRepository.GetByClientIdAsync</c>'s own remarks).
/// - <c>/api/v1/integrations/service-accounts/whoami</c> - a minimal protected resource that
///   demonstrates bearer-token validation, per-route rate limiting (AC03), AND real scope-to-
///   domain-permission enforcement (AC02, security-review fix, Zainab, ZB-INT003-03, round 3)
///   end to end, without yet retrofitting any other story's existing endpoints to require a
///   bearer token (same "named follow-up, not a mass retrofit" scope boundary ADR-0006
///   Decision 5 already established for BIO-INT-001's session tokens).
///
/// <see cref="RolePermissionGatewayAdapter"/> wraps BIO-IDN-006's own
/// <c>IRolePermissionGrantRepository.GetAsync</c> DIRECTLY - not
/// <c>PermissionService.GetGrantAsync</c>, which resolves its tenant AMBIENTLY and so cannot
/// serve an OAuth2 bearer-token caller (no Host-channel tenant exists for that request at all).
/// See <c>IRolePermissionGateway</c>'s own remarks for the full rationale.
/// </summary>
public sealed class BioInt003Module : IBiologModule
{
    public string Name => "ApiSecurity";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IServiceAccountRepository, InMemoryServiceAccountRepository>();
        services.AddSingleton<IApiAuditSink, InMemoryApiAuditSink>();
        services.AddSingleton<IApiRateLimiter>(_ => new InMemoryApiRateLimiter(
            limitPerWindow: configuration.GetValue<int?>("ApiSecurity:RateLimitPerMinute") ?? 60,
            windowSeconds: 60));

        services.AddSingleton<IApiSecretHasher>(sp =>
            new HmacApiSecretHasher(configuration[HmacApiSecretHasher.ConfigurationKey]));
        services.AddSingleton<IApiAccessTokenCodec>(sp =>
            new HmacApiAccessTokenCodec(configuration[HmacApiAccessTokenCodec.ConfigurationKey]));

        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddSingleton<IRolePermissionGateway>(
            sp => new RolePermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.Domain.IRolePermissionGrantRepository>()));
        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddScoped<ServiceAccountService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapServiceAccountEndpoints(endpoints.MapGroup("/api/v1/integrations/service-accounts"));
        MapOAuthEndpoints(endpoints.MapGroup("/api/v1/oauth"));
    }

    private static void MapServiceAccountEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            HttpContext context, CreateServiceAccountRequest request, ServiceAccountService service,
            ITenantContextAccessor tenantAccessor, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            var scopes = new HashSet<string>(request.Scopes, StringComparer.Ordinal);
            var invalidScope = scopes.FirstOrDefault(s => !KnownRoles.IsKnown(s));
            if (invalidScope is not null)
            {
                return Results.Problem(title: "unknown_scope", detail: $"'{invalidScope}' is not a known BIOLOG scope.", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var (account, secret) = await service.CreateAsync(tenantId, actor!, request.DisplayName, scopes, cancellationToken);
                return Results.Created($"/api/v1/integrations/service-accounts/{account.Id}", ToWire(account, secret));
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
            catch (ApiAuditUnavailableException)
            {
                return AuditUnavailableProblem();
            }
        });

        group.MapGet("/", async (
            HttpContext context, ServiceAccountService service, ITenantContextAccessor tenantAccessor,
            ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            try
            {
                var accounts = await service.ListAsync(tenantId, actor!, cancellationToken);
                return Results.Ok(accounts.Select(a => ToWire(a, secret: null)));
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
        });

        group.MapPost("/{id}/rotate-secret", async (
            HttpContext context, string id, RotateSecretRequest request, ServiceAccountService service,
            ITenantContextAccessor tenantAccessor, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            try
            {
                var result = await service.RotateSecretAsync(tenantId, actor!, id, request.ExpectedVersion, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(ToWire(result.Value.Account, result.Value.PlaintextSecret));
            }
            catch (ServiceAccountConcurrencyConflictException ex)
            {
                return ConcurrencyProblem(ex);
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
            catch (ApiAuditUnavailableException)
            {
                return AuditUnavailableProblem();
            }
        });

        group.MapPost("/{id}/revoke", async (
            HttpContext context, string id, RevokeServiceAccountRequest request, ServiceAccountService service,
            ITenantContextAccessor tenantAccessor, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            try
            {
                var updated = await service.RevokeAsync(tenantId, actor!, id, request.ExpectedVersion, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(ToWire(updated, secret: null));
            }
            catch (ServiceAccountConcurrencyConflictException ex)
            {
                return ConcurrencyProblem(ex);
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
            catch (ApiAuditUnavailableException)
            {
                return AuditUnavailableProblem();
            }
        });

        group.MapGet("/whoami", async (HttpContext context, ServiceAccountService service, CancellationToken cancellationToken) =>
        {
            var bearerToken = ExtractBearerToken(context);
            // BIO-INT-003-ZB-03 (round 3): whoami requires the WhoamiPermission DOMAIN
            // PERMISSION, resolved against the token's live effective scopes via BIO-IDN-006's
            // real tenant-scoped RolePermissionGrant - never a bare role-label string match.
            var authorization = await service.AuthorizeAsync(bearerToken, "service-accounts:whoami", requiredPermission: ServiceAccountService.WhoamiPermission, cancellationToken);

            if (authorization.DenialReason is AuthorizationDenialReason.RateLimited)
            {
                context.Response.Headers.RetryAfter = authorization.RateLimit!.RetryAfterSeconds.ToString();
                return Results.Problem(title: "rate_limit_exceeded", statusCode: StatusCodes.Status429TooManyRequests);
            }

            if (authorization.DenialReason is AuthorizationDenialReason.InsufficientScope)
            {
                return Results.Problem(title: "insufficient_scope", statusCode: StatusCodes.Status403Forbidden);
            }

            if (authorization.Token is null)
            {
                return Results.Problem(title: "invalid_token", statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(new
            {
                tenantId = authorization.Token.TenantId,
                clientId = authorization.Token.ClientId,
                scopes = authorization.Token.Scopes,
            });
        });
    }

    private static void MapOAuthEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/token", async (OAuthTokenRequest request, ServiceAccountService service, CancellationToken cancellationToken) =>
        {
            if (request.GrantType != "client_credentials")
            {
                return Results.Problem(title: "unsupported_grant_type", statusCode: StatusCodes.Status400BadRequest);
            }

            var requestedScopes = string.IsNullOrWhiteSpace(request.Scope)
                ? null
                : new HashSet<string>(request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

            var outcome = await service.IssueTokenAsync(request.ClientId, request.ClientSecret, requestedScopes, cancellationToken);
            return outcome.Kind switch
            {
                TokenIssueOutcomeKind.Success => Results.Ok(new
                {
                    access_token = outcome.Token!.AccessToken,
                    token_type = "Bearer",
                    expires_in = (int)(outcome.Token.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds,
                    scope = string.Join(' ', outcome.Token.Scopes.OrderBy(s => s, StringComparer.Ordinal)),
                }),
                TokenIssueOutcomeKind.RateLimited => RateLimitedResult(outcome.RetryAfterSeconds),
                TokenIssueOutcomeKind.InvalidScope => Results.Problem(title: "invalid_scope", statusCode: StatusCodes.Status400BadRequest),
                TokenIssueOutcomeKind.AccountRevoked => Results.Problem(title: "invalid_client", detail: "service account is revoked", statusCode: StatusCodes.Status401Unauthorized),
                _ => Results.Problem(title: "invalid_client", statusCode: StatusCodes.Status401Unauthorized),
            };
        });

        static IResult RateLimitedResult(int retryAfterSeconds)
        {
            var problem = Results.Problem(title: "rate_limit_exceeded", statusCode: StatusCodes.Status429TooManyRequests);
            return new RetryAfterResult(problem, retryAfterSeconds);
        }
    }

    /// <summary>Resolves the calling actor and verifies their provenance proof
    /// (BIO-INT-003-ZB-02) before any management/read operation runs. Returns a non-null
    /// <see cref="IResult"/> (400/401) if resolution fails - the caller must check that first and
    /// return it directly; <paramref name="context"/>'s actor is otherwise guaranteed non-null.</summary>
    private static (AuditActor? Actor, IResult? Problem) ResolveActor(HttpContext context, ActorProvenanceGate provenanceGate)
    {
        try
        {
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);
            return (actor, null);
        }
        catch (ActorRequiredException ex)
        {
            return (null, Results.Problem(title: "actor_required", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }
        catch (InvalidActorTypeException ex)
        {
            return (null, Results.Problem(title: "invalid_actor_type", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }
        catch (ActorProvenanceRequiredException ex)
        {
            return (null, Results.Problem(title: "actor_provenance_required", detail: ex.Message, statusCode: StatusCodes.Status401Unauthorized));
        }
    }

    private static string? ExtractBearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..] : null;
    }

    private static object ToWire(ServiceAccount account, string? secret) => new
    {
        id = account.Id,
        clientId = account.Id,
        displayName = account.DisplayName,
        scopes = account.Scopes,
        status = account.Status.ToString(),
        version = account.Version,
        createdAt = account.CreatedAt,
        secretLastRotatedAt = account.SecretLastRotatedAt,
        secret,
    };

    private static string RequireTenant(ITenantContextAccessor tenantAccessor) =>
        tenantAccessor.Context?.TenantId ?? throw new TenantContextMissingException();

    private static IResult TenantContextProblem(TenantContextMissingException ex) => Results.Problem(
        title: "tenant_context_missing", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);

    private static IResult ConcurrencyProblem(ServiceAccountConcurrencyConflictException ex) => Results.Problem(
        title: "concurrency_conflict",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["current_version"] = ex.CurrentVersion });

    private static IResult PermissionProblem(PermissionRequiredException ex) => Results.Problem(
        title: "permission_required",
        detail: ex.Message,
        statusCode: StatusCodes.Status403Forbidden,
        extensions: new Dictionary<string, object?> { ["permission_key"] = ex.PermissionKey });

    private static IResult AuditUnavailableProblem() => Results.Problem(
        title: "audit_unavailable",
        detail: "the audit sink failed; the change was not committed without an audit record",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    /// <summary>Wraps BIO-IDN-006's own already-registered <c>PermissionService</c> - only this
    /// host project can see that internal type (<c>InternalsVisibleTo</c>), so this adapter is
    /// the one place <see cref="Biolog.Features.ApiSecurity"/>'s own <c>IPermissionGateway</c>
    /// seam is actually implemented (same pattern BIO-ACC-001's own <c>PermissionGatewayAdapter</c>
    /// already established).</summary>
    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    /// <summary>Wraps BIO-IDN-006's own already-registered <c>IRolePermissionGrantRepository</c>
    /// DIRECTLY (not <c>PermissionService.GetGrantAsync</c>, which resolves its tenant AMBIENTLY
    /// and so cannot serve an OAuth2 bearer-token caller - see <c>IRolePermissionGateway</c>'s own
    /// remarks). <see cref="Biolog.Features.Rbac.Domain.RoleType"/>'s enum MEMBER names are
    /// byte-for-byte identical to this project's own <c>KnownRoles</c> wire strings (both read
    /// "ReadOnly", "Gate", etc - only BIO-IDN-006's SEPARATE wire-string extension method uses
    /// SNAKE_CASE for its own HTTP surface), so a plain <see cref="Enum.TryParse{TEnum}"/> is
    /// correct here, never BIO-IDN-006's own <c>RoleTypeExtensions.TryParse</c>.
    ///
    /// <b>Security-review fix (Zainab, ZB-INT003-07, High - round 4).</b> This adapter no
    /// longer special-cases <c>RoleType.SuperAdmin</c> - round 3's short-circuit (mirroring
    /// BIO-IDN-006's own "SuperAdmin never needs a grant" HUMAN-role invariant) turned the
    /// narrow <see cref="ServiceAccountService.ManagePermission"/> into a privilege-escalation
    /// primitive for a non-human credential: any holder could mint a service account scoped
    /// <c>SuperAdmin</c> that would then auto-pass every current and future domain-permission
    /// check. <see cref="KnownRoles"/> now excludes <c>SuperAdmin</c> from the issuable
    /// service-account scope catalogue entirely (the real fix - reject it at the root, never
    /// try to constrain what a SuperAdmin-scoped token could do after the fact), which makes
    /// this method's old short-circuit branch unreachable in practice; it was removed rather
    /// than left in as dead code that would misleadingly imply this adapter still honors that
    /// case.</summary>
    private sealed class RolePermissionGatewayAdapter(Biolog.Features.Rbac.Domain.IRolePermissionGrantRepository grantRepository) : IRolePermissionGateway
    {
        public async Task<bool> RoleHasPermissionAsync(string tenantId, string role, string permissionKey, CancellationToken cancellationToken = default)
        {
            if (!Enum.TryParse<Biolog.Features.Rbac.Domain.RoleType>(role, out var roleType))
            {
                return false;
            }

            var grant = await grantRepository.GetAsync(tenantId, roleType, permissionKey, cancellationToken);
            return grant is { Granted: true };
        }
    }
}

public sealed record CreateServiceAccountRequest(string DisplayName, IReadOnlyList<string> Scopes);
public sealed record RotateSecretRequest(int ExpectedVersion);
public sealed record RevokeServiceAccountRequest(int ExpectedVersion);
/// <summary>RFC 6749 §4.4.2's own wire field names (snake_case) - a real OAuth2 client sends
/// exactly these, not this host's usual camelCase JSON convention.</summary>
public sealed record OAuthTokenRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("grant_type")] string GrantType,
    [property: System.Text.Json.Serialization.JsonPropertyName("client_id")] string ClientId,
    [property: System.Text.Json.Serialization.JsonPropertyName("client_secret")] string ClientSecret,
    [property: System.Text.Json.Serialization.JsonPropertyName("scope")] string? Scope);

/// <summary>Wraps an <see cref="IResult"/> to add the <c>Retry-After</c> header ADR-0004's
/// problem-response envelope expects on a 429 (BIO-INT-003-AC03).</summary>
internal sealed class RetryAfterResult(IResult inner, int retryAfterSeconds) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        await inner.ExecuteAsync(httpContext);
    }
}
