namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// AC03's "evidence source is preserved" - this feature's own, local projection of one of
/// BIO-ATT-001's raw <c>AttendanceEvent</c>s, carried across <see cref="IAttendanceEventQueryGateway"/>'s
/// gateway boundary. Deliberately a flat, feature-owned shape (not a reference to BIO-ATT-001's own
/// <c>AttendanceEvent</c> type, which this project cannot see - no <c>ProjectReference</c>) - every
/// field a caller of <see cref="SessionAttendanceClassifier"/> or a reader of a roster view needs
/// to see exactly how a classification was grounded: which event, what source/mode captured it,
/// and how (or whether) the claimed identity behind it was verified. Nothing here is ever
/// discarded before it reaches a roster read - AC03's whole point.
/// </summary>
public sealed record AttendanceEvidenceRecord
{
    public required string EventId { get; init; }

    /// <summary>RFC 3339 UTC instant this event actually happened, per BIO-ATT-001's own producer clock.</summary>
    public required string OccurredAt { get; init; }

    /// <summary>BIO-ATT-001's own <c>AttendanceSource</c> wire string ("device"/"manual"/"import") - carried through verbatim, never reinterpreted.</summary>
    public required string Source { get; init; }

    /// <summary>BIO-ATT-001's own <c>AttendanceMode</c> wire string ("check_in"/"check_out"/"presence").</summary>
    public required string Mode { get; init; }

    /// <summary>BIO-ATT-001's own <c>VerificationMethod</c> wire string ("biometric"/"credential"/"manual"/"unverified").</summary>
    public required string EvidenceMethod { get; init; }

    public required bool EvidenceVerified { get; init; }

    public double? EvidenceConfidence { get; init; }
}
