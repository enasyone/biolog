using System.Collections.Concurrent;
using Biolog.Api.Observability;

namespace Biolog.Api.Audit;

/// <summary>
/// The append-only audit store for this foundation story: an in-memory, process-lifetime
/// list. A real durable, cross-restart audit store (a database table or queue with its own
/// retention policy) is a later story's concern, matching how BIO-DEV-001's device
/// repository is also in-memory rather than pulling in a real database - this story
/// establishes the shape callers emit through, not the final storage engine. Also logs
/// each event through <see cref="ILogger"/> as a visible local-dev side channel; audit
/// events carry no payload (ADR-0003 Decision 1) so nothing here needs BIO-FND-009's
/// redaction boundary, but the values are still wrapped in <see cref="SafeLogValue"/>
/// explicitly, the same as any other call site would, rather than assumed exempt.
/// </summary>
public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly ConcurrentQueue<AuditEvent> _events = new();
    private readonly ILogger<InMemoryAuditSink> _logger;

    public InMemoryAuditSink(ILogger<InMemoryAuditSink> logger)
    {
        _logger = logger;
    }

    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Enqueue(auditEvent);

        _logger.LogInformation(
            "Audit: {Action} on {TargetType} {TargetId} by {ActorType}:{ActorId} - {Outcome}",
            SafeLogValue.Of(auditEvent.Action),
            SafeLogValue.Of(auditEvent.Target.Type),
            SafeLogValue.Of(auditEvent.Target.Id),
            SafeLogValue.Of(auditEvent.Actor.Type),
            SafeLogValue.Of(auditEvent.Actor.Id),
            SafeLogValue.Of(auditEvent.Outcome));

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_events.Any(e => e.EventId == eventId));
    }

    /// <summary>Exposed for developer tests only - a real durable sink would not offer
    /// this, since "read back everything ever written" is not a production audit query.</summary>
    public IReadOnlyList<AuditEvent> Events => _events.ToArray();
}
