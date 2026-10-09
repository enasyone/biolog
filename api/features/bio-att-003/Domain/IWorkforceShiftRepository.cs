namespace Biolog.Features.WorkforceAttendance.Domain;

public interface IWorkforceShiftRepository
{
    Task<WorkforceShift?> GetAsync(string tenantId, string shiftId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkforceShift>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<WorkforceShift> CreateAsync(WorkforceShift shift, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set on <see cref="WorkforceShift.Version"/>. Returns false on a version mismatch rather than throwing - the caller decides how to report that.</summary>
    Task<bool> TrySaveAsync(WorkforceShift shift, int expectedVersion, CancellationToken cancellationToken = default);
}
