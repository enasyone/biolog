using Biolog.Api.Tenancy;
using Biolog.Features.DeviceAdapters;

namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>
/// Tenant-scoped device storage (BIO-DEV-001-AC04). Every method takes the resolved
/// <see cref="TenantContext"/> explicitly rather than reading an ambient value, so a
/// caller can never reach a device method without a tenant context already in hand - the
/// same "cannot be built without a resolved context" discipline ADR-0002 §1.5 describes
/// for a real tenant repository.
///
/// <see cref="GetAsync"/> returns <c>null</c> for a device that does not exist AND for one
/// that exists in a different tenant (ADR-0002 §4.3: a cross-tenant access answers "not
/// found", never "forbidden") - callers must not distinguish the two cases from this
/// interface alone, because the interface itself is the boundary that makes them
/// indistinguishable to whoever asks.
/// </summary>
public interface IDeviceRepository
{
    Task<Device> CreateAsync(TenantContext tenant, DeviceRegistrationRequest request, CancellationToken cancellationToken);

    Task<Device?> GetAsync(TenantContext tenant, string deviceId, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="DeviceConcurrencyConflictException"/> if
    /// <paramref name="expectedVersion"/> does not match the stored version (ADR-0001
    /// §3.2). Returns <c>null</c> - not an exception - for a device the tenant cannot see,
    /// preserving the same not-found-not-forbidden rule as <see cref="GetAsync"/>.</summary>
    Task<Device?> UpdateStatusAsync(
        TenantContext tenant, string deviceId, DeviceStatus newStatus, int expectedVersion, CancellationToken cancellationToken);

    /// <summary>BIO-DEV-004-AC01: records a health-poll snapshot (<paramref name="health"/>/
    /// <paramref name="detail"/>/<paramref name="checkedAt"/>) and bumps
    /// <see cref="Device.LastSeenAt"/> to <paramref name="checkedAt"/> in the same atomic
    /// compare-and-set as <see cref="UpdateStatusAsync"/> uses - a lifecycle
    /// <see cref="DeviceStatus"/> change and a health-poll snapshot are independent axes, so
    /// this never touches <see cref="Device.Status"/>. Same not-found-not-forbidden and
    /// concurrency-conflict rules as every other write here.</summary>
    Task<Device?> RecordHeartbeatAsync(
        TenantContext tenant,
        string deviceId,
        DeviceHealthStatus health,
        string? detail,
        DateTimeOffset checkedAt,
        int expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>BIO-DEV-004-AC01/AC05: every device visible to the resolved tenant, for the
    /// polling sweep and the <c>/devices</c> list surface. Same tenant-scoping discipline as
    /// every other read here.</summary>
    Task<IReadOnlyList<Device>> ListAsync(TenantContext tenant, CancellationToken cancellationToken);
}
