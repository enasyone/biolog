namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// This feature's own narrow, read-only seam onto BIO-ATT-001's raw clock events - never a
/// <c>ProjectReference</c> to that project's own internal <c>AttendanceEvent</c> type. Returns a
/// view this feature owns (<see cref="ClockEventView"/>), mapped by the host adapter in
/// <c>Biolog.Api.Modules.BioAtt003Module</c> from BIO-ATT-001's own already-registered
/// <c>AttendanceEventService.ListForPersonOnDateAsync</c> - the same cross-story gateway shape
/// every sibling story uses (e.g. BIO-INT-002's own <c>IPersonGateway</c>).
///
/// <b>AC02/AC03 - derived, never raw-event mutation.</b> This gateway is READ-ONLY by design:
/// this feature has no method that writes back to BIO-ATT-001's own event store, because clock-
/// pair validation and overtime are pure functions of the events already captured there, the
/// same discipline BIO-ATT-001's own <c>AttendanceStatusEvaluator</c> established for itself.
/// </summary>
public interface IAttendanceEventGateway
{
    Task<IReadOnlyList<ClockEventView>> ListForPersonOnDateAsync(string personId, string date, CancellationToken cancellationToken = default);
}

/// <summary>One raw clock event, as this feature sees it - never this feature's own fact, always BIO-ATT-001's.</summary>
public sealed record ClockEventView
{
    public required string EventId { get; init; }
    public required ClockMode Mode { get; init; }
    public required string OccurredAt { get; init; }
}
