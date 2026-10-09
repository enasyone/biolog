namespace Biolog.Features.WorkforceAttendance.Domain;

public interface IWorkforceAttendanceAdjustmentRepository
{
    /// <summary>Append-only - this is the only write. No update, no version field, ever (the same discipline BIO-ATT-002's own <c>IAttendanceAdjustmentRepository</c> applies to its identically-shaped override record).</summary>
    Task<WorkforceAttendanceAdjustment> CreateAsync(WorkforceAttendanceAdjustment adjustment, CancellationToken cancellationToken = default);

    /// <summary>The most recently created adjustment for this (tenant, person, date), or <c>null</c> if none exists yet.</summary>
    Task<WorkforceAttendanceAdjustment?> GetLatestAsync(string tenantId, string personId, string date, CancellationToken cancellationToken = default);
}
