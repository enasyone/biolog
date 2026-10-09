namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// AC02/AC03's derived side - the OUTPUT of <see cref="SessionAttendanceClassifier"/>, never a
/// stored aggregate and never written back onto any <see cref="Session"/> or BIO-ATT-001
/// <c>AttendanceEvent</c>. <see cref="GroundingEvidence"/> carries the exact raw evidence (AC03)
/// that produced this classification - empty when nothing was captured for this person on this
/// date, i.e. <see cref="SessionAttendanceStatusValue.Absent"/> with no grounding fact at all.
/// </summary>
public sealed record SessionMemberClassification
{
    public required string TenantId { get; init; }
    public required string SessionId { get; init; }
    public required string PersonId { get; init; }
    public required SessionAttendanceStatusValue Status { get; init; }
    public required IReadOnlyList<AttendanceEvidenceRecord> GroundingEvidence { get; init; }
    public required int PolicyVersion { get; init; }
    public required string ComputedAt { get; init; }
}
