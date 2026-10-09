using Biolog.Api.Features.DeviceCertification.Audit;

namespace Biolog.UnitTests.DeviceCertification;

internal sealed class RecordingCertificationAuditSink : ICertificationAuditSink
{
    public List<CertificationAuditEvent> Events { get; } = new();

    public Task RecordAsync(CertificationAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => Task.FromResult(Events.Any(e => e.EventId == eventId));
}

/// <summary>Always throws on <see cref="RecordAsync"/> and reports the event as genuinely
/// absent - a total, confirmed audit-store outage. Mirrors
/// <c>Biolog.UnitTests.DeviceRegistry.ThrowingAuditSink</c>.</summary>
internal sealed class ThrowingCertificationAuditSink : ICertificationAuditSink
{
    public Task RecordAsync(CertificationAuditEvent auditEvent, CancellationToken cancellationToken)
        => throw new InvalidOperationException("synthetic audit sink failure");

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => Task.FromResult(false);
}

/// <summary>ADR-0003 Decision 7 element 4: <see cref="RecordAsync"/> throws (a dropped
/// acknowledgement), but <see cref="ExistsAsync"/> confirms the event was actually
/// recorded anyway.</summary>
internal sealed class AmbiguousThenPresentCertificationAuditSink : ICertificationAuditSink
{
    public Task RecordAsync(CertificationAuditEvent auditEvent, CancellationToken cancellationToken)
        => throw new InvalidOperationException("synthetic dropped-acknowledgement failure");

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => Task.FromResult(true);
}

/// <summary><see cref="RecordAsync"/> throws, and the follow-up <see cref="ExistsAsync"/>
/// verification call ALSO throws - a total audit-store outage occurring
/// mid-verification.</summary>
internal sealed class TotalOutageCertificationAuditSink : ICertificationAuditSink
{
    public Task RecordAsync(CertificationAuditEvent auditEvent, CancellationToken cancellationToken)
        => throw new InvalidOperationException("synthetic audit sink failure");

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => throw new InvalidOperationException("synthetic audit store total outage during verification");
}

/// <summary>Blocks inside <see cref="RecordAsync"/> until <see cref="Release"/> is called,
/// exposing <see cref="Entered"/> so a test can prove genuine interleaving rather than
/// timing-dependent ordering. Mirrors
/// <c>Biolog.UnitTests.DeviceRegistry.GatedAuditSink</c>.</summary>
internal sealed class GatedCertificationAuditSink(ICertificationAuditSink inner) : ICertificationAuditSink
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;

    public void Release() => _release.TrySetResult();

    public async Task RecordAsync(CertificationAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        _entered.TrySetResult();
        await _release.Task;
        await inner.RecordAsync(auditEvent, cancellationToken);
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => inner.ExistsAsync(eventId, cancellationToken);
}

/// <summary>Simulates a caller's cancellation token being cancelled in the gap between a
/// successful audit write and the repository commit that follows it.</summary>
internal sealed class CancelingCertificationAuditSink(CancellationTokenSource cancelAfterRecording) : ICertificationAuditSink
{
    public Task RecordAsync(CertificationAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancelAfterRecording.Cancel();
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        => Task.FromResult(true);
}
