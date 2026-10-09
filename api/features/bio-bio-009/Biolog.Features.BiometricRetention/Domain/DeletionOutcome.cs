namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>
/// BIO-BIO-009-AC02/AC04. Every member is a routine, meaningful business outcome - never an
/// exception (see <see cref="BiometricRetentionException"/>'s own header).
/// </summary>
internal enum DeletionOutcome
{
    /// <summary>The record existed and was removed.</summary>
    Deleted,

    /// <summary>No record existed for the given identifier - deleting nothing is itself a valid,
    /// idempotent outcome of a delete request, never an error.</summary>
    NotFound,

    /// <summary>BIO-BIO-009-AC04/ZB-BIO009-06: the source record was genuinely deleted, but
    /// gallery eviction did not converge (it threw or returned <c>false</c> after this service's
    /// own bounded in-process retries). This is deliberately NOT <see cref="Deleted"/> - a caller
    /// must be able to tell "fully converged" apart from "deleted, but a durable cleanup
    /// obligation is still pending" (see <see cref="IPendingGalleryCleanupStore"/> and
    /// <c>BiometricDeletionService.ReconcilePendingGalleryCleanupAsync</c>), rather than this
    /// service silently reporting a successful terminal state while a biometric gallery entry
    /// remains searchable.</summary>
    DeletedPendingGalleryCleanup,
}
