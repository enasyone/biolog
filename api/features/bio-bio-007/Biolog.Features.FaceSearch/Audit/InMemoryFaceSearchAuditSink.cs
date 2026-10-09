using System.Collections.Concurrent;

namespace Biolog.Features.FaceSearch.Audit;

/// <summary>In-process, in-memory <see cref="IFaceSearchAuditSink"/> for local development and
/// this story's own tests - explicitly NOT a real persistence layer. Internal, same seam as the
/// interface.</summary>
internal sealed class InMemoryFaceSearchAuditSink : IFaceSearchAuditSink
{
    private readonly ConcurrentDictionary<string, FaceSearchAuditEvent> _events = new(StringComparer.Ordinal);

    public Task WriteAsync(FaceSearchAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (!_events.TryAdd(auditEvent.EventId, auditEvent))
        {
            throw new InvalidOperationException($"face_search_audit_event_already_exists: '{auditEvent.EventId}'.");
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken) => Task.FromResult(_events.ContainsKey(eventId));

    public Task<IReadOnlyList<FaceSearchAuditEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        IReadOnlyList<FaceSearchAuditEvent> matches = _events.Values.Where(e => e.TenantId == tenantId).ToList();
        return Task.FromResult(matches);
    }
}
