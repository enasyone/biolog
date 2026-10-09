using System.Collections.Concurrent;

namespace Biolog.Api.Features.DeviceCertification;

/// <summary>
/// The certification matrix's storage for this foundation story: an in-memory,
/// process-lifetime dictionary, matching how <see cref="Features.DeviceRegistry.InMemoryDeviceRepository"/>
/// is also in-memory rather than pulling in a real database. Also re-validates the key on
/// <see cref="CreateAsync"/> (BIO-DEV-002-AC01, belt-and-braces): the service layer already
/// validates before calling here, but a caller that reached this repository directly -
/// bypassing <see cref="DeviceCertificationService"/> entirely - must not be able to store
/// an invalid key either, the same two-boundary discipline BIO-DEV-001-QA-04 established
/// for <see cref="Features.DeviceRegistry.CredentialReferenceValidator"/>.
/// </summary>
public sealed class InMemoryDeviceCertificationRepository : IDeviceCertificationRepository
{
    private readonly ConcurrentDictionary<string, DeviceCertification> _entries = new();

    public Task<DeviceCertification> CreateAsync(
        string? tenantId, DeviceCertificationRegistrationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = request.Key;
        DeviceCertificationKeyValidator.Validate(key);

        var now = DateTimeOffset.UtcNow;
        var entry = new DeviceCertification
        {
            Vendor = key.Vendor,
            Model = key.Model,
            Firmware = key.Firmware,
            Sdk = key.Sdk,
            TenantId = tenantId,
            Status = request.Status,
            TestEvidenceReference = request.TestEvidenceReference,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (!_entries.TryAdd(StorageKey(tenantId, key), entry))
        {
            throw new DeviceCertificationAlreadyExistsException(key.Vendor, key.Model, key.Firmware, key.Sdk);
        }

        return Task.FromResult(entry);
    }

    public Task<DeviceCertification?> GetAsync(string? tenantId, DeviceCertificationKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.TryGetValue(StorageKey(tenantId, key), out var entry);
        return Task.FromResult(entry);
    }

    public Task<DeviceCertification?> UpdateAsync(
        string? tenantId,
        DeviceCertificationKey key,
        CertificationStatus newStatus,
        string testEvidenceReference,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageKey = StorageKey(tenantId, key);
        if (!_entries.TryGetValue(storageKey, out var current))
        {
            return Task.FromResult<DeviceCertification?>(null);
        }

        if (current.Version != expectedVersion)
        {
            throw new DeviceCertificationConcurrencyConflictException(current.Version);
        }

        var updated = current with
        {
            Status = newStatus,
            TestEvidenceReference = testEvidenceReference,
            Version = current.Version + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Same aggregate whose version this method just confirmed, reached only through
        // DeviceCertificationService's own per-key lock - this compare-and-swap is
        // defense-in-depth (BIO-DEV-001-QA-02's lesson: a repository's own independent
        // re-check must exist even when a lock already serializes callers), not a live
        // race under normal operation.
        if (!_entries.TryUpdate(storageKey, updated, current))
        {
            var latest = _entries[storageKey];
            throw new DeviceCertificationConcurrencyConflictException(latest.Version);
        }

        return Task.FromResult<DeviceCertification?>(updated);
    }

    public Task<IReadOnlyList<DeviceCertification>> ListAsync(string? tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DeviceCertification> result = _entries.Values
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.Vendor).ThenBy(e => e.Model).ThenBy(e => e.Firmware).ThenBy(e => e.Sdk)
            .ToArray();
        return Task.FromResult(result);
    }

    private static string StorageKey(string? tenantId, DeviceCertificationKey key) =>
        $"{tenantId ?? "*global*"}::{key.Composite}";
}
