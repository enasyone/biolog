namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>
/// BIO-BIO-009-AC02/AC04: one request to delete a single enrolled record and propagate that
/// deletion to its own modality's 1:N identification gallery cache.
///
/// <b>Deliberately carries no <c>PersonId</c>.</b> Zainab's round-1 finding (ZA-BIO009-01): a
/// caller-supplied <c>PersonId</c> independent of <see cref="RecordId"/> would let a caller
/// delete one person's record while evicting a DIFFERENT person's gallery entry. The identity
/// whose gallery entry is evicted is resolved from the record being deleted itself, by the
/// sibling story's own service, never asserted by the caller - see
/// <see cref="ITemplateDeletionGateway"/>'s own remarks.
/// </summary>
internal sealed record TemplateDeletionRequest
{
    public required string TenantId { get; init; }
    public required BiometricRecordKind Kind { get; init; }

    /// <summary>The fingerprint session id (<see cref="BiometricRecordKind.FingerprintSession"/>)
    /// or face reference id (<see cref="BiometricRecordKind.FaceReference"/>) to delete.</summary>
    public required string RecordId { get; init; }

    public required string ActorId { get; init; }
    public required string Reason { get; init; }
}
