using System.Collections.Concurrent;

namespace Biolog.Features.Attendance.Domain;

public sealed class InMemoryAttendancePolicyRepository : IAttendancePolicyRepository
{
    private readonly ConcurrentDictionary<string, AttendancePolicy> _byTenantId = new();

    public Task<AttendancePolicy> GetAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byTenantId.TryGetValue(tenantId, out var found) ? found : AttendancePolicy.Default(tenantId));

    public Task<bool> TrySaveAsync(AttendancePolicy policy, int expectedVersion, CancellationToken cancellationToken = default)
    {
        var current = _byTenantId.TryGetValue(policy.TenantId, out var existing) ? existing : AttendancePolicy.Default(policy.TenantId);
        if (current.Version != expectedVersion)
        {
            return Task.FromResult(false);
        }

        _byTenantId[policy.TenantId] = policy;
        return Task.FromResult(true);
    }
}
