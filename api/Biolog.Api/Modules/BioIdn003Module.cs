using Biolog.Api.Tenancy;
using Biolog.Features.InstitutionalIdentifiers;
using Biolog.Features.InstitutionalIdentifiers.Api;
using Biolog.Features.InstitutionalIdentifiers.Audit;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Biolog.Features.InstitutionalIdentifiers.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Institutional identifiers (BIO-IDN-003), registered the same way <see cref="BioIdn001Module"/>
/// and <see cref="BioIdn002Module"/> wrap their feature projects - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start (never inside <c>api/features/bio-idn-003/**</c>
/// first), so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to <see cref="BioIdn001Module"/>'s and
/// <see cref="BioIdn002Module"/>'s own adapters) - ships in-story per ADR-0002 Decision 6's
/// per-channel accommodation-ending rule.
///
/// <b>Applying BIO-IDN-002-QA-01 from the start.</b> <see cref="PersonExistenceGatewayAdapter"/>
/// is the concrete implementation of
/// <c>Biolog.Features.InstitutionalIdentifiers.Domain.IPersonExistenceGateway</c> - this
/// feature's own narrow, read-only seam onto BIO-IDN-001's real store (see that interface's
/// header for the full rationale, ported from BIO-IDN-002's own remediation). It wraps
/// BIO-IDN-001's own <c>Biolog.Features.PersonRegistry.PersonService</c> (already registered
/// Scoped by <see cref="BioIdn001Module"/>) directly: both modules resolve from the SAME scoped
/// DI container for a given request, so BIO-IDN-001's own <c>PersonService</c> sees the SAME
/// resolved tenant context this feature's services do, and its existing tenant-scoped
/// <c>GetAsync</c> already returns <c>null</c> for a person that exists only in a different
/// tenant - no extra logic needed here to get that for free.
///
/// Depends on <see cref="TenantModule"/> (for <see cref="TenantContextMiddleware"/> to have
/// already run) and on <see cref="BioIdn001Module"/> (a genuine runtime dependency via
/// <see cref="PersonExistenceGatewayAdapter"/>).
/// </summary>
public sealed class BioIdn003Module : IBiologModule
{
    public string Name => "InstitutionalIdentifiers";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddSingleton<IInstitutionalIdentifierRepository, InMemoryInstitutionalIdentifierRepository>();
        services.AddSingleton<IIdentifierUniquenessRuleRepository, InMemoryIdentifierUniquenessRuleRepository>();
        services.AddSingleton<IInstitutionalIdentifierAuditSink, InMemoryInstitutionalIdentifierAuditSink>();
        services.AddSingleton<InstitutionalIdentifierLockRegistry>();
        services.AddSingleton<IdentifierUniquenessRuleLockRegistry>();
        services.AddScoped<Biolog.Features.InstitutionalIdentifiers.Domain.IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<InstitutionalIdentifierService>();
        services.AddScoped<IdentifierUniquenessRuleService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        InstitutionalIdentifierEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PersonExistenceGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService)
        : Biolog.Features.InstitutionalIdentifiers.Domain.IPersonExistenceGateway
    {
        public async Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
            await personService.GetAsync(personId, cancellationToken) is not null;
    }
}
