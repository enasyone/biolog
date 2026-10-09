namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>
/// BIO-BIO-009-AC04/ZB-BIO009-06: the durable recovery path for a gallery eviction that did not
/// converge. <c>Service.BiometricDeletionService</c> records an entry here BEFORE deleting the
/// source record and only removes it once gallery eviction genuinely succeeds -
/// <c>ReconcilePendingGalleryCleanupAsync</c> retries every entry still present for a tenant.
/// Keyed by (tenant, kind, person) - a second pending record for the same person/modality from a
/// different source record is the same cleanup obligation, so <see cref="RecordAsync"/> upserts
/// rather than accumulating duplicates.
/// </summary>
internal interface IPendingGalleryCleanupStore
{
    Task RecordAsync(PendingGalleryCleanup entry, CancellationToken cancellationToken);

    Task RemoveAsync(string tenantId, BiometricRecordKind kind, string personId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingGalleryCleanup>> ListAsync(string tenantId, CancellationToken cancellationToken);
}
