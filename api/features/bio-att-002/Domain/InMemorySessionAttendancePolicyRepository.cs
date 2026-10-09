using System.Collections.Concurrent;

namespace Biolog.Features.AttendanceSessions.Domain;

public sealed class InMemorySessionAttendancePolicyRepository : ISessionAttendancePolicyRepository
{
    private readonly ConcurrentDictionary<(string TenantId, SessionType SessionType), SessionAttendancePolicy> _byKey = new();

    public Task<SessionAttendancePolicy> GetAsync(string tenantId, SessionType sessionType, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byKey.TryGetValue((tenantId, sessionType), out var found) ? found : SessionAttendancePolicy.Default(tenantId, sessionType));

    public Task<bool> TrySaveAsync(SessionAttendancePolicy policy, int expectedVersion, CancellationToken cancellationToken = default)
    {
        var key = (policy.TenantId, policy.SessionType);
        var current = _byKey.TryGetValue(key, out var existing) ? existing : SessionAttendancePolicy.Default(policy.TenantId, policy.SessionType);
        if (current.Version != expectedVersion)
        {
            return Task.FromResult(false);
        }

        _byKey[key] = policy;
        return Task.FromResult(true);
    }
}
