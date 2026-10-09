namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>
/// BIO-BIO-009-AC04/ZB-BIO009-06: a durable record of "this person's gallery entry for this
/// modality still needs to be evicted" - recorded BEFORE the source record is deleted, so a
/// throw or a false result from gallery eviction can never lose the person id needed to retry.
/// See <see cref="IPendingGalleryCleanupStore"/> and <c>Service.BiometricDeletionService</c>'s
/// own header.
/// </summary>
internal sealed record PendingGalleryCleanup
{
    public required string TenantId { get; init; }

    public required BiometricRecordKind Kind { get; init; }

    /// <summary>Resolved from the record itself before deletion - never a caller assertion
    /// (the same ZA-BIO009-01 discipline as every other person id in this feature).</summary>
    public required string PersonId { get; init; }

    public required string RecordId { get; init; }

    public required string CreatedAtUtc { get; init; }
}
