using Biolog.Api.Tenancy;
using Biolog.Features.PersonProfileRegistry;
using Biolog.Features.PersonProfileRegistry.Api;
using Biolog.Features.PersonProfileRegistry.Audit;
using Biolog.Features.PersonProfileRegistry.Domain;
using Biolog.Features.PersonProfileRegistry.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Configurable person profiles and hierarchy assignment (BIO-IDN-002), registered the same
/// way <see cref="BioIdn001Module"/> and every prior story's module wraps its feature project
/// - written directly here, in <c>Biolog.Api.Modules</c>, from the start (never inside
/// <c>api/features/bio-idn-002/**</c> first), so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to <see cref="BioIdn001Module"/>'s own
/// adapter) - ships in-story per ADR-0002 Decision 6's per-channel accommodation-ending rule,
/// the same reasoning BIO-IDN-001 established (BIO-TEN-002's host channel shipped before this
/// wave began).
///
/// <b>BIO-IDN-002-QA-01 (Seyi round 1).</b> <see cref="PersonExistenceGatewayAdapter"/> is the
/// concrete implementation of <c>Biolog.Features.PersonProfileRegistry.Domain.IPersonExistenceGateway</c>
/// - this feature's own narrow, read-only seam onto BIO-IDN-001's real store (see that
/// interface's header for the full rationale). It wraps BIO-IDN-001's own
/// <c>Biolog.Features.PersonRegistry.PersonService</c> (already registered Scoped by
/// <see cref="BioIdn001Module"/>) directly: both modules resolve from the SAME scoped DI
/// container for a given request, so BIO-IDN-001's own <c>PersonService</c> sees the SAME
/// resolved tenant context BIO-IDN-002's own services do, and its existing tenant-scoped
/// <c>GetAsync</c> already returns <c>null</c> for a person that exists only in a different
/// tenant - no extra logic needed here to get that for free.
///
/// Depends on <see cref="TenantModule"/> (for <see cref="TenantContextMiddleware"/> to have
/// already run) and on <see cref="BioIdn001Module"/> - now a genuine runtime dependency (via
/// <see cref="PersonExistenceGatewayAdapter"/>), not merely a documentation convenience the
/// way it was before this fix.
/// </summary>
public sealed class BioIdn002Module : IBiologModule
{
    public string Name => "PersonProfileRegistry";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddSingleton<IPersonProfileRepository, InMemoryPersonProfileRepository>();
        services.AddSingleton<IPersonProfileAuditSink, InMemoryPersonProfileAuditSink>();
        services.AddSingleton<PersonProfileLockRegistry>();
        services.AddScoped<Biolog.Features.PersonProfileRegistry.Domain.IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<PersonProfileService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PersonProfileEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PersonExistenceGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService)
        : Biolog.Features.PersonProfileRegistry.Domain.IPersonExistenceGateway
    {
        public async Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
            await personService.GetAsync(personId, cancellationToken) is not null;
    }
}
