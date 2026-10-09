namespace Biolog.Features.Notifications;

/// <summary>BIO-RPT-001-AC03's delivery-audit vocabulary. <see cref="Deduplicated"/> is
/// recorded (not silently skipped) so the audit trail can tell "we never tried" apart from
/// "we tried once and stopped ourselves on purpose" - both are legitimate outcomes, but only
/// one of them means the same notification would otherwise have gone out twice.</summary>
public enum DeliveryAttemptOutcome
{
    Delivered,
    Retrying,
    DeadLettered,
    Deduplicated,
}

/// <summary>One row of the delivery audit trail (BIO-RPT-001-AC03) - one per attempt, not
/// one per rule-event pair, so a retried delivery's full history (every attempt, not just
/// the final one) is reconstructable.</summary>
public sealed record DeliveryAuditRecord(
    string TenantId,
    string EventId,
    string RuleId,
    NotificationChannel Channel,
    int AttemptNumber,
    DeliveryAttemptOutcome Outcome,
    string Detail,
    DateTimeOffset OccurredAt);

/// <summary>
/// BIO-RPT-001-AC03: "Retry/deduplication and delivery audit exist." <see cref="HasSucceededAsync"/>
/// is the dedup check - the same <c>(tenant_id, event_id)</c> key ADR-0003 Decision 4
/// already defines for a domain event's own idempotency, extended here with
/// <paramref name="ruleId"/>/<paramref name="channel"/> because one event can fan out to
/// several independent rule+channel deliveries, each needing its own dedup entry - the base
/// pair is unchanged, this only adds the two dimensions delivery (as opposed to event
/// consumption) actually needs.
/// </summary>
public interface INotificationDeliveryAuditSink
{
    Task RecordAsync(DeliveryAuditRecord record, CancellationToken cancellationToken);

    Task<bool> HasSucceededAsync(string tenantId, string eventId, string ruleId, NotificationChannel channel, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeliveryAuditRecord>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken);
}

public sealed class InMemoryNotificationDeliveryAuditSink : INotificationDeliveryAuditSink
{
    private readonly List<DeliveryAuditRecord> _records = [];
    private readonly Lock _gate = new();

    public Task RecordAsync(DeliveryAuditRecord record, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _records.Add(record);
        }

        return Task.CompletedTask;
    }

    public Task<bool> HasSucceededAsync(
        string tenantId, string eventId, string ruleId, NotificationChannel channel, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var succeeded = _records.Any(r =>
                r.TenantId == tenantId && r.EventId == eventId && r.RuleId == ruleId && r.Channel == channel
                && r.Outcome == DeliveryAttemptOutcome.Delivered);
            return Task.FromResult(succeeded);
        }
    }

    public Task<IReadOnlyList<DeliveryAuditRecord>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<DeliveryAuditRecord> result = _records.Where(r => r.TenantId == tenantId).ToList();
            return Task.FromResult(result);
        }
    }
}
