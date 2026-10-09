using Biolog.Api.Tenancy;
using Biolog.Features.DuplicateReview;
using Biolog.Features.DuplicateReview.Api;
using Biolog.Features.DuplicateReview.Audit;
using Biolog.Features.DuplicateReview.Domain;
using Biolog.Features.DuplicateReview.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Duplicate review foundation (BIO-IDN-005), registered the same way
/// <see cref="BioIdn001Module"/>/<see cref="BioIdn002Module"/>/<see cref="BioIdn003Module"/>/
/// <see cref="BioIdn004Module"/> wrap their feature projects - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start (never inside <c>api/features/bio-idn-005/**</c>
/// first), so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter)
/// - ships in-story per ADR-0002 Decision 6's per-channel accommodation-ending rule.
///
/// <b>Applying BIO-IDN-002-QA-01 from the start.</b> <see cref="PersonAttributeGatewayAdapter"/>
/// is the concrete implementation of
/// <c>Biolog.Features.DuplicateReview.Domain.IPersonAttributeGateway</c> - this feature's own
/// narrow, read-only seam onto BIO-IDN-001's real store (see that interface's header). It wraps
/// BIO-IDN-001's own <c>Biolog.Features.PersonRegistry.PersonService</c> (already registered
/// Scoped by <see cref="BioIdn001Module"/>) directly: both modules resolve from the SAME scoped
/// DI container for a given request, so BIO-IDN-001's own <c>PersonService</c> sees the SAME
/// resolved tenant context this feature's service does, and its existing tenant-scoped
/// <c>GetAsync</c>/<c>ListAsync</c> already exclude a person that exists only in a different
/// tenant - no extra logic needed here to get BIO-IDN-005-AC04 for free.
///
/// <see cref="IdentifierAttributeGatewayAdapter"/> is the identical shape onto BIO-IDN-003's own
/// <c>InstitutionalIdentifierService.ListAsync(personId)</c>, filtered to <c>Active</c> records
/// only here (a retired value being legitimately reassigned is BIO-IDN-003's own designed
/// behaviour, not a duplicate signal - see that gateway interface's header).
///
/// <b>BIO-IDN-005-QA-01.</b> <c>IPersonContactPointRepository</c>/<c>PersonContactPointService</c>
/// need no adapter here at all - <c>PersonContactPoint</c> is owned by THIS SAME feature
/// project, not a sibling story's store, so <c>DuplicateReviewService</c> and
/// <c>PersonContactPointService</c> both consume the repository directly (registered Singleton,
/// like every other in-memory store in this project).
///
/// Depends on <see cref="TenantModule"/> (for <see cref="TenantContextMiddleware"/> to have
/// already run), <see cref="BioIdn001Module"/> and <see cref="BioIdn003Module"/> (genuine
/// runtime dependencies via the two adapters above).
/// </summary>
public sealed class BioIdn005Module : IBiologModule
{
    public string Name => "DuplicateReview";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "InstitutionalIdentifiers"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddSingleton<IDuplicateCandidateRepository, InMemoryDuplicateCandidateRepository>();
        services.AddSingleton<IDuplicateCandidateAuditSink, InMemoryDuplicateCandidateAuditSink>();
        services.AddSingleton<DuplicateCandidateLockRegistry>();
        services.AddSingleton<IPersonContactPointRepository, InMemoryPersonContactPointRepository>();
        services.AddSingleton<PersonContactPointLockRegistry>();
        services.AddScoped<IPersonAttributeGateway>(
            sp => new PersonAttributeGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<IIdentifierAttributeGateway>(
            sp => new IdentifierAttributeGatewayAdapter(sp.GetRequiredService<Biolog.Features.InstitutionalIdentifiers.InstitutionalIdentifierService>()));
        services.AddScoped<DuplicateReviewService>();
        services.AddScoped<PersonContactPointService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        DuplicateCandidateEndpoints.Map(endpoints);
        PersonContactPointEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PersonAttributeGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService) : IPersonAttributeGateway
    {
        public async Task<PersonAttributes?> GetAsync(string personId, CancellationToken cancellationToken = default)
        {
            var person = await personService.GetAsync(personId, cancellationToken);
            return person is null ? null : ToAttributes(person);
        }

        public async Task<IReadOnlyList<PersonAttributes>> ListOthersAsync(string excludingPersonId, CancellationToken cancellationToken = default)
        {
            var persons = await personService.ListAsync(cancellationToken);
            return [.. persons.Where(p => p.Id != excludingPersonId).Select(ToAttributes)];
        }

        private static PersonAttributes ToAttributes(Biolog.Features.PersonRegistry.Domain.Person person) =>
            new(person.Id, person.FirstName, person.LastName, person.BornOn);
    }

    private sealed class IdentifierAttributeGatewayAdapter(Biolog.Features.InstitutionalIdentifiers.InstitutionalIdentifierService identifierService)
        : IIdentifierAttributeGateway
    {
        public async Task<IReadOnlyList<IdentifierAttribute>> ListActiveValuesAsync(string personId, CancellationToken cancellationToken = default)
        {
            var identifiers = await identifierService.ListAsync(personId, cancellationToken);
            return
            [
                .. identifiers
                    .Where(i => i.Status == Biolog.Features.InstitutionalIdentifiers.Domain.IdentifierStatus.Active)
                    .Select(i => new IdentifierAttribute(
                        Biolog.Features.InstitutionalIdentifiers.Domain.IdentifierTypeExtensions.ToWireString(i.Type), i.CustomLabel, i.Value)),
            ];
        }
    }
}
