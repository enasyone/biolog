using System.Collections.Concurrent;

namespace Biolog.Features.WorkforceAttendance.Domain;

internal sealed class InMemoryWorkforceShiftRepository : IWorkforceShiftRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string ShiftId), WorkforceShift> _shifts = new();

    public Task<WorkforceShift?> GetAsync(string tenantId, string shiftId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_shifts.TryGetValue((tenantId, shiftId), out var shift) ? shift : null);

    public Task<IReadOnlyList<WorkforceShift>> ListAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkforceShift>>(
            [.. _shifts.Values.Where(s => s.TenantId == tenantId).OrderBy(s => s.CreatedAt, StringComparer.Ordinal)]);

    public Task<WorkforceShift> CreateAsync(WorkforceShift shift, CancellationToken cancellationToken = default)
    {
        _shifts[(shift.TenantId, shift.ShiftId)] = shift;
        return Task.FromResult(shift);
    }

    public Task<bool> TrySaveAsync(WorkforceShift shift, int expectedVersion, CancellationToken cancellationToken = default)
    {
        var key = (shift.TenantId, shift.ShiftId);
        var current = _shifts.TryGetValue(key, out var existing) ? existing.Version : 0;
        if (current != expectedVersion)
        {
            return Task.FromResult(false);
        }

        _shifts[key] = shift;
        return Task.FromResult(true);
    }
}
