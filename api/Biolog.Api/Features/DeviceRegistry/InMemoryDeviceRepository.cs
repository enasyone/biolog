using System.Collections.Concurrent;
using Biolog.Api.Identifiers;
using Biolog.Api.Tenancy;
using Biolog.Features.DeviceAdapters;

namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant
/// database ADR-0002 Decision 4.1 describes - real per-tenant routing (a
/// <c>TenantDataBinding</c> lookup selecting an actual separate database) is a later
/// story's concern, the same scope boundary BIO-FND-008 already drew around tenant
/// resolution itself. What this repository DOES implement, on top of that simplification,
/// is Decision 4.2's belt-and-braces rule: every read and write asserts the record's own
/// <see cref="Device.TenantId"/> equals the calling context's, so a bug that let one
/// tenant's device leak into a query meant for another - the exact failure a single shared
/// dictionary makes easy to introduce - is caught here rather than assumed away by
/// "each tenant has its own table."
/// </summary>
public sealed class InMemoryDeviceRepository : IDeviceRepository, ICrossTenantDeviceLookup
{
    private readonly ConcurrentDictionary<string, Device> _devices = new();

    /// <summary>See <see cref="ICrossTenantDeviceLookup"/> - explicit implementation so this
    /// escape hatch never appears on this class's ordinary, tenant-scoped public surface.</summary>
    Task<Device?> ICrossTenantDeviceLookup.GetByIdAsync(string deviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_devices.TryGetValue(deviceId, out var device) ? device : null);
    }

    public Task<Device> CreateAsync(TenantContext tenant, DeviceRegistrationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // BIO-DEV-001-QA-04: re-validated here even though DeviceService.RegisterAsync
        // already checked it, so a caller reaching this repository directly - exactly what
        // the acceptance probe that found this gap does - cannot slip plaintext past by
        // skipping the service layer.
        CredentialReferenceValidator.Validate(request.CredentialReference);
        var now = DateTimeOffset.UtcNow;
        var device = new Device
        {
            Id = CanonicalId.NewDeviceId(),
            TenantId = tenant.TenantId,
            Serial = request.Serial,
            Vendor = request.Vendor,
            Model = request.Model,
            Firmware = request.Firmware,
            Sdk = request.Sdk,
            Site = request.Site,
            Network = request.Network,
            Status = DeviceStatus.PendingActivation,
            LastSeenAt = null,
            CredentialReference = request.CredentialReference,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _devices[device.Id] = device;
        return Task.FromResult(device);
    }

    public Task<Device?> GetAsync(TenantContext tenant, string deviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_devices.TryGetValue(deviceId, out var device) || device.TenantId != tenant.TenantId)
        {
            return Task.FromResult<Device?>(null);
        }

        return Task.FromResult<Device?>(device);
    }

    public Task<Device?> UpdateStatusAsync(
        TenantContext tenant, string deviceId, DeviceStatus newStatus, int expectedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_devices.TryGetValue(deviceId, out var existing) || existing.TenantId != tenant.TenantId)
        {
            return Task.FromResult<Device?>(null);
        }

        if (existing.Version != expectedVersion)
        {
            throw new DeviceConcurrencyConflictException(existing.Version);
        }

        var updated = existing with
        {
            Status = newStatus,
            Version = existing.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Compare-and-set against the value actually still in the dictionary, not just the
        // version number checked above: a concurrent writer could have replaced `existing`
        // between the check and this write. TryUpdate re-validates against the live entry.
        if (!_devices.TryUpdate(deviceId, updated, existing))
        {
            throw new DeviceConcurrencyConflictException(_devices[deviceId].Version);
        }

        return Task.FromResult<Device?>(updated);
    }

    public Task<Device?> RecordHeartbeatAsync(
        TenantContext tenant,
        string deviceId,
        DeviceHealthStatus health,
        string? detail,
        DateTimeOffset checkedAt,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_devices.TryGetValue(deviceId, out var existing) || existing.TenantId != tenant.TenantId)
        {
            return Task.FromResult<Device?>(null);
        }

        if (existing.Version != expectedVersion)
        {
            throw new DeviceConcurrencyConflictException(existing.Version);
        }

        var updated = existing with
        {
            LastSeenAt = checkedAt,
            Health = health,
            HealthDetail = detail,
            HealthCheckedAt = checkedAt,
            Version = existing.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Same live-reference compare-and-set discipline as UpdateStatusAsync above.
        if (!_devices.TryUpdate(deviceId, updated, existing))
        {
            throw new DeviceConcurrencyConflictException(_devices[deviceId].Version);
        }

        return Task.FromResult<Device?>(updated);
    }

    public Task<IReadOnlyList<Device>> ListAsync(TenantContext tenant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<Device> devices = _devices.Values.Where(d => d.TenantId == tenant.TenantId).ToArray();
        return Task.FromResult(devices);
    }
}
