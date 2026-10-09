namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// This feature's own narrow, read-only seam onto BIO-ATT-001 (never a <c>ProjectReference</c>).
/// <c>Biolog.Api</c>'s <c>BioAtt002Module</c> implements this by wrapping BIO-ATT-001's own
/// already-registered (internal, <c>InternalsVisibleTo Biolog.Api</c>)
/// <c>AttendanceEventService.ListForPersonOnDateAsync</c> directly - the identical cross-story
/// gateway pattern BIO-TRN-002 established for its own dependency on BIO-TRN-001.
///
/// Reuses BIO-ATT-001's own per-(tenant, person, calendar date) query shape exactly - a
/// <see cref="Session"/>'s own <c>ScheduledDate</c> is the date this gateway is queried with, so
/// <see cref="SessionAttendanceClassifier"/> reads against the SAME raw-fact granularity
/// BIO-ATT-001's own <c>AttendanceStatusEvaluator</c> already uses, rather than inventing a
/// second, incompatible time-window concept.
/// </summary>
public interface IAttendanceEventQueryGateway
{
    Task<IReadOnlyList<AttendanceEvidenceRecord>> ListForPersonOnDateAsync(string personId, string date, CancellationToken cancellationToken = default);
}
