namespace Biolog.Api.Features.DeviceCertification;

/// <summary>
/// Storage for certification matrix entries (BIO-DEV-002-AC01/AC03). Unlike
/// <see cref="Features.DeviceRegistry.IDeviceRepository"/>, methods take an explicit,
/// nullable <c>tenantId</c> rather than a resolved <see cref="Tenancy.TenantContext"/>:
/// <c>null</c> reads/writes the platform-wide default (no tenant context required - this
/// layer is control-plane, "As platform operations" per the user story), a real value
/// reads/writes that one tenant's own override (BIO-DEV-002-AC03). The two scopes never
/// collide: internally, a <c>null</c> tenant id and a real one produce different storage
/// keys for the identical vendor+model+firmware+sdk combination.
/// </summary>
public interface IDeviceCertificationRepository
{
    /// <summary>Throws <see cref="DeviceCertificationAlreadyExistsException"/> if an entry
    /// already exists for this exact key and scope.</summary>
    Task<DeviceCertification> CreateAsync(
        string? tenantId, DeviceCertificationRegistrationRequest request, CancellationToken cancellationToken);

    Task<DeviceCertification?> GetAsync(string? tenantId, DeviceCertificationKey key, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="DeviceCertificationConcurrencyConflictException"/> if
    /// <paramref name="expectedVersion"/> does not match the stored version. Returns
    /// <c>null</c> - not an exception - if no entry exists for this key and scope.</summary>
    Task<DeviceCertification?> UpdateAsync(
        string? tenantId,
        DeviceCertificationKey key,
        CertificationStatus newStatus,
        string testEvidenceReference,
        int expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>All entries in one scope (<c>null</c> = the platform-wide matrix,
    /// BIO-DEV-002-AC02; a real tenant id = only that tenant's own overrides,
    /// BIO-DEV-002-AC03).</summary>
    Task<IReadOnlyList<DeviceCertification>> ListAsync(string? tenantId, CancellationToken cancellationToken);
}
