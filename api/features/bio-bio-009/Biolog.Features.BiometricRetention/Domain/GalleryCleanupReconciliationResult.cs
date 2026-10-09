namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>BIO-BIO-009-AC04: the result of one reconciliation pass over a tenant's pending
/// gallery-cleanup obligations.</summary>
internal sealed record GalleryCleanupReconciliationResult
{
    public required int Resolved { get; init; }

    public required int StillPending { get; init; }
}
