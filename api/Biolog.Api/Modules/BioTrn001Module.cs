using Biolog.Api.Tenancy;
using Biolog.Features.Transport;
using Biolog.Features.Transport.Api;
using Biolog.Features.Transport.Audit;
using Biolog.Features.Transport.Demo;
using Biolog.Features.Transport.Domain;
using Biolog.Features.Transport.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Routes, buses and person-assignment model (BIO-TRN-001), registered the same way every
/// sibling identity-wave module wraps its feature project.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <see cref="PersonExistenceGatewayAdapter"/> wraps BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> directly. <see cref="DeviceExistenceGatewayAdapter"/> wraps
/// BIO-DEV-001's own already-registered <c>Biolog.Api.Features.DeviceRegistry.DeviceService.GetAsync</c>
/// directly - BIO-DEV-001 is not a story-keyed feature project (it lives inside
/// <c>Biolog.Api</c> itself), so this adapter is this feature's ONLY coupling to it.
/// <see cref="PermissionGatewayAdapter"/> wraps BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly - this story's RBAC integration seam
/// for its one authorization decision (<c>PersonAssignmentService.CreateAsync</c>/<c>EndAsync</c>).
///
/// <see cref="TransportDemoBridgeService"/> is registered Singleton alongside the Singleton
/// in-memory repositories/audit sink it depends on directly (AC05's own headerless UI bridge).
/// </summary>
public sealed class BioTrn001Module : IBiologModule
{
    public string Name => "Transport";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "Rbac", "DeviceRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<IRouteRepository, InMemoryRouteRepository>();
        services.AddSingleton<IVehicleRepository, InMemoryVehicleRepository>();
        services.AddSingleton<IStopRepository, InMemoryStopRepository>();
        services.AddSingleton<IPersonAssignmentRepository, InMemoryPersonAssignmentRepository>();
        services.AddSingleton<ITransportAuditSink, InMemoryTransportAuditSink>();
        services.AddSingleton<RouteLockRegistry>();
        services.AddSingleton<VehicleLockRegistry>();
        services.AddSingleton<StopLockRegistry>();
        services.AddSingleton<PersonAssignmentLockRegistry>();
        services.AddSingleton<PersonAssignmentUniquenessLockRegistry>();

        services.AddScoped<IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<IDeviceExistenceGateway>(
            sp => new DeviceExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Api.Features.DeviceRegistry.DeviceService>()));
        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));

        services.AddScoped<RouteService>();
        services.AddScoped<VehicleService>();
        services.AddScoped<StopService>();
        services.AddScoped<PersonAssignmentService>();

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddSingleton<TransportDemoBridgeService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        TransportEndpoints.Map(endpoints);
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

    private sealed class DeviceExistenceGatewayAdapter(Biolog.Api.Features.DeviceRegistry.DeviceService deviceService) : IDeviceExistenceGateway
    {
        public async Task<bool> ExistsAsync(string deviceId, CancellationToken cancellationToken = default) =>
            await deviceService.GetAsync(deviceId, cancellationToken) is not null;
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }
}
