namespace Biolog.Features.AttendanceSessions.Domain;

public interface ISessionAttendancePolicyRepository
{
    /// <summary>Never null - a (tenant, session type) pair with no explicit policy yet still has an effective one, <see cref="SessionAttendancePolicy.Default"/> (AC02).</summary>
    Task<SessionAttendancePolicy> GetAsync(string tenantId, SessionType sessionType, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set on <see cref="SessionAttendancePolicy.Version"/>: <paramref name="expectedVersion"/> must match the currently stored version (0 for a (tenant, session type) pair's very first real write). Returns false on a version mismatch rather than throwing - the caller decides how to report that.</summary>
    Task<bool> TrySaveAsync(SessionAttendancePolicy policy, int expectedVersion, CancellationToken cancellationToken = default);
}
