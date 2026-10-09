using Biolog.Api.Tenancy;
using Biolog.Features.AccessPolicy;
using Biolog.Features.AccessPolicy.Api;
using Biolog.Features.AccessPolicy.Audit;
using Biolog.Features.AccessPolicy.Domain;
using Biolog.Features.AccessPolicy.Tenancy;
using Biolog.Features.PersonRegistry.Domain;

namespace Biolog.Api.Modules;

/// <summary>
/// Access policy model and assurance levels (BIO-ACC-001), registered the same way every sibling
/// identity-wave module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start (never inside <c>api/features/bio-acc-001/**</c>
/// first), so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <b>Applying BIO-IDN-002-QA-01 from the start.</b> <see cref="PersonExistenceGatewayAdapter"/>
/// and <see cref="IdentityStatusGatewayAdapter"/> both wrap BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> directly - the same tenant-scoping-for-free reasoning every prior
/// gateway's header documents. <see cref="PermissionGatewayAdapter"/> wraps BIO-IDN-006's own
/// already-registered <c>PermissionService.ActorHasPermissionAsync</c> directly - this story's own
/// RBAC integration seam.
///
/// <b>This story's own scope boundary.</b> No adapter here reaches BIO-CRD-005 (card status) or
/// any BIO-IDN-002 profile catalogue - neither is a declared dependency of this story (only
/// BIO-IDN-006 and BIO-ARC-006 are); see docs/features/bio-acc-001/README.md "Integration seam"
/// for why credential class, location, presented card status and profile keys are caller-supplied
/// evaluation inputs rather than gateway-resolved facts.
///
/// <b>ADR-0006 Decision 3, applied from the start.</b> <see cref="IActorProvenanceStore"/> is
/// loaded from <c>IConfiguration</c> here, the same out-of-band provisioning path BIO-IDN-006's
/// own <c>ActorProvenanceGate</c> already established - see that class's own header for the full
/// ADR-0006 rationale and disclosed residual scope.
/// </summary>
public sealed class BioAcc001Module : IBiologModule
{
    public string Name => "AccessPolicy";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddSingleton<IAccessPolicyRepository, InMemoryAccessPolicyRepository>();
        services.AddSingleton<IAccessPolicyAuditSink, InMemoryAccessPolicyAuditSink>();
        services.AddSingleton<AccessPolicyLockRegistry>();
        services.AddScoped<IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<IIdentityStatusGateway>(
            sp => new IdentityStatusGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddScoped<AccessPolicyService>();
        services.AddScoped<PolicyEvaluationService>();
        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AccessPolicyEndpoints.Map(endpoints);
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

    private sealed class IdentityStatusGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService) : IIdentityStatusGateway
    {
        public async Task<IdentityStatus?> GetStatusAsync(string personId, CancellationToken cancellationToken = default)
        {
            var person = await personService.GetAsync(personId, cancellationToken);
            return person is not null && IdentityStatusExtensions.TryParse(person.Status.ToWireString(), out var status) ? status : null;
        }
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }
}
