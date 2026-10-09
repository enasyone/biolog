using Biolog.Features.TransportEvents.Domain;
using Biolog.Features.TransportEvents.Tenancy;

namespace Biolog.Features.TransportEvents.Tests;

public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>Defaults to "every person exists"; a test that specifically proves the existence check constructs this with <paramref name="existingPersonIds"/> set.</summary>
public sealed class FakePersonExistenceGateway(IReadOnlySet<string>? existingPersonIds = null) : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(existingPersonIds is null || existingPersonIds.Contains(personId));
}

/// <summary>Test double for <see cref="ITransportRegistryGateway"/>. A caller registers whichever routes/vehicles/stops a test needs via <see cref="Routes"/>/<see cref="ActiveVehicleIds"/>/<see cref="ActiveStopIds"/>.</summary>
public sealed class FakeTransportRegistryGateway : ITransportRegistryGateway
{
    public Dictionary<string, TransportRouteInfo> Routes { get; } = [];
    public HashSet<string> ActiveVehicleIds { get; } = [];
    public HashSet<string> ActiveStopIds { get; } = [];

    public Task<TransportRouteInfo?> GetRouteAsync(string routeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Routes.TryGetValue(routeId, out var route) ? route : null);

    public Task<bool> VehicleIsActiveAsync(string vehicleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ActiveVehicleIds.Contains(vehicleId));

    public Task<bool> StopIsActiveAsync(string stopId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ActiveStopIds.Contains(stopId));
}
