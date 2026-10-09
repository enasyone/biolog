namespace Biolog.Features.AttendanceSessions.Domain;

public interface IAttendanceAdjustmentRepository
{
    /// <summary>Durable write. Throwing does not, on its own, prove the adjustment was never persisted - see <see cref="AttendanceAdjustmentService"/>'s own audit-atomicity handling.</summary>
    Task<AttendanceAdjustment> CreateAsync(AttendanceAdjustment adjustment, CancellationToken cancellationToken = default);

    /// <summary>Every adjustment ever recorded for this (tenant, session, person) - the full audit trail (AC04), oldest first.</summary>
    Task<IReadOnlyList<AttendanceAdjustment>> ListForPersonAsync(string tenantId, string sessionId, string personId, CancellationToken cancellationToken = default);

    /// <summary>Every adjustment ever recorded for this (tenant, session), across all roster members - used to build a roster-wide view without one query per person.</summary>
    Task<IReadOnlyList<AttendanceAdjustment>> ListForSessionAsync(string tenantId, string sessionId, CancellationToken cancellationToken = default);
}
