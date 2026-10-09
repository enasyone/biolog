namespace Biolog.Features.Transport.Domain;

/// <summary>Storage for <see cref="VehicleRecord"/>. Same tenant-scoping and <c>internal</c> discipline as <see cref="IRouteRepository"/>.</summary>
internal interface IVehicleRepository
{
    Task<VehicleRecord> CreateAsync(VehicleRecord record, CancellationToken cancellationToken = default);

    Task<VehicleRecord?> GetAsync(string tenantId, string vehicleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VehicleRecord>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<VehicleRecord> ReplaceAsync(VehicleRecord expectedCurrent, VehicleRecord updated, CancellationToken cancellationToken = default);
}
