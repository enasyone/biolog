using Biolog.Api.Tenancy;
using Biolog.Features.Authentication;

namespace Biolog.Api.Modules;

/// <summary>
/// BIO-INT-001's authentication provider abstraction, registered the same thin-adapter way
/// every sibling separate-project feature is (see <c>Biolog.Features.Authentication.csproj</c>'s
/// own comment for why the project itself cannot reference <see cref="ITenantContextAccessor"/>
/// or BIO-IDN-006's role model directly).
///
/// Endpoint groups:
/// - <c>/api/v1/auth-providers</c> - tenant-scoped provider configuration CRUD (AC01).
/// - <c>/api/v1/auth-providers/{providerId}/password-credentials</c> - registers a demo/test
///   password credential row against a <see cref="AuthProviderType.Password"/> provider.
/// - <c>/api/v1/auth/{providerId}/authenticate</c> and <c>/api/v1/auth/mfa/{challengeId}/verify</c> -
///   the real sign-in flow (provider dispatch, AC02 claim mapping, AC03 MFA hook, AC04 audit).
/// - <c>/api/v1/integrations/primary-action</c> (AC05) - the EXACT path
///   <c>web/features/integrations/live.ts</c> (BIO-UX-005) already targets for its
///   <c>IntegrationService</c> module, backing both <c>/administration/integrations</c> and
///   <c>/settings/authentication</c> (both placeholder-only today, per that module's own doc
///   comment - "no Integrations-specific method exists yet"). No change to any file under
///   <c>web/features/integrations/**</c> or <c>web/app/{administration,settings}/**</c> was
///   needed or made - see docs/features/bio-int-001/README.md's "AC05" section.
///
/// Per ADR-0006 Decision 5: this module ends the asserted-actor accommodation for NEW sign-ins
/// this story itself issues a session for. It does not retrofit BIO-TEN-001/002 or
/// BIO-IDN-001 through BIO-IDN-006's own existing endpoints (still reading `X-Actor-Id`) - the
/// ADR itself names that as "a named follow-up on each story", not a mass retrofit performed by
/// the closing story. See docs/features/bio-int-001/README.md's own "Scope boundaries" section.
/// </summary>
public sealed class BioInt001Module : IBiologModule
{
    public string Name => "Authentication";
    public IReadOnlyCollection<string> DependsOn => ["Tenant"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAuthProviderConfigRepository, InMemoryAuthProviderConfigRepository>();
        services.AddSingleton<ICredentialStore, InMemoryCredentialStore>();
        services.AddSingleton<IMfaChallengeProvider, InMemoryMfaChallengeProvider>();
        services.AddSingleton<IAccountLinkRepository, InMemoryAccountLinkRepository>();
        services.AddSingleton<IAuthAuditSink, InMemoryAuthAuditSink>();

        services.AddSingleton<IAuthProviderAdapter, PasswordProviderAdapter>();
        services.AddSingleton<IAuthProviderAdapter, EntraIdProviderAdapter>();
        services.AddSingleton<IAuthProviderAdapter, Saml2ProviderAdapter>();
        services.AddSingleton<IAuthProviderAdapter, OidcProviderAdapter>();
        services.AddSingleton<IAuthProviderAdapter, GoogleProviderAdapter>();
        services.AddSingleton<IAuthProviderAdapter, LdapProviderAdapter>();

        services.AddSingleton<ISessionTokenCodec>(sp =>
            new HmacSessionTokenCodec(configuration[HmacSessionTokenCodec.ConfigurationKey]));

        services.AddSingleton<AuthenticationService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapProviderEndpoints(endpoints.MapGroup("/api/v1/auth-providers"));
        MapAuthenticationEndpoints(endpoints.MapGroup("/api/v1/auth"));
        MapIntegrationsEndpoints(endpoints.MapGroup("/api/v1/integrations"));
    }

