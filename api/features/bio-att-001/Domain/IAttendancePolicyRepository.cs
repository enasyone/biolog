namespace Biolog.Features.Attendance.Domain;

public interface IAttendancePolicyRepository
{
    /// <summary>Never null - a tenant with no explicit policy yet still has an effective one, <see cref="AttendancePolicy.Default"/> (AC04).</summary>
    Task<AttendancePolicy> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set on <see cref="AttendancePolicy.Version"/>: <paramref name="expectedVersion"/> must match the currently stored version (the default policy's own <see cref="AttendancePolicy.Version"/> of 0 for a tenant's very first real write). Returns false on a version mismatch rather than throwing - the caller decides how to report that.</summary>
    Task<bool> TrySaveAsync(AttendancePolicy policy, int expectedVersion, CancellationToken cancellationToken = default);
}
