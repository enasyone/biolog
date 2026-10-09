using Biolog.Api.Tenancy;
using Biolog.Features.AccessDecisions.Domain;
using Biolog.Features.CardProduction.Domain;
using Biolog.Features.OperationalDashboard;
using Biolog.Features.OperationalDashboard.Api;
using Biolog.Features.OperationalDashboard.Domain;
using Biolog.Features.OperationalDashboard.Tenancy;
using Biolog.Features.PersonRegistry.Domain;

namespace Biolog.Api.Modules;

/// <summary>
/// Operational dashboard and health views (BIO-RPT-004), registered the same way every sibling
/// module wraps its feature project - written directly here, in <c>Biolog.Api.Modules</c>, from
/// the start, so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <b><see cref="IdentityHealthGatewayAdapter"/>, <see cref="AccessHealthGatewayAdapter"/> and
/// <see cref="ProductionHealthGatewayAdapter"/> are the real cross-story dependencies.</b> Each
/// wraps an already-registered repository interface from its own owning module
/// (<c>IPersonRepository</c>, <c>IAccessDecisionEventRepository</c>, <c>IProductionJobRepository</c>)
/// directly - the first two are <c>internal</c> to their own assemblies but already grant
/// <c>Biolog.Api</c> access via <c>InternalsVisibleTo</c> (BIO-IDN-001-QA-01 discipline); the third
/// is PUBLIC, the same already-public method BIO-RPT-002's own <c>ICardProductionGateway</c>
/// independently wraps. Zero edits to any of the three dependencies' own files.
///
/// Card/Device/Sync health are a disclosed HYBRID gap (see <c>Biolog.Features.OperationalDashboard.csproj</c>'s
/// own header and docs/features/bio-rpt-004/README.md "Which sources are wired, and why") -
/// <see cref="DashboardService"/> reports those three panels itself, with no gateway and no
/// dependency on BIO-DEV-001/BIO-CRD-005/BIO-CRD-006/BIO-MOB-003 at all.
///
/// <b><see cref="PermissionGatewayAdapter"/> (Seyi round-1, BIO-RPT-004-F01).</b> Wraps
/// BIO-IDN-006's own PUBLIC <c>PermissionService.HasPermissionAsync</c> directly - the exact same
/// dependency and wrapping shape BIO-RPT-003's own <c>RbacPermissionGatewayAdapter</c> already
/// established for its own export-permission gate. An actor id that fails
/// <c>HasPermissionAsync</c>'s own validation (not well-formed, or not found in the resolved
/// tenant) is treated as "not granted" rather than let the exception propagate - the same
/// fail-closed semantics as BIO-RPT-003's own adapter.
///
/// <b><see cref="ActorProvenanceGate"/> (Seyi round-2, BIO-RPT-004-F03, Critical).</b> Registered
/// and provisioned from <c>OperationalDashboard:ActorProvenanceSecret</c> the same way every
/// sibling story's own gate is - built on the CURRENT canonical shape (no fallback/dummy key),
/// not the earlier superseded one - see that file's own header for the full rationale.
///
/// <b><see cref="LastKnownHealthCache"/> (Seyi round-2, BIO-RPT-004-F04).</b> Singleton,
/// process-lifetime, the same reasoning as <c>ReportJobRunner</c>'s own Singleton registration in
/// <c>BioRpt002Module</c> - must survive past any one request's own Scoped lifetime so a later
/// failed read can retrieve an earlier successful one's values.
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioIdn001Module"/> (registered as
/// <c>"PersonRegistry"</c>), <see cref="BioAcc002Module"/> (registered as
/// <c>"AccessDecisions"</c>), <see cref="BioCrd007Module"/> (registered as
/// <c>"CardProduction"</c>) and <see cref="BioIdn006Module"/> (registered as <c>"Rbac"</c>).
/// </summary>
public sealed class BioRpt004Module : IBiologModule
{
    public string Name => "OperationalDashboard";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "AccessDecisions", "CardProduction", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        // Singleton: none of the three gateways resolve anything from a scoped accessor
        // (tenantId flows as an explicit GetAsync parameter) and every repository they wrap is
        // itself a Singleton - the same reasoning BioRpt002Module's own gateway registrations
        // already document.
        services.AddSingleton<IIdentityHealthGateway>(
            sp => new IdentityHealthGatewayAdapter(sp.GetRequiredService<IPersonRepository>()));
        services.AddSingleton<IAccessHealthGateway>(
            sp => new AccessHealthGatewayAdapter(sp.GetRequiredService<IAccessDecisionEventRepository>()));
        services.AddSingleton<IProductionHealthGateway>(
            sp => new ProductionHealthGatewayAdapter(sp.GetRequiredService<IProductionJobRepository>()));

        // Scoped, matching BioRpt003Module's own identical PermissionService wrapper: the wrapped
        // service itself resolves a Scoped tenant accessor.
        services.AddScoped<IDashboardPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddSingleton<LastKnownHealthCache>();

        services.AddScoped<DashboardService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        DashboardEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class IdentityHealthGatewayAdapter(IPersonRepository repository) : IIdentityHealthGateway
    {
        public async Task<IdentityCounts> GetAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var people = await repository.ListByTenantAsync(tenantId, cancellationToken);
            var active = 0;
            var suspended = 0;
            var other = 0;
            foreach (var person in people)
            {
                switch (person.Status)
                {
                    case PersonStatus.Active: active++; break;
                    case PersonStatus.Suspended: suspended++; break;
                    default: other++; break;
                }
            }

            return new IdentityCounts(people.Count, active, suspended, other);
        }
    }

    private sealed class AccessHealthGatewayAdapter(IAccessDecisionEventRepository repository) : IAccessHealthGateway
    {
        public async Task<AccessCounts> GetAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var events = await repository.ListForTenantAsync(tenantId, cancellationToken);
            var allowed = events.Count(e => e.Decision == AccessDecisionOutcome.Allow);
            return new AccessCounts(events.Count, allowed, events.Count - allowed);
        }
    }

    private sealed class ProductionHealthGatewayAdapter(IProductionJobRepository repository) : IProductionHealthGateway
    {
        public Task<ProductionCounts> GetAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var jobs = repository.ListForTenant(tenantId);
            var passed = 0;
            var failed = 0;
            var rejected = 0;
            var inProgress = 0;
            var cancelled = 0;
            foreach (var job in jobs)
            {
                switch (job.Status)
                {
                    case ProductionJobStatus.Passed: passed++; break;
                    case ProductionJobStatus.Failed: failed++; break;
                    case ProductionJobStatus.Rejected: rejected++; break;
                    case ProductionJobStatus.InProgress: inProgress++; break;
                    case ProductionJobStatus.Cancelled: cancelled++; break;
                }
            }

            return Task.FromResult(new ProductionCounts(jobs.Count, passed, failed, rejected, inProgress, cancelled));
        }
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IDashboardPermissionGateway
    {
        public async Task<bool> HasPermissionAsync(string personId, string permissionKey, CancellationToken cancellationToken = default)
        {
            try
            {
                var (granted, _) = await permissionService.HasPermissionAsync(personId, permissionKey, cancellationToken);
                return granted;
            }
            catch (Biolog.Features.Rbac.Domain.RbacError)
            {
                return false;
            }
        }
    }
}
