using System.Collections.Concurrent;
using Biolog.Api.Observability;

namespace Biolog.Api.Features.DeviceCertification.Audit;

/// <summary>
/// The append-only audit store for this foundation story: an in-memory, process-lifetime
/// queue, matching <see cref="Biolog.Api.Audit.InMemoryAuditSink"/>'s own shape exactly. A
/// real durable, cross-restart audit store is a later story's concern.
/// </summary>
public sealed class InMemoryCertificationAuditSink : ICertificationAuditSink
{
    private readonly ConcurrentQueue<CertificationAuditEvent> _events = new();
    private readonly ILogger<InMemoryCertificationAuditSink> _logger;

    public InMemoryCertificationAuditSink(ILogger<InMemoryCertificationAuditSink> logger)
    {
        _logger = logger;
    }

    public Task RecordAsync(CertificationAuditEvent auditEvent, CancellationToken cancellationToken)
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
    /// this.</summary>
    public IReadOnlyList<CertificationAuditEvent> Events => _events.ToArray();
}
