using Biolog.Api.Features.DeviceRegistry;
using Biolog.Features.OfflineSync;
using Biolog.Features.OfflineSync.Api;
using Biolog.Features.OfflineSync.Audit;
using Biolog.Features.OfflineSync.Domain;

namespace Biolog.Api.Modules;

/// <summary>
/// Offline sync central intake (BIO-MOB-003), registered the same way every sibling module
/// wraps its feature project - written directly here, in <c>Biolog.Api.Modules</c>, from the
/// start, so the dependency stays one-way.
///
/// <b>Kemi's architectural ruling on BIO-MOB-003's acceptance FAIL (2026-09-23).</b> Chioma's
/// acceptance round 1 found no executable central offline-intake endpoint existed anywhere in
/// this repository - the mobile-side <c>HttpSyncTransport</c> (BIO-MOB-003-FEMI-01/02/03) was a
/// real, well-wired client with nothing real to talk to. Ruling: build a real minimal endpoint
/// INSIDE this story (feature-path-ownership.json already grants
/// <c>api/features/bio-mob-003/**</c>; AC02/AC03/AC05 are server-side by the story's own text,
/// so this is unfinished approved scope, not scope creep; the wire contract is already frozen -
/// <c>offline-mutation.schema.json</c>, ADR-0001 Decisions 3.4/3.5 - so there is no open design
/// question, only implementation).
///
/// <b>The hard guardrail Kemi found while checking whether the frozen schema is safe to
/// implement literally.</b> <c>offline-mutation.schema.json</c> carries <c>tenant_id</c> as a
/// request BODY field. ADR-0002 Decision 1.1 is explicit that a body field is not a channel -
/// trusting one to route a mutation is the cross-tenant-substitution threat as a shipped
/// feature, not a hypothetical. <see cref="DeviceTenantResolutionGatewayAdapter"/> is the fix:
/// it depends on <see cref="ICrossTenantDeviceLookup"/> directly - a new, narrow, internal
/// lookup added to <c>DeviceRegistry</c> for exactly this caller, not on
/// <see cref="DeviceService"/> (whose <c>GetAsync</c> requires an ambient tenant this module
/// has none of, and whose constructor the built-in DI container must activate by reflection,
/// which requires it stay public - the new capability had to live somewhere DI never
/// auto-activates by reflection instead). It resolves the tenant a request may act on from
/// the authenticated device's own registered <see cref="Device.TenantId"/> - never from the
/// request body. The body's own <c>tenant_id</c> is still read, but only as a claim
/// <see cref="OfflineMutationIntakeService"/> cross-validates against this independently
/// resolved value, rejecting on mismatch (<see cref="OfflineSyncError.TenantMismatch"/>).
///
/// <b>Device credential provisioning</b> is loaded from <see cref="IConfiguration"/> here, the
/// same out-of-band path every sibling device-authenticated feature (BIO-CRD-001, BIO-PHO-002)
/// establishes, under this feature's own configuration section
/// (<see cref="DeviceCredentialProvisioning.ConfigurationSectionPath"/>) rather than sharing one
/// across features.
///
/// Depends on <see cref="DeviceRegistryModule"/> - the one genuine runtime dependency, via
/// <see cref="DeviceTenantResolutionGatewayAdapter"/>. Deliberately does NOT depend on
/// <see cref="TenantModule"/>: unlike every sibling module, this feature has no ambient tenant
/// context to fall back on - the whole point of this ruling is that the tenant comes from the
/// device, not from a host/principal channel BIO-INT-001 has not shipped yet. That also means,
/// unlike sibling features gated on BIO-INT-001's still-unshipped resolution boundary, this
/// endpoint can genuinely function in production today.
/// </summary>
public sealed class BioMob003Module : IBiologModule
{
    public string Name => "OfflineSync";
    public IReadOnlyCollection<string> DependsOn => ["DeviceRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDeviceTenantResolutionGateway>(
            sp => new DeviceTenantResolutionGatewayAdapter(sp.GetRequiredService<ICrossTenantDeviceLookup>()));

        services.AddSingleton(_ => DeviceCredentialProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<IDeviceCredentialStore>(sp => sp.GetRequiredService<InMemoryDeviceCredentialStore>());
        services.AddScoped<IDeviceAuthenticator, HeaderDeviceAuthenticator>();

        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<IAggregateVersionLedger, InMemoryAggregateVersionLedger>();
        services.AddSingleton<IOfflineSyncAuditSink, InMemoryOfflineSyncAuditSink>();
        services.AddSingleton<OfflineMutationLockRegistry>();

        services.AddScoped<OfflineMutationIntakeService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        OfflineSyncEndpoints.Map(endpoints);
    }

    private sealed class DeviceTenantResolutionGatewayAdapter(ICrossTenantDeviceLookup crossTenantLookup) : IDeviceTenantResolutionGateway
    {
        public async Task<ResolvedDevice?> ResolveAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            // Kemi's ruling, 2026-09-23: DeviceService.GetAsync requires an already-resolved
            // ambient TenantContext this module deliberately has none of (see this module's
            // own header) - circular for a call whose entire purpose IS resolving the tenant.
            // ICrossTenantDeviceLookup is the fix: a narrow, internal, cross-tenant lookup
            // added to DeviceRegistry (api/Biolog.Api/Features/DeviceRegistry/
            // ICrossTenantDeviceLookup.cs) for exactly this caller - constructed here via a
            // manual factory lambda (see RegisterServices above), not DI's reflection-based
            // activator, so its internal visibility is no obstacle.
            var device = await crossTenantLookup.GetByIdAsync(deviceId, cancellationToken);
            return device is not null && device.Status == DeviceStatus.Active
                ? new ResolvedDevice(device.Id, device.TenantId)
                : null;
        }
    }
}
