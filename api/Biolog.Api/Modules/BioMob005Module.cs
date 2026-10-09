using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Biolog.Features.OfflineEnrolment;
using Biolog.Features.OfflineEnrolment.Api;
using Biolog.Features.OfflineEnrolment.Audit;
using Biolog.Features.OfflineEnrolment.Domain;

namespace Biolog.Api.Modules;

/// <summary>
/// Limited offline enrolment package (BIO-MOB-005), registered the same way every sibling module
/// wraps its feature project - written directly here, in <c>Biolog.Api.Modules</c>, from the
/// start, so the dependency stays one-way.
///
/// <b>The ambient-tenant wall (device-authenticated routes), mirroring Kemi's BIO-MOB-003 ruling
/// and BIO-ACC-003's identical extension of it.</b> A device-authenticated capture-policy pull or
/// package submission has no host/principal channel - <see cref="DeviceTenantResolutionGatewayAdapter"/>
/// resolves the tenant from the authenticated device's own registered record via the existing
/// <see cref="ICrossTenantDeviceLookup"/> escape hatch on <c>DeviceRegistry</c> (already reviewed
/// for BIO-MOB-003/BIO-ACC-003 - this is a third, independent feature-side gateway consuming the
/// same seam, not a new grant).
///
/// <b>AC03/AC04's duplicate detection and human resolution queue are deliberately self-contained
/// within this feature</b> (<see cref="Biolog.Features.OfflineEnrolment.Domain.EnrolmentDuplicateReviewService"/>),
/// scanning only this feature's own <c>EnrolmentPackage</c> records - not a dependency on
/// BIO-IDN-005 or BIO-BIO-006 (neither declared, both internal to their own projects). See
/// <c>EnrolmentDuplicateReviewService.cs</c>'s own header for the full reasoning, which mirrors
/// BIO-BIO-006's own precedent for the identical situation.
///
/// This feature owns its own device-credential namespace and its own actor-provenance secret,
/// distinct from every sibling feature's own, per this repository's "no cross-feature reference"
/// convention.
///
/// Depends on <see cref="DeviceRegistryModule"/> (device auth/tenant resolution for the
/// mobile-facing routes), <see cref="TenantModule"/> (ambient tenant for the human-facing admin
/// routes) and BIO-IDN-006's <c>Rbac</c> module (the human-facing routes' permission gate).
/// Deliberately does NOT depend on <c>PersonRegistry</c> - materializing an accepted enrolment
/// package into a real BIO-IDN-001 person record is explicitly out of this story's scope (see
/// <c>Domain/EnrolmentPackage.cs</c>'s own header).
/// </summary>
public sealed class BioMob005Module : IBiologModule
{
    public string Name => "OfflineEnrolment";
    public IReadOnlyCollection<string> DependsOn => ["DeviceRegistry", "Tenant", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDeviceTenantResolutionGateway>(
            sp => new DeviceTenantResolutionGatewayAdapter(sp.GetRequiredService<ICrossTenantDeviceLookup>()));

        services.AddSingleton(_ => DeviceCredentialProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<IDeviceCredentialStore>(sp => sp.GetRequiredService<InMemoryDeviceCredentialStore>());
        services.AddScoped<IDeviceAuthenticator, HeaderDeviceAuthenticator>();

        services.AddScoped<Biolog.Features.OfflineEnrolment.Tenancy.ICurrentTenantProvider>(
            sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<IEnrolmentAuditSink, InMemoryEnrolmentAuditSink>();
        services.AddSingleton<IEnrolmentCapturePolicyRepository, InMemoryEnrolmentCapturePolicyRepository>();
        services.AddSingleton<IEnrolmentPackageRepository, InMemoryEnrolmentPackageRepository>();
        services.AddSingleton<IEnrolmentDuplicateCandidateRepository, InMemoryEnrolmentDuplicateCandidateRepository>();
        services.AddSingleton<Biolog.Features.OfflineEnrolment.Domain.EnrolmentDuplicateReviewService>();

        services.AddScoped<EnrolmentPackageService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        EnrolmentEndpoints.Map(endpoints);
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

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : Biolog.Features.OfflineEnrolment.Tenancy.ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }
}