    private static void MapProviderEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            AuthProviderRegistrationRequest request, IAuthProviderConfigRepository repo, ITenantContextAccessor tenantAccessor,
            CancellationToken cancellationToken) =>
        {
            var invalidRole = request.ClaimMappingRules.Select(r => r.MappedRole).FirstOrDefault(role => !KnownRoles.IsKnown(role));
            if (invalidRole is not null)
            {
                return Results.Problem(title: "unknown_role", detail: $"'{invalidRole}' is not a known BIOLOG role.", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var config = await repo.CreateAsync(RequireTenant(tenantAccessor), request, cancellationToken);
                return Results.Created($"/api/v1/auth-providers/{config.ProviderId}", config);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
        });

        group.MapGet("/", async (IAuthProviderConfigRepository repo, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await repo.ListAsync(RequireTenant(tenantAccessor), cancellationToken));
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
        });

        group.MapGet("/{providerId}", async (
            string providerId, IAuthProviderConfigRepository repo, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            try
            {
                var config = await repo.GetAsync(RequireTenant(tenantAccessor), providerId, cancellationToken);
                return config is null ? Results.NotFound() : Results.Ok(config);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
        });

        group.MapPatch("/{providerId}/status", async (
            string providerId, AuthProviderStatusChangeRequest request, IAuthProviderConfigRepository repo,
            ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await repo.UpdateStatusAsync(RequireTenant(tenantAccessor), providerId, request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (AuthProviderConcurrencyConflictException ex)
            {
                return Results.Problem(
                    title: "concurrency_conflict",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["current_version"] = ex.CurrentVersion });
            }
        });

        group.MapPost("/{providerId}/password-credentials", async (
            string providerId, PasswordCredentialRegistrationRequest request, IAuthProviderConfigRepository configRepo,
            ICredentialStore credentialStore, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            var config = await configRepo.GetAsync(tenantId, providerId, cancellationToken);
            if (config is null)
            {
                return Results.NotFound();
            }

            if (config.ProviderType != AuthProviderType.Password)
            {
                return Results.Problem(title: "not_a_password_provider", statusCode: StatusCodes.Status400BadRequest);
            }

            await credentialStore.RegisterAsync(tenantId, providerId, request.Username, request.Password, request.Claims, cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapAuthenticationEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/{providerId}/authenticate", async (
            string providerId, ProviderCredentialInput input, AuthenticationService authService,
            ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            var outcome = await authService.AuthenticateAsync(tenantId, providerId, input, cancellationToken);
            return OutcomeResult(outcome);
        });

        group.MapPost("/mfa/{challengeId}/verify", async (
            string challengeId, MfaVerificationRequest request, AuthenticationService authService,
            ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            var outcome = await authService.CompleteMfaAsync(tenantId, challengeId, request.Code, cancellationToken);
            return OutcomeResult(outcome);
        });
    }

    private static void MapIntegrationsEndpoints(RouteGroupBuilder group)
    {
        // BIO-INT-001-AC05: the exact path `integrationsLivePort` targets. Demonstrates the
        // real pipeline end to end - config lookup/auto-seed, password provider dispatch, AC02
        // claim mapping, AC03 MFA challenge issuance AND completion, AC04 audit - not a
        // hardcoded response, the same honesty precedent BIO-RPT-001's own primary-action
        // endpoint established.
        group.MapPost("/primary-action", async (
            IAuthProviderConfigRepository configRepo, ICredentialStore credentialStore, AuthenticationService authService,
            IAuthAuditSink auditSink, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            const string demoProviderName = "BIO-INT-001 demo password provider";
            const string demoUsername = "demo.integrations.user";
            const string demoPassword = "demo-integrations-password-do-not-reuse";

            var existing = (await configRepo.ListEnabledAsync(tenantId, cancellationToken))
                .FirstOrDefault(c => c.ProviderType == AuthProviderType.Password && c.DisplayName == demoProviderName);

            var config = existing ?? await configRepo.CreateAsync(
                tenantId,
                new AuthProviderRegistrationRequest(
                    AuthProviderType.Password,
                    demoProviderName,
                    MfaRequirement.Required,
                    [new ClaimMappingRule("group", "registrars", "RegistrarHr")]),
                cancellationToken);

            if (existing is null)
            {
                await credentialStore.RegisterAsync(
                    tenantId, config.ProviderId, demoUsername, demoPassword,
                    [new ProviderClaim("group", "registrars")], cancellationToken);
            }

            var before = (await auditSink.ListForTenantAsync(tenantId, cancellationToken)).Count;

            var input = new ProviderCredentialInput(
                new Dictionary<string, string> { ["username"] = demoUsername, ["password"] = demoPassword },
                []);
            var outcome = await authService.AuthenticateAsync(tenantId, config.ProviderId, input, cancellationToken);

            if (outcome.Kind != AuthenticationOutcomeKind.MfaRequired)
            {
                return Results.Ok(new { message = $"Demo authentication check did not reach the expected MFA step (outcome: {outcome.Kind})." });
            }

            // BIO-INT-001-F01 (Zainab, High) fix: the code is NEVER derived from the challenge
            // id or any other externally-observable value - it is only readable here because
            // this demo handler is the exact same in-process caller that just requested the
            // challenge (see AuthenticationOutcome.DemoOnlyMfaCode's own remarks). A real
            // caller of the live /authenticate + /mfa/{challengeId}/verify endpoints never
            // sees this value anywhere in either HTTP response.
            var challenge = outcome.Challenge!;
            var finalOutcome = await authService.CompleteMfaAsync(tenantId, challenge.ChallengeId, outcome.DemoOnlyMfaCode!, cancellationToken);

            var after = await auditSink.ListForTenantAsync(tenantId, cancellationToken);
            var newRecords = after.Count - before;

            var message = finalOutcome.Kind == AuthenticationOutcomeKind.Success
                ? $"Authentication check via '{demoProviderName}' succeeded (MFA verified). Mapped roles: [{string.Join(", ", finalOutcome.Principal!.Roles)}]. {newRecords} audit event(s) recorded."
                : $"Authentication check via '{demoProviderName}' did not complete (outcome: {finalOutcome.Kind}).";

            return Results.Ok(new { message });
        });
    }

    private static IResult OutcomeResult(AuthenticationOutcome outcome) => outcome.Kind switch
    {
        AuthenticationOutcomeKind.Success => Results.Ok(new
        {
            outcome = "success",
            sessionToken = outcome.Principal!.SessionToken,
            expiresAt = outcome.Principal.ExpiresAt,
            personId = outcome.Principal.PersonId,
            subjectId = outcome.Principal.SubjectId,
            roles = outcome.Principal.Roles,
        }),
        AuthenticationOutcomeKind.MfaRequired => Results.Ok(new
        {
            outcome = "mfa_required",
            challengeId = outcome.Challenge!.ChallengeId,
            expiresAt = outcome.Challenge.ExpiresAt,
        }),
        _ => Results.Problem(
            title: "authentication_failed",
            detail: outcome.FailureReason?.ToString(),
            statusCode: StatusCodes.Status401Unauthorized),
    };

    private static string RequireTenant(ITenantContextAccessor tenantAccessor) =>
        tenantAccessor.Context?.TenantId ?? throw new TenantContextMissingException();

    private static IResult TenantContextProblem(TenantContextMissingException ex) => Results.Problem(
        title: "tenant_context_missing", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
}
