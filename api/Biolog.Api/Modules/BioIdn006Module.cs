using Biolog.Api.Tenancy;
using Biolog.Features.Rbac;
using Biolog.Features.Rbac.Api;
using Biolog.Features.Rbac.Audit;
using Biolog.Features.Rbac.Domain;
using Biolog.Features.Rbac.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// RBAC role catalogue and maker-checker hooks (BIO-IDN-006), registered the same way every
/// sibling identity-wave module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start (never inside <c>api/features/bio-idn-006/**</c>
/// first), so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter)
/// - ships in-story per ADR-0002 Decision 6's per-channel accommodation-ending rule.
///
/// <b>Applying BIO-IDN-002-QA-01 from the start.</b> <see cref="PersonExistenceGatewayAdapter"/>
/// wraps BIO-IDN-001's own already-registered <c>PersonService.GetAsync</c> directly - the same
/// tenant-scoping-for-free reasoning every prior gateway's header documents.
///
/// <b>This story's own scope boundary.</b> This module does NOT wire real permission
/// enforcement into any OTHER story's endpoints (BIO-IDN-004's own <c>PermissionResolver</c>
/// remains an asserted-only header pending BIO-INT-001's real authentication) - see
/// docs/features/bio-idn-006/README.md "Deliberately out of scope" for why that integration is
/// a disclosed future step, not something reopening an already-accepted sibling story would buy
/// this story right now.
///
/// Depends on <see cref="TenantModule"/> (for <see cref="TenantContextMiddleware"/> to have
/// already run) and <see cref="BioIdn001Module"/> (a genuine runtime dependency via the
/// existence gateway adapter). "Dependencies: BIO-TEN-001" in the story's own metadata is
/// build-wave/conceptual sequencing, not a runtime service dependency - this module never calls
/// into BIO-TEN-001's own <c>TenantService</c> (mirrors the established "Dependencies in story
/// metadata is sequencing, not coupling" precedent this epic has used since BIO-IDN-002/003).
///
/// <b>BIO-IDN-006-QA-08 (Zainab's security review, Critical; ADR-0006 Decision 3).</b>
/// <see cref="IActorProvenanceStore"/> is loaded from <c>IConfiguration</c> here, the same
/// out-of-band provisioning path <c>TenantCredentialProvisioning</c> already established for the
/// structurally identical BIO-CRD-001-QA-01 finding - see <see cref="ActorProvenanceGate"/>'s
/// own header for the full ADR-0006 rationale and disclosed residual scope.
/// </summary>
public sealed class BioIdn006Module : IBiologModule
{
    public string Name => "Rbac";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddSingleton<IPersonRoleAssignmentRepository, InMemoryPersonRoleAssignmentRepository>();
        services.AddSingleton<IRolePermissionGrantRepository, InMemoryRolePermissionGrantRepository>();
        services.AddSingleton<ISensitiveActionRuleRepository, InMemorySensitiveActionRuleRepository>();
        services.AddSingleton<IApprovalRequestRepository, InMemoryApprovalRequestRepository>();
        services.AddSingleton<IRbacAuditSink, InMemoryRbacAuditSink>();
        services.AddSingleton<RoleAssignmentLockRegistry>();
        services.AddSingleton<PermissionGrantLockRegistry>();
        services.AddSingleton<SensitiveActionRuleLockRegistry>();
        services.AddSingleton<ApprovalRequestLockRegistry>();
        services.AddScoped<IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<RoleAssignmentService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<MakerCheckerService>();
        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RoleAssignmentEndpoints.Map(endpoints);
        PermissionEndpoints.Map(endpoints);
        MakerCheckerEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PersonExistenceGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService) : IPersonExistenceGateway
    {
        public async Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
            await personService.GetAsync(personId, cancellationToken) is not null;
    }
}
