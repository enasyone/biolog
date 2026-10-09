using Biolog.Features.TenantFeatureConfig;
using Biolog.Features.TenantFeatureConfig.Api;
using Biolog.Features.TenantFeatureConfig.Audit;
using Biolog.Features.TenantFeatureConfig.Domain;

namespace Biolog.Api.Modules;

/// <summary>
/// Tenant feature module configuration (BIO-TEN-003), registered into the host the same thin
/// -adapter shape every module in this directory uses - see <see cref="BioTen001Module"/>'s
/// own header for why this wrapper lives here (in <c>Biolog.Api.Modules</c>) rather than
/// inside <c>api/features/bio-ten-003/**</c>: keeps the <c>Biolog.Api</c> &lt;-&gt; feature
/// project dependency one-way (<c>scripts/check-module-project-references.sh</c>).
///
/// <b>Round-2 remediation (ZB-TEN003-01/BIO-TEN-003-F01, Critical).</b>
/// <see cref="PermissionGatewayAdapter"/> wraps BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly - this story's RBAC integration
/// seam, the identical cross-story gateway shape BIO-INT-002's own module uses.
/// <see cref="TenantExistenceGatewayAdapter"/> wraps BIO-TEN-001's own already-registered
/// <c>TenantService.GetAsync</c> directly - closes the "configuration for a nonexistent
/// tenant" finding, previously a disclosed boundary this review found was the defect itself.
/// Now depends on <see cref="BioIdn006Module"/> (registered as <c>"Rbac"</c>) and
/// <see cref="BioTen001Module"/> (registered as <c>"TenantRegistry"</c>).
///
/// <see cref="TenantFeatureConfigurationService"/> is registered as a DI singleton (same
/// reasoning as BIO-TEN-001's own <c>TenantService</c>: exactly one instance, and therefore
/// one shared per-tenant lock table and one shared Face-1N sync cache, serving every request
/// in this process), and also registered under <see cref="ITenantFeatureGateway"/> - the
/// read-only seam this story's AC04 leaves ready for a future story to consume for the other
/// five modules.
/// </summary>
public sealed class BioTen003Module : IBiologModule
{
    public string Name => "TenantFeatureConfig";
    public IReadOnlyCollection<string> DependsOn => ["Rbac", "TenantRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ITenantFeatureConfigurationStore, InMemoryTenantFeatureConfigurationStore>();
        services.AddSingleton<ITenantFeatureConfigurationAuditSink, InMemoryTenantFeatureConfigurationAuditSink>();
        services.AddSingleton<TenantFeatureConfigurationService>();
        services.AddSingleton<ITenantFeatureGateway>(sp => sp.GetRequiredService<TenantFeatureConfigurationService>());

        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddScoped<ITenantExistenceGateway>(
            sp => new TenantExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.TenantRegistry.TenantService>()));

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
    }

    /// <summary>
    /// TD-WEB-PROVENANCE-BRIDGE-REVERIFY (Tunde, 2026-10-07): this story's own share of the
    /// obligation Seyi's re-verification (`evidence/TD-WEB-PROVENANCE-BRIDGE/codex/
    /// REVERIFY-Seyi-BIO-RPT-004-BIO-TEN-003.md`) left open - a real, positive, authorized LIVE
    /// browser workflow proof, not just the pre-bridge negative-only one.
    /// <see cref="DemoSessionIdentitySeed"/> (round 6, Emeka) seeds the three fixed
    /// <c>per_&lt;UUIDv7&gt;</c> demo-actor Person+PersonRoleAssignment rows into the one
    /// hardcoded <c>WebSessionBridge:SyntheticTenantId</c> synthetic tenant, but deliberately
    /// seeds NO <see cref="Biolog.Features.Rbac.Domain.RolePermissionGrant"/> rows - that story's
    /// own header names this as each affected story's own reverify obligation. This seeds
    /// exactly the two this story's own AC05 workflow needs
    /// (<see cref="TenantFeatureConfigurationEndpoints.ReadPermissionKey"/>/
    /// <see cref="TenantFeatureConfigurationEndpoints.ManagePermissionKey"/>) for
    /// <see cref="Biolog.Features.Rbac.Domain.RoleType.TenantAdmin"/> (the "admin" demo role) -
    /// both, so the admin workflow can genuinely load AND toggle a module - plus the read key
    /// alone for <see cref="Biolog.Features.Rbac.Domain.RoleType.ReadOnly"/> (the "viewer" demo
    /// role), so the unprivileged-denial proof exercises the real manage-permission boundary
    /// (403 on write) rather than a blanket read-time rejection, matching round 5/6's own
    /// "admin and viewer reach different enforcement outcomes" shape.
    ///
    /// Also registers the synthetic tenant ITSELF into BIO-TEN-001's own
    /// <see cref="Biolog.Features.TenantRegistry.Domain.ITenantRegistry"/> - a step
    /// <see cref="DemoSessionIdentitySeed"/> never needed, because BIO-RPT-004/BIO-BIO-009
    /// resolve tenant AMBIENTLY (<c>DemoSessionTenantResolver</c>, never checking whether that
    /// resolved id is itself a REGISTERED tenant). This story is different by design - the
    /// route carries the tenant id EXPLICITLY in the URL path (an admin console reading/writing
    /// several tenants from one surface, Round-2's own `ITenantExistenceGateway` remediation) -
    /// so without a real `TenantRegistry` row for this exact id, the endpoint 404s
    /// (`tenant_not_found`) after passing provenance and permission, before ever reaching the
    /// service. Fail-closed, inert when unconfigured, idempotent - same discipline as
    /// <see cref="DemoSessionIdentitySeed"/>; writes directly to the repositories (seed data,
    /// never through <see cref="Biolog.Features.TenantRegistry.TenantService"/> or
    /// <see cref="Biolog.Features.Rbac.PermissionService.SetGrantAsync"/>, both of which expect
    /// a real administrative actor and an audit trail a fixed startup fixture has neither of).
    /// </summary>
    public void Configure(IApplicationBuilder app)
    {
        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var tenantId = configuration["WebSessionBridge:SyntheticTenantId"];
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        SeedSyntheticTenantIfMissing(app.ApplicationServices, tenantId);
        SeedDemoPermissionGrantsIfMissing(app.ApplicationServices, tenantId);
    }

    private static void SeedSyntheticTenantIfMissing(IServiceProvider services, string tenantId)
    {
        var registry = services.GetRequiredService<Biolog.Features.TenantRegistry.Domain.ITenantRegistry>();
        if (registry.GetAsync(tenantId).GetAwaiter().GetResult() is not null)
        {
            return;
        }

        var now = Biolog.Features.TenantRegistry.Identifiers.UtcInstant.Now();
        registry.CreateAsync(new Biolog.Features.TenantRegistry.Domain.Tenant
        {
            Id = tenantId,
            Name = "TD-WEB-PROVENANCE-BRIDGE demo tenant (SYNTHETIC)",
            Status = Biolog.Features.TenantRegistry.Domain.TenantStatus.Active,
            LicensePlan = "synthetic-demo",
            LicenseSeats = 3,
            RoutingRegion = "eu-west",
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        }).GetAwaiter().GetResult();
    }

    private static void SeedDemoPermissionGrantsIfMissing(IServiceProvider services, string tenantId)
    {
        var grants = services.GetRequiredService<Biolog.Features.Rbac.Domain.IRolePermissionGrantRepository>();

        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.TenantAdmin, TenantFeatureConfigurationEndpoints.ReadPermissionKey);
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.TenantAdmin, TenantFeatureConfigurationEndpoints.ManagePermissionKey);
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.ReadOnly, TenantFeatureConfigurationEndpoints.ReadPermissionKey);
    }

    private static void SeedGrant(Biolog.Features.Rbac.Domain.IRolePermissionGrantRepository grants, string tenantId, Biolog.Features.Rbac.Domain.RoleType role, string permissionKey)
    {
        if (grants.GetAsync(tenantId, role, permissionKey).GetAwaiter().GetResult() is not null)
        {
            return;
        }

        var now = Biolog.Features.Rbac.Identifiers.UtcInstant.Now();
        grants.UpsertAsync(new Biolog.Features.Rbac.Domain.RolePermissionGrant(tenantId, role, permissionKey)
        {
            Granted = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        }).GetAwaiter().GetResult();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        TenantFeatureConfigurationEndpoints.Map(endpoints);
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    private sealed class TenantExistenceGatewayAdapter(Biolog.Features.TenantRegistry.TenantService tenantService) : ITenantExistenceGateway
    {
        public async Task<bool> ExistsAsync(string tenantId, CancellationToken cancellationToken = default) =>
            await tenantService.GetAsync(tenantId, cancellationToken) is not null;
    }
}
