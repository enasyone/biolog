namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>
/// BIO-BIO-009-AC02/AC04's narrow seam onto the sibling stores this feature does not reference
/// (see this project's csproj remarks). Implemented in <c>api/Biolog.Api/Modules/
/// BioBio009Module.cs</c>, the one place in the host that references BIO-BIO-002/003/006/007's
/// projects already.
///
/// <b>Why this is a Result-shaped interface, not a throwing one.</b> Same reasoning as
/// BIO-IDN-004's own <c>IPersonLifecycleGateway</c>: the sibling stores' own exception types
/// cannot be referenced or caught here without a ProjectReference this project deliberately does
/// not have.
///
/// <b>Why the delete methods also return the deleted record's own <c>PersonId</c>.</b> Zainab's
/// round-1 finding (ZA-BIO009-01): gallery eviction must use the IDENTITY THAT OWNED THE DELETED
/// RECORD, resolved by the sibling story's own service from the record itself (BIO-BIO-002's
/// <c>FingerprintEnrolmentService.DeleteSessionAsync</c> already loads the session before
/// deleting it; BIO-BIO-003's <c>FaceEnrolmentService.DeleteReferenceAsync</c> loads the
/// reference first for this same reason) - never a value the caller merely asserts alongside an
/// unrelated record id. <c>null</c> only when nothing was deleted (there is no "deleted record's
/// own identity" to report).
///
/// <b>Why <see cref="DeleteFingerprintSessionAsync"/> alone takes <c>reason</c>/<c>actorId</c>.</b>
/// BIO-BIO-002's own <c>FingerprintEnrolmentService.DeleteSessionAsync</c> reuses that story's
/// existing shared lock/audit infrastructure and records its OWN audit event for the deletion
/// (Kemi-approved, 2026-10-06) - it needs these to build that event. The other three sibling
/// methods this gateway calls are deliberately plain, unaudited persistence wrappers (same
/// approval) - this feature's own <see cref="Service.BiometricDeletionService"/> is the one and
/// only place the full ADR-0003 Decision 7 discipline (lock, audit-before-commit, ambiguous-throw
/// verification, <see cref="CancellationToken.None"/> past the confirmed audit point) exists for
/// those three operations, recorded once as this feature's own coherent retention-deletion event
/// rather than four fragmented, inconsistent partial records.
///
/// <b>Why gallery propagation no longer changes <see cref="DeletionOutcome.Deleted"/> into a
/// different enum member on its own.</b> It still never un-deletes the source record. But since
/// ZB-BIO009-06, a propagation that does not converge resolves to
/// <see cref="DeletionOutcome.DeletedPendingGalleryCleanup"/> rather than being silently folded
/// into a plain <see cref="DeletionOutcome.Deleted"/> - see
/// <see cref="Service.BiometricDeletionService"/>'s own remarks.
///
/// <b>Why <see cref="ResolveFingerprintSessionOwnerAsync"/>/<see cref="ResolveFaceReferenceOwnerAsync"/>
/// exist (ZB-BIO009-06).</b> <see cref="Service.BiometricDeletionService"/> must record a durable
/// gallery-cleanup obligation BEFORE deleting the source record, not only after - a throw or a
/// `false` result from the gallery calls below must never lose the person id needed to retry.
/// These two methods are read-only peeks (BIO-BIO-002's existing
/// <c>FingerprintEnrolmentService.GetSessionAsync</c>; BIO-BIO-003's new, equally plain
/// <c>FaceEnrolmentService.ResolveOwnerAsync</c>), never a delete.
/// </summary>
internal interface ITemplateDeletionGateway
{
    Task<string?> ResolveFingerprintSessionOwnerAsync(string tenantId, string sessionId, CancellationToken cancellationToken);

    Task<string?> ResolveFaceReferenceOwnerAsync(string tenantId, string referenceId, CancellationToken cancellationToken);

    Task<(DeletionOutcome Outcome, string? PersonId)> DeleteFingerprintSessionAsync(
        string tenantId, string sessionId, string reason, string actorId, CancellationToken cancellationToken);

    Task<(DeletionOutcome Outcome, string? PersonId)> DeleteFaceReferenceAsync(
        string tenantId, string referenceId, CancellationToken cancellationToken);

    /// <summary>Returns <c>true</c> only when an entry genuinely existed and was removed;
    /// <c>false</c> for "nothing to remove".</summary>
    Task<bool> RemoveFromFingerprintGalleryAsync(string tenantId, string personId, CancellationToken cancellationToken);

    Task<bool> RemoveFromFaceGalleryAsync(string tenantId, string personId, CancellationToken cancellationToken);
}
