using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Biolog.Features.AccessCache;
using Biolog.Features.AccessCache.Api;
using Biolog.Features.AccessCache.Audit;
using Biolog.Features.AccessCache.Demo;
using Biolog.Features.AccessCache.Domain;
using Biolog.Features.PersonRegistry.Domain;

namespace Biolog.Api.Modules;

/// <summary>
/// Offline deny-list and cached access decisions (BIO-ACC-003), registered the same way every
/// sibling module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start, so the dependency stays one-way.
///
/// <b>Scope decision (god, 2026-09-24).</b> AC02/AC03 implement identity-level revocation only
/// (via PersonRegistry), not card-level (BIO-CRD-006 is not a declared dependency of this
/// story - see <see cref="Biolog.Features.AccessCache.Domain.IIdentityRevocationFeedGateway"/>'s
/// own header for the full reasoning and the disclosed BIO-CRD-006 gap this leaves out of
/// scope).
///
/// <b>The ambient-tenant wall (approved 2026-09-24, mirroring Kemi's BIO-MOB-003 ruling
/// exactly).</b> A device-authenticated request has no host/principal channel, so
/// <c>PersonService</c>'s own ambient-tenant-only surface (including BIO-ACC-001's existing
/// <c>IIdentityStatusGateway</c>) cannot be called from <see cref="RevocationFeedGatewayAdapter"/>.
/// <see cref="Biolog.Features.PersonRegistry.Domain.ICrossTenantPersonRevocationLookup"/> is the
/// fix - a narrow, internal, explicit-tenant lookup added to PersonRegistry for exactly this
/// caller, the identical shape <see cref="DeviceRegistryModule"/>'s own
/// <see cref="ICrossTenantDeviceLookup"/> already established for BIO-MOB-003.
///
/// This feature owns its own device-credential namespace and its own actor-provenance secret
/// (<see cref="Biolog.Features.AccessCache.Api.DeviceCredentialProvisioning"/>,
/// <see cref="Biolog.Features.AccessCache.Api.ActorProvenanceProvisioning"/>) - distinct from
/// every sibling feature's own, per this repository's "no cross-feature reference" convention.
///
/// Depends on <see cref="PersonRegistryModule"/> aliasing note: the actual dependency name is
/// <c>"PersonRegistry"</c> (BIO-IDN-001's own <see cref="BioIdn001Module.Name"/>), plus
/// <see cref="DeviceRegistryModule"/> (device auth/tenant resolution),
/// <see cref="TenantModule"/> (the human-facing read's ambient tenant) and BIO-IDN-006's
/// <c>Rbac</c> module (the human-facing read's permission gate).
/// </summary>
public sealed class BioAcc003Module : IBiologModule
{
    public string Name => "AccessCache";
    public IReadOnlyCollection<string> DependsOn => ["PersonRegistry", "DeviceRegistry", "Tenant", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDeviceTenantResolutionGateway>(
            sp => new DeviceTenantResolutionGatewayAdapter(sp.GetRequiredService<ICrossTenantDeviceLookup>()));

        services.AddSingleton(_ => DeviceCredentialProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<IDeviceCredentialStore>(sp => sp.GetRequiredService<InMemoryDeviceCredentialStore>());
        services.AddScoped<IDeviceAuthenticator, HeaderDeviceAuthenticator>();

        services.AddScoped<Biolog.Features.AccessCache.Tenancy.ICurrentTenantProvider>(
            sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddScoped<IIdentityRevocationFeedGateway>(
            sp => new RevocationFeedGatewayAdapter(sp.GetRequiredService<ICrossTenantPersonRevocationLookup>()));

        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<IPresentationEventLog, InMemoryPresentationEventLog>();
        services.AddSingleton<IAccessCacheAuditSink, InMemoryAccessCacheAuditSink>();

        services.AddScoped<AccessCacheService>();
        services.AddSingleton<AccessCacheDemoBridgeService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AccessCacheEndpoints.Map(endpoints);
    }

    private sealed class DeviceTenantResolutionGatewayAdapter(ICrossTenantDeviceLookup crossTenantLookup) : IDeviceTenantResolutionGateway
    {
        public async Task<ResolvedDevice?> ResolveAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            var device = await crossTenantLookup.GetByIdAsync(deviceId, cancellationToken);
            return device is not null && device.Status == DeviceStatus.Active
                ? new ResolvedDevice(device.Id, device.TenantId)
                : null;
        }
    }

    /// <summary>Maps PersonRegistry's own <c>Person</c> down to this feature's minimum-necessary <see cref="RevocationEntry"/> (AC01) at the boundary - nothing beyond person id and the instant it was revoked ever crosses out of this adapter.</summary>
    private sealed class RevocationFeedGatewayAdapter(ICrossTenantPersonRevocationLookup lookup) : IIdentityRevocationFeedGateway
    {
        public async Task<IReadOnlyList<RevocationEntry>> ListRevokedSinceAsync(string tenantId, string? since, CancellationToken cancellationToken = default)
        {
            var people = await lookup.ListRevokedSinceAsync(tenantId, since, cancellationToken);
            return [.. people.Select(p => new RevocationEntry(p.Id, p.UpdatedAt))];
        }
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : Biolog.Features.AccessCache.Tenancy.ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }
}
