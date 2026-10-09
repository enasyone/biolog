using System.Collections.Concurrent;

namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>Process-lifetime, volatile - documentation precision, not a durability claim.</summary>
public sealed class InMemorySessionRepository : ISessionRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string SessionId), Session> _bySessionId = new();

    public Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default)
    {
        _bySessionId[(session.TenantId, session.SessionId)] = session;
        return Task.FromResult(session);
    }

    public Task<Session?> GetAsync(string tenantId, string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_bySessionId.TryGetValue((tenantId, sessionId), out var found) ? found : null);

    public Task<bool> TrySaveAsync(Session session, int expectedVersion, CancellationToken cancellationToken = default)
    {
        var key = (session.TenantId, session.SessionId);
        if (!_bySessionId.TryGetValue(key, out var current) || current.Version != expectedVersion)
        {
            return Task.FromResult(false);
        }

        _bySessionId[key] = session;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Session>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var results = _bySessionId.Values.Where(s => s.TenantId == tenantId).OrderBy(s => s.ScheduledDate, StringComparer.Ordinal).ToList();
        return Task.FromResult<IReadOnlyList<Session>>(results);
    }
}
