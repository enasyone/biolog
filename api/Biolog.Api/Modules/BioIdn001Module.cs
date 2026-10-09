using Biolog.Api.Tenancy;
using Biolog.Features.PersonRegistry;
using Biolog.Features.PersonRegistry.Api;
using Biolog.Features.PersonRegistry.Audit;
using Biolog.Features.PersonRegistry.Domain;
using Biolog.Features.PersonRegistry.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// The universal PERSON aggregate (BIO-IDN-001), registered the same way <see cref="DocumentModule"/>,
/// <see cref="DeviceRegistryModule"/>, <see cref="BioTen001Module"/> and <see cref="BioTen002Module"/>
/// wrap their feature projects: this thin adapter lives here, in <c>Biolog.Api.Modules</c>,
/// from the start - never inside <c>api/features/bio-idn-001/**</c> - so the dependency
/// between <c>Biolog.Api</c> and the feature project stays one-way
/// (<c>scripts/check-module-project-references.sh</c>, BIO-TEN-001's circular-reference
/// finding, BIO-TEN-002's identical fix). This module was written directly here, learning
/// from both of those - it was never written inside the feature project first and moved.
///
/// <b>Why this module ships in-story, not as a deferred follow-up integration.</b> ADR-0002
/// Decision 6: "the accommodation ends per-channel, not all at once ... once it ships, any
/// feature reachable through a verified custom hostname has a genuine shipped-host positive
/// path and the interim closure above no longer applies to it" and "obligation on every
/// W03+ tenant-plane story ... once the channel it depends on ships, its positive-path
/// evidence is upgraded to a real shipped-host test." BIO-TEN-002's host channel shipped and
/// merged to <c>develop</c> before this story started - PERSON is a NEW W04 tenant-plane
/// story built entirely after that point, so the deferred-integration accommodation BIO-DEV-001
/// and BIO-DOC-001 used (built before any real tenant resolver existed) does not apply here
/// the same way. Registering this module and wiring a real
/// <see cref="ICurrentTenantProvider"/> adapter is what makes
/// <c>tests/dotnet/Biolog.IntegrationTests/PersonHostChannelTests.cs</c>'s real, packaged-host
/// positive-path proof possible.
///
/// <see cref="CurrentTenantProviderAdapter"/> only READS the already-shipped, already-reviewed
/// <see cref="ITenantContextAccessor"/> (exactly as <c>DeviceService</c>, inside this same
/// assembly, already does directly) - this module does not modify
/// <see cref="TenantContextMiddleware"/>, <see cref="TenantContext"/> or
/// <see cref="ITenantContextAccessor"/> in any way, so it carries none of the risk BIO-TEN-002's
/// QA-01 required-closure did (that finding was about the RESOLUTION MECHANISM itself being
/// absent; here the mechanism already exists and is stable - this module only consumes it).
///
/// Depends on <see cref="TenantModule"/> so <see cref="TenantContextMiddleware"/> has already
/// run - and therefore <see cref="ITenantContextAccessor.Context"/> already reflects this
/// request's real resolved tenant, if any - before any PERSON endpoint executes, the same
/// dependency <see cref="DeviceRegistryModule"/> already declares for the same reason.
///
/// Also registers <see cref="ICrossTenantPersonRevocationLookup"/> (approved 2026-09-24,
/// applying <see cref="DeviceRegistryModule"/>'s <c>ICrossTenantDeviceLookup</c> precedent
/// directly): a narrow, internal exception for BIO-ACC-003's device-authenticated revocation-
/// delta feed, which has no ambient tenant channel to read. Bound to the same
/// <see cref="InMemoryPersonRepository"/> singleton instance as <see cref="IPersonRepository"/>
/// - one store, two interfaces - the identical pattern <see cref="DeviceRegistryModule"/> uses.
/// </summary>
public sealed class BioIdn001Module : IBiologModule
{
    public string Name => "PersonRegistry";
    public IReadOnlyCollection<string> DependsOn => ["Tenant"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddSingleton<InMemoryPersonRepository>();
        services.AddSingleton<IPersonRepository>(sp => sp.GetRequiredService<InMemoryPersonRepository>());
        services.AddSingleton<ICrossTenantPersonRevocationLookup>(sp => sp.GetRequiredService<InMemoryPersonRepository>());
        services.AddSingleton<IPersonAuditSink, InMemoryPersonAuditSink>();
        services.AddSingleton<PersonLockRegistry>();
        services.AddScoped<PersonService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PersonEndpoints.Map(endpoints);
    }

    /// <summary>
    /// Reads the real, already-resolved <see cref="ITenantContextAccessor.Context"/> and
    /// projects it onto this feature's own minimal <see cref="ICurrentTenantProvider"/> shape
    /// - see that interface's header (in the feature project) for why this feature defines
    /// its own narrower abstraction instead of depending on <see cref="TenantContext"/>
    /// directly. Registered Scoped in lockstep with <see cref="ITenantContextAccessor"/>
    /// itself, so both resolve to values for the SAME request.
    /// </summary>
    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }
}
