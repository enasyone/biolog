using Biolog.Api.Tenancy;
using Biolog.Features.Photography;
using Biolog.Features.Photography.Api;
using Biolog.Features.Photography.Audit;
using Biolog.Features.Photography.Domain;
using Biolog.Features.Photography.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Immutable photo master and derivatives (BIO-PHO-001), registered the same way every sibling
/// identity/card-wave module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start (never inside <c>api/features/bio-pho-001/**</c>
/// first), so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <b>Applying BIO-IDN-002-QA-01 from the start.</b> <see cref="PersonExistenceGatewayAdapter"/>
/// wraps BIO-IDN-001's own already-registered <c>PersonService.GetAsync</c> directly - the same
/// tenant-scoping-for-free reasoning every prior gateway's header documents.
///
/// <b>Dependency: BIO-CRD-001 is sequencing, not coupling.</b> The approved story lists
/// <c>BIO-CRD-001</c> as a dependency, matching this epic's established "Dependencies in story
/// metadata is build-wave sequencing, not a runtime service dependency" precedent (used since
/// BIO-IDN-002/003): nothing in this module or the feature project it wraps calls into
/// <c>BrandAssetsModule</c> or references brand-asset types - a photo master/derivative and a
/// brand asset are sibling card-production concerns with no runtime overlap in this story's own
/// acceptance criteria.
///
/// <b>ADR-0006 Decision 1/2/4.</b> This module wires no authorization/provenance gate - see
/// <see cref="ActorResolver"/>'s header for why that is the correct, disclosed shape here, not
/// an omission.
///
/// Depends on <see cref="TenantModule"/> (for <see cref="TenantContextMiddleware"/> to have
/// already run) and <see cref="BioIdn001Module"/> (a genuine runtime dependency via the
/// existence gateway adapter).
/// </summary>
public sealed class BioPho001Module : IBiologModule
{
    public string Name => "Photography";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddScoped<IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));

        services.AddSingleton<IPhotoMasterRepository, InMemoryPhotoMasterRepository>();
        services.AddSingleton<IPhotoDerivativeRepository, InMemoryPhotoDerivativeRepository>();
        services.AddSingleton<IPhotoAssignmentRepository, InMemoryPhotoAssignmentRepository>();
        services.AddSingleton<IPhotoDerivativeGenerator, MetadataOnlyPhotoDerivativeGenerator>();
        services.AddSingleton<IPhotoAuditSink, InMemoryPhotoAuditSink>();
        services.AddSingleton<PhotoMasterLockRegistry>();
        services.AddSingleton<PhotoAssignmentLockRegistry>();

        services.AddScoped<PhotoMasterService>();
        services.AddScoped<PhotoDerivativeService>();
        services.AddScoped<PhotoAssignmentService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PhotoEndpoints.Map(endpoints);
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
