using System.Collections.Concurrent;

namespace Biolog.Features.TransportNotifications;

/// <summary>
/// AC03's own delivery vocabulary - mirrors BIO-RPT-001's/BIO-INT-004's own
/// <c>DeliveryAttemptOutcome</c> shape (same concept, independently duplicated: this project
/// cannot reference either sibling feature project), minus their own <c>Deduplicated</c> value:
/// this story's own dispatch pass runs on every read (<see cref="TransportNotificationDispatchService.DispatchAndListAsync"/>,
/// "catch-up on read" the same way bio-trn-002's own <c>GetPresenceStatusAsync</c> recomputes at
/// read time), so an already-processed source event is already durably visible once from its
/// FIRST pass - recording a fresh "deduplicated" row on every later read of the same page would
/// make the delivery trail grow without bound purely from repeated page loads, never from a new
/// underlying fact. A later dispatch claim loss is therefore a true no-op, not a recorded outcome.
/// </summary>
public enum DeliveryAttemptOutcome
{
    Delivered,
    DeadLettered,
}

/// <summary>One row per rule-match (AC03's own visible delivery record). <see cref="PersonId"/>/<see cref="RouteId"/>/<see cref="EventType"/>/<see cref="OccurredAt"/> are the SAME minimized fields <see cref="TransportNotificationPayload"/> carries (AC02) - this audit trail never stores anything the redactor already dropped.</summary>
public sealed record TransportNotificationDeliveryRecord(
    string TenantId,
    string DeliveryId,
    string RuleId,
    string SourceEventId,
    string PersonId,
    string? RouteId,
    TransportNotificationEventType EventType,
    DateTimeOffset OccurredAt,
    DeliveryAttemptOutcome Outcome,
    int AttemptCount,
    string Detail,
    DateTimeOffset RecordedAt);

/// <summary>
/// AC03: "Delivery failure is visible." The durable, read-back delivery trail -
/// <see cref="TryRecordOnceAsync"/> is this sink's own atomic create-if-absent, keyed by
/// <c>(TenantId, RuleId, SourceEventId)</c> - the identical <c>ConcurrentDictionary.GetOrAdd</c>-
/// plus-<c>ReferenceEquals</c> ownership pattern BIO-INT-004's own
/// <c>IWebhookDeliveryAuditSink.TryRecordOnceAsync</c> already established for the identical
/// "two genuinely concurrent dispatch passes must never both win" race.
/// </summary>
public interface ITransportNotificationDeliveryAuditSink
{
    Task<bool> TryRecordOnceAsync(TransportNotificationDeliveryRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransportNotificationDeliveryRecord>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryTransportNotificationDeliveryAuditSink : ITransportNotificationDeliveryAuditSink
{
    private readonly List<TransportNotificationDeliveryRecord> _records = [];
    private readonly Lock _gate = new();

    private readonly ConcurrentDictionary<(string TenantId, string RuleId, string SourceEventId), TransportNotificationDeliveryRecord> _byKey = new();

    public Task<bool> TryRecordOnceAsync(TransportNotificationDeliveryRecord record, CancellationToken cancellationToken = default)
    {
        var key = (record.TenantId, record.RuleId, record.SourceEventId);

        TransportNotificationDeliveryRecord? created = null;
        var stored = _byKey.GetOrAdd(key, _ =>
        {
            created = record;
            return record;
        });
        var isNew = ReferenceEquals(stored, created);
        if (isNew)
        {
            lock (_gate)
            {
                _records.Add(record);
            }
        }

        return Task.FromResult(isNew);
    }

    public Task<IReadOnlyList<TransportNotificationDeliveryRecord>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<TransportNotificationDeliveryRecord> result = [.. _records.Where(r => r.TenantId == tenantId).OrderByDescending(r => r.RecordedAt)];
            return Task.FromResult(result);
        }
    }
}
