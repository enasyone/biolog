using System.Collections.Concurrent;
using Biolog.Features.CardLifecycle.Audit;

namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>BIO-CRD-006-AC05: "old-card presentation denies AND LOGS" - this is the log entry.</summary>
public enum PresentationOutcome
{
    Allowed,
    Denied,
}

public static class PresentationOutcomeExtensions
{
    public static string ToWireString(this PresentationOutcome outcome) => outcome switch
    {
        PresentationOutcome.Allowed => "allowed",
        PresentationOutcome.Denied => "denied",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out PresentationOutcome outcome)
    {
        switch (value)
        {
            case "allowed": outcome = PresentationOutcome.Allowed; return true;
            case "denied": outcome = PresentationOutcome.Denied; return true;
            default: outcome = default; return false;
        }
    }
}

/// <summary>
/// One card-presentation attempt, recorded either synchronously by
/// <see cref="CardLifecycleService.CheckPresentationAsync"/> (a connected reader) or, per
/// AC05, submitted later by <see cref="CardLifecycleService.IntakePresentationEventAsync"/> - a
/// reader that decided ALLOW/DENY entirely from its own locally-cached deny list while offline,
/// then syncs the fact once connectivity resumes. <see cref="OccurredAt"/> is when the reader
/// actually made the decision (may be well before <see cref="ReceivedAt"/> for an offline
/// submission); <see cref="ReceivedAt"/> is always this server's own clock.
/// </summary>
public sealed record PresentationEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required string CardClass { get; init; }
    public required int SequenceNumber { get; init; }
    public required PresentationOutcome Outcome { get; init; }
    public required string ReasonCode { get; init; }
    public string? DeviceRef { get; init; }

    /// <summary>The caller who recorded this event (ADR-0003: structured, never a bare string) - for an offline submission, this is whoever/whatever operates the reader, not necessarily a human operator.</summary>
    public required AuditActor RecordedBy { get; init; }

    /// <summary><c>"online_check"</c> or <c>"offline_sync"</c> - which entry point recorded this event.</summary>
    public required string Source { get; init; }

    public required string OccurredAt { get; init; }
    public required string ReceivedAt { get; init; }
}

public interface IPresentationEventLog
{
    Task AppendAsync(PresentationEvent presentationEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>Read-only, tenant-scoped, newest first. Exposed for tests and a future presentation-history endpoint.</summary>
    Task<IReadOnlyList<PresentationEvent>> ListAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryPresentationEventLog : IPresentationEventLog
{
    private readonly ConcurrentQueue<PresentationEvent> _events = new();

    public Task AppendAsync(PresentationEvent presentationEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(presentationEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    public Task<IReadOnlyList<PresentationEvent>> ListAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PresentationEvent>>(
            [.. _events
                .Where(e => e.TenantId == tenantId && e.PersonId == personId && e.CardClass == cardClass && e.SequenceNumber == sequenceNumber)
                .OrderByDescending(e => e.ReceivedAt, StringComparer.Ordinal)]);
}
