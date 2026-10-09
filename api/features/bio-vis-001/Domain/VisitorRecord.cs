using Biolog.Features.Visitors.Identifiers;

namespace Biolog.Features.Visitors.Domain;

/// <summary>
/// One visitor's preregistration-through-departure record (BIO-VIS-001).
///
/// <b>AC01 - links to PERSON/profile where appropriate, without forcing permanent identity for
/// every lead.</b> <see cref="PersonId"/> is nullable - a first-time or one-off visitor ("a
/// lead") never needs a PERSON minted just to be preregistered; a repeat or already-known visitor
/// (e.g. a contractor) can be linked to their existing PERSON instead. <see cref="FullName"/> is
/// always denormalized onto this record regardless of the link, since gate staff read a name off
/// a presented ID, not a profile lookup.
///
/// <b>AC02 - host approval states are auditable.</b> <see cref="HostApprovalStatus"/> starts
/// <see cref="Domain.HostApprovalStatus.Pending"/> and moves to
/// <see cref="Domain.HostApprovalStatus.Approved"/>/<see cref="Domain.HostApprovalStatus.Denied"/>
/// exactly once (<see cref="Approve"/>/<see cref="Deny"/>, both called only by
/// <see cref="VisitorRecordService"/> after the caller's authority is verified) - every transition
/// this record can make is routed through <see cref="VisitorRecordService.AuditAsync"/>,
/// mirroring BIO-IDN-001's "all mutations are audited" discipline.
///
/// <b>AC03 - configurable fields.</b> Vehicle, NDA, safety briefing, watchlist screening and
/// temporary credential are all present on this record. Whether a given tenant REQUIRES any of
/// them before check-in is <see cref="VisitorTenantConfig"/>'s concern, enforced by
/// <see cref="VisitorRecordService.CheckInAsync"/> - not by this record refusing to be created
/// without them (a visitor is preregistered first; requirements are satisfied before arrival).
///
/// <b>AC04 - automatic expiry, computed at read time.</b> <see cref="ExpiresAt"/> is set once at
/// preregistration; nothing on this record is ever auto-mutated when it passes. See
/// <see cref="VisitorStatusEvaluator"/> - the same "refuse auto-flip for time-based status,
/// compute in effect at read time" discipline BIO-IDN-002 established, applied fresh here for an
/// identical reason (no background scheduler infrastructure exists to keep a stored derived value
/// from going stale).
///
/// <b>No canonical <c>vis_</c> identifier prefix.</b> See
/// <c>Identifiers/CanonicalId.cs</c>'s own header - <see cref="Id"/> is a plain UUIDv7 string,
/// deliberately not claiming conformance with the Kemi-owned identifier registry.
///
/// <b>Construction and mutation are internal</b> (the same BIO-IDN-001-QA-01 discipline every
/// identity-wave aggregate applies): <see cref="Id"/>/<see cref="TenantId"/> are <c>get</c>-only
/// (fixed for the object's whole life, including across a <c>with</c> expression), the
/// constructor is <c>internal</c>, and every state-transition method is <c>internal</c> too -
/// restricted to this assembly, <c>Biolog.Api</c> (DI/adapters) and
/// <c>Biolog.Features.Visitors.Tests</c> via <c>InternalsVisibleTo</c>. Legality of a transition
/// (e.g. "approve only from Pending") is checked by <see cref="VisitorRecordService"/> before
/// calling these methods AND independently re-checked by
/// <see cref="InMemoryVisitorRecordRepository.ReplaceAsync"/> at the persistence boundary - the
/// same "validated at the request boundary AND the repository boundary" discipline
/// <c>CredentialReferenceValidator</c> (BIO-DEV-001-QA-04) established.
/// </summary>
public sealed record VisitorRecord
{
    public string Id { get; }
    public string TenantId { get; }

    public string? PersonId { get; init; }
    public required string FullName { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    public required string HostPersonId { get; init; }
    public string? VehiclePlate { get; init; }

    public required bool NdaAccepted { get; init; }
    public string? NdaAcceptedAt { get; init; }

    public required bool SafetyBriefingCompleted { get; init; }
    public string? SafetyBriefingCompletedAt { get; init; }

    public required WatchlistScreeningResult WatchlistScreeningResult { get; init; }

    public required bool TemporaryCredentialRequested { get; init; }
    public required bool TemporaryCredentialIssued { get; init; }

    public required HostApprovalStatus HostApprovalStatus { get; init; }
    public string? ApprovalDecidedBy { get; init; }
    public string? ApprovalDecidedAt { get; init; }
    public string? ApprovalReason { get; init; }

    public required VisitStatus VisitStatus { get; init; }
    public string? CheckedInAt { get; init; }
    public string? CheckedOutAt { get; init; }

    public required string ExpiresAt { get; init; }
    public required string CreatedAt { get; init; }
    public required int Version { get; init; }

    internal VisitorRecord(string id, string tenantId)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        Id = id;
        TenantId = tenantId;
    }

    internal static VisitorRecord Preregister(
        string tenantId, string? personId, string fullName, string? contactEmail, string? contactPhone,
        string hostPersonId, string? vehiclePlate, bool temporaryCredentialRequested, string expiresAt, string now) =>
        new(CanonicalId.NewVisitorId(), tenantId)
        {
            PersonId = personId,
            FullName = fullName,
            ContactEmail = contactEmail,
            ContactPhone = contactPhone,
            HostPersonId = hostPersonId,
            VehiclePlate = vehiclePlate,
            NdaAccepted = false,
            SafetyBriefingCompleted = false,
            WatchlistScreeningResult = WatchlistScreeningResult.NotScreened,
            TemporaryCredentialRequested = temporaryCredentialRequested,
            TemporaryCredentialIssued = false,
            HostApprovalStatus = HostApprovalStatus.Pending,
            VisitStatus = VisitStatus.Preregistered,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            Version = 1,
        };

    internal VisitorRecord Approve(string actorId, string now) =>
        this with { HostApprovalStatus = HostApprovalStatus.Approved, ApprovalDecidedBy = actorId, ApprovalDecidedAt = now, Version = Version + 1 };

    internal VisitorRecord Deny(string actorId, string reason, string now) =>
        this with { HostApprovalStatus = HostApprovalStatus.Denied, ApprovalDecidedBy = actorId, ApprovalDecidedAt = now, ApprovalReason = reason, Version = Version + 1 };

    internal VisitorRecord CheckIn(string now) =>
        this with { VisitStatus = VisitStatus.CheckedIn, CheckedInAt = now, Version = Version + 1 };

    internal VisitorRecord CheckOut(string now) =>
        this with { VisitStatus = VisitStatus.CheckedOut, CheckedOutAt = now, Version = Version + 1 };

    internal VisitorRecord RecordNdaAcceptance(string now) =>
        NdaAccepted ? this : this with { NdaAccepted = true, NdaAcceptedAt = now, Version = Version + 1 };

    internal VisitorRecord RecordSafetyBriefing(string now) =>
        SafetyBriefingCompleted ? this : this with { SafetyBriefingCompleted = true, SafetyBriefingCompletedAt = now, Version = Version + 1 };

    internal VisitorRecord RecordWatchlistResult(WatchlistScreeningResult result) =>
        WatchlistScreeningResult == result ? this : this with { WatchlistScreeningResult = result, Version = Version + 1 };

    internal VisitorRecord IssueTemporaryCredential() =>
        TemporaryCredentialIssued ? this : this with { TemporaryCredentialIssued = true, Version = Version + 1 };
}
