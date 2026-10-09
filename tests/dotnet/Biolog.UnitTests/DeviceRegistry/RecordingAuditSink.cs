using Biolog.Api.Audit;

namespace Biolog.UnitTests.DeviceRegistry;

internal sealed class RecordingAuditSink : IAuditSink
{
    public List<AuditEvent> Events { get; } = new();

    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => Task.FromResult(Events.Any(e => e.EventId == eventId));
}

/// <summary>
/// Always throws on <see cref="RecordAsync"/> and reports the event as genuinely absent -
/// simulating a total, confirmed audit-store outage (as opposed to <see cref="AmbiguousThenPresentAuditSink"/>,
/// which throws but the event turns out to have actually been recorded).
/// </summary>
internal sealed class ThrowingAuditSink : IAuditSink
{
    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        => throw new InvalidOperationException("synthetic audit sink failure");

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => Task.FromResult(false);
}

/// <summary>
/// BIO-DEV-001-QA-04-follow-up / ADR-0003 Decision 7 element 4: <see cref="RecordAsync"/>
/// throws (simulating a dropped acknowledgement), but <see cref="ExistsAsync"/> confirms the
/// event was actually recorded anyway - the exact ambiguity <c>DeviceService.AuditAsync</c>
/// must resolve as a success, not a failure.
/// </summary>
internal sealed class AmbiguousThenPresentAuditSink : IAuditSink
{
    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        => throw new InvalidOperationException("synthetic dropped-acknowledgement failure");

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => Task.FromResult(true);
}

/// <summary>
/// <see cref="RecordAsync"/> throws, and the follow-up <see cref="ExistsAsync"/> verification
/// call ALSO throws - a total audit-store outage occurring mid-verification, distinct from a
/// confirmed-absent result.
/// </summary>
internal sealed class TotalOutageAuditSink : IAuditSink
{
    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        => throw new InvalidOperationException("synthetic audit sink failure");

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => throw new InvalidOperationException("synthetic audit store total outage during verification");
}

/// <summary>
/// Blocks inside <see cref="RecordAsync"/> until <see cref="Release"/> is called, and
/// exposes <see cref="Entered"/> so a test can wait until a caller is genuinely inside its
/// critical section before acting - the only deterministic way to prove real interleaving
/// (as opposed to two synchronously-completing calls happening to run one after the other)
/// without relying on timing.
/// </summary>
internal sealed class GatedAuditSink(IAuditSink inner) : IAuditSink
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;

    public void Release() => _release.TrySetResult();

    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        await _release.Task;
        await inner.RecordAsync(auditEvent, cancellationToken);
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => inner.ExistsAsync(eventId, cancellationToken);
}

/// <summary>
/// Simulates a caller's cancellation token being cancelled in the gap between a successful
/// audit write and the repository commit that follows it - the exact race
/// BIO-DEV-001-QA-03 closes by no longer honouring that token for the commit call.
/// </summary>
internal sealed class CancelingAuditSink(CancellationTokenSource cancelAfterRecording) : IAuditSink
{
    public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancelAfterRecording.Cancel();
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => Task.FromResult(true);
}
