using Biolog.Features.VisitorCredentials.Identifiers;

namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>
/// One visitor's temporary physical-access credential (BIO-VIS-002), backed by a real BIO-CRD-005
/// <c>CardIssue</c>.
///
/// <b>AC02 - expiry and scope.</b> <see cref="ExpiresAt"/> is validated at grant time to be no
/// later than the visit's own <c>VisitorRecordView.ExpiresAt</c> (a credential cannot outlive the
/// visit it was granted for) - see <see cref="VisitorCredentialService.GrantAsync"/>.
/// <see cref="Scope"/> is this story's own small, synthetic taxonomy (<see cref="VisitorCredentialScope"/>).
///
/// <b>AC03 - revocable.</b> <see cref="Revoke"/> moves <see cref="Status"/> from
/// <see cref="VisitorCredentialStatus.Granted"/> to <see cref="VisitorCredentialStatus.Revoked"/>,
/// one-way, never back - triggered either by an explicit manual revoke or by this story's own
/// orchestrating checkout (<see cref="VisitorCredentialService.CheckOutAsync"/>).
///
/// <b>Post-review remediation (BIO-VIS-002-F01).</b> A grant attempt now creates this record
/// <see cref="VisitorCredentialStatus.Pending"/> (via <see cref="CreatePending"/>) BEFORE any
/// BIO-CRD-005 call, so a durable local row exists for the whole operation, then moves it to
/// <see cref="VisitorCredentialStatus.Granted"/> (<see cref="MarkGranted"/>) once the backing card
/// is genuinely Active, or to the terminal <see cref="VisitorCredentialStatus.Failed"/>
/// (<see cref="MarkFailed"/>) if anything after that point throws - see
/// <see cref="VisitorCredentialService.GrantAsync"/>'s own header for why, and
/// <see cref="CardSequenceNumber"/> is therefore nullable now (unknown until the card is actually
/// issued).
///
/// <b>No canonical identifier prefix.</b> See <c>Identifiers/CanonicalId.cs</c>'s own header -
/// <see cref="Id"/> is a plain UUIDv7 string, the same no-canonical-prefix precedent BIO-VIS-001's
/// own <c>VisitorRecord.Id</c> already established.
///
/// <b>Construction and mutation are internal</b> (the same BIO-IDN-001-QA-01 discipline every
/// identity-wave aggregate applies): <see cref="Id"/>/<see cref="TenantId"/> are <c>get</c>-only
/// (fixed for the object's whole life, including across a <c>with</c> expression), the
/// constructor is <c>internal</c>, and <see cref="Revoke"/> is <c>internal</c> too - restricted to
/// this assembly, <c>Biolog.Api</c> (DI/adapters) and <c>Biolog.Features.VisitorCredentials.Tests</c>
/// via <c>InternalsVisibleTo</c>.
/// </summary>
public sealed record VisitorCredential
{
    public string Id { get; }
    public string TenantId { get; }

    public required string VisitorRecordId { get; init; }
    public required string PersonId { get; init; }
    public required VisitorCredentialScope Scope { get; init; }
    public required string ExpiresAt { get; init; }

    public required string CardClass { get; init; }

    /// <summary>Unknown while <see cref="Status"/> is <see cref="VisitorCredentialStatus.Pending"/> (no BIO-CRD-005 card has been issued yet); set from <see cref="MarkGranted"/> onward, and may also be set on a <see cref="VisitorCredentialStatus.Failed"/> record if a card was genuinely issued/activated before the failure (see <see cref="MarkFailed"/>).</summary>
    public int? CardSequenceNumber { get; init; }

    public required VisitorCredentialStatus Status { get; init; }
    public string? RevokedAt { get; init; }
    public string? RevokeReason { get; init; }

    /// <summary>Set only on a <see cref="VisitorCredentialStatus.Failed"/> record (BIO-VIS-002-F01's own remediation) - why the grant attempt did not complete.</summary>
    public string? FailureReason { get; init; }
    public string? FailedAt { get; init; }

    public required string GrantedBy { get; init; }
    public required string CreatedAt { get; init; }
    public required int Version { get; init; }

    internal VisitorCredential(string id, string tenantId)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        Id = id;
        TenantId = tenantId;
    }

    /// <summary>
    /// BIO-VIS-002-F01's own durable anchor - created and persisted BEFORE any BIO-CRD-005 call,
    /// under the same per-visitor lock the whole grant operation holds. No card is known yet.
    /// </summary>
    internal static VisitorCredential CreatePending(
        string tenantId, string visitorRecordId, string personId, VisitorCredentialScope scope, string expiresAt,
        string cardClass, string actorId, string now) =>
        new(CanonicalId.NewVisitorCredentialId(), tenantId)
        {
            VisitorRecordId = visitorRecordId,
            PersonId = personId,
            Scope = scope,
            ExpiresAt = expiresAt,
            CardClass = cardClass,
            CardSequenceNumber = null,
            Status = VisitorCredentialStatus.Pending,
            GrantedBy = actorId,
            CreatedAt = now,
            Version = 1,
        };

    /// <summary>Only valid from <see cref="VisitorCredentialStatus.Pending"/> - the backing card is genuinely Active.</summary>
    internal VisitorCredential MarkGranted(int cardSequenceNumber, string now) =>
        this with { Status = VisitorCredentialStatus.Granted, CardSequenceNumber = cardSequenceNumber, Version = Version + 1 };

    /// <summary>
    /// Only valid from <see cref="VisitorCredentialStatus.Pending"/> - the grant attempt did not
    /// complete. <paramref name="cardSequenceNumber"/> is non-null when a card was genuinely
    /// issued/activated before the failure (so there is always a traceable reference to it, even
    /// though the compensating deactivate may itself have failed - BIO-VIS-002-F01's disclosed
    /// residual risk, see <see cref="VisitorCredentialService.GrantAsync"/>'s own header).
    /// </summary>
    internal VisitorCredential MarkFailed(string reason, int? cardSequenceNumber, string now) =>
        this with
        {
            Status = VisitorCredentialStatus.Failed,
            CardSequenceNumber = cardSequenceNumber ?? CardSequenceNumber,
            FailureReason = reason,
            FailedAt = now,
            Version = Version + 1,
        };

    internal VisitorCredential Revoke(string reason, string now) =>
        this with { Status = VisitorCredentialStatus.Revoked, RevokedAt = now, RevokeReason = reason, Version = Version + 1 };
}
