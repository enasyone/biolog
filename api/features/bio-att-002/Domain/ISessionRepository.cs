namespace Biolog.Features.AttendanceSessions.Domain;

public interface ISessionRepository
{
    Task<Session> CreateAsync(Session session, CancellationToken cancellationToken = default);

    Task<Session?> GetAsync(string tenantId, string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set on <see cref="Session.Version"/>: <paramref name="expectedVersion"/> must match the currently stored version. Returns false on a version mismatch rather than throwing - the caller decides how to report that.</summary>
    Task<bool> TrySaveAsync(Session session, int expectedVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Session>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}
