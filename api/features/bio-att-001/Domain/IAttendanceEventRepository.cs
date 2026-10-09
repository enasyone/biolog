namespace Biolog.Features.Attendance.Domain;

public interface IAttendanceEventRepository
{
    /// <summary>Durable write. Throwing does not, on its own, prove the event was never persisted - see <see cref="AttendanceEventService"/>'s own audit-atomicity handling.</summary>
    Task<AttendanceEvent> CreateAsync(AttendanceEvent attendanceEvent, CancellationToken cancellationToken = default);

    /// <summary>AC03's own lookup: finds a prior capture by its producer-minted idempotency key within one tenant, or null if this key has never been used.</summary>
    Task<AttendanceEvent?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Raw events for one person on one calendar UTC date (AC02's own read side, distinct from any derived-status read).</summary>
    Task<IReadOnlyList<AttendanceEvent>> ListForPersonOnDateAsync(string tenantId, string personId, string date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}
