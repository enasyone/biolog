using System.Collections.Concurrent;

namespace Biolog.Features.WorkforceAttendance.Domain;

internal sealed class InMemoryWorkforceAttendanceAdjustmentRepository : IWorkforceAttendanceAdjustmentRepository
{
    private readonly ConcurrentDictionary<string, List<WorkforceAttendanceAdjustment>> _byScope = new(StringComparer.Ordinal);

    public Task<WorkforceAttendanceAdjustment> CreateAsync(WorkforceAttendanceAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        var key = ScopeKey(adjustment.TenantId, adjustment.PersonId, adjustment.Date);
        var list = _byScope.GetOrAdd(key, static _ => []);
        lock (list)
        {
            list.Add(adjustment);
        }

        return Task.FromResult(adjustment);
    }

    public Task<WorkforceAttendanceAdjustment?> GetLatestAsync(string tenantId, string personId, string date, CancellationToken cancellationToken = default)
    {
        var key = ScopeKey(tenantId, personId, date);
        if (!_byScope.TryGetValue(key, out var list))
        {
            return Task.FromResult<WorkforceAttendanceAdjustment?>(null);
        }

        lock (list)
        {
            return Task.FromResult(list.Count == 0 ? null : list[^1]);
        }
    }

    private static string ScopeKey(string tenantId, string personId, string date) => $"{tenantId}|{personId}|{date}";
}
