namespace Biolog.Features.Notifications;

/// <summary>Aggregate result of dispatching one event against every matching rule -
/// what the AC05 "reporting" endpoints summarize back to a human.</summary>
public sealed record DispatchSummary(int MatchedRules, int Delivered, int Deduplicated, int DeadLettered);

/// <summary>
/// Ties BIO-RPT-001-AC01 (matching), AC02 (per-channel adapters), and AC03
/// (retry/dedup/audit) together. Subscribed to <see cref="IDomainEventBus"/> by
/// <c>BioRpt001Module</c> at startup, so any future real event publisher triggers this with
/// no change here.
///
/// Never branches on <see cref="NotificationChannel"/> itself (AC02's "separated" - see
/// <see cref="INotificationChannelAdapter"/>'s own doc) - the only per-channel decision this
/// class makes is which adapter instance to call, via <see cref="_adaptersByChannel"/>.
/// </summary>
public sealed class NotificationDispatchService
{
    /// <summary>Bounded retry (AC03): a transient failure is retried up to this many total
    /// attempts before the delivery is dead-lettered - never retried forever, and a
    /// permanent failure is never retried at all (see <see cref="AttemptWithRetryAsync"/>).</summary>
    private const int MaxAttempts = 3;

    private readonly INotificationRuleRepository _rules;
    private readonly INotificationDeliveryAuditSink _auditSink;
    private readonly IReadOnlyDictionary<NotificationChannel, INotificationChannelAdapter> _adaptersByChannel;

    public NotificationDispatchService(
        INotificationRuleRepository rules, IEnumerable<INotificationChannelAdapter> adapters, INotificationDeliveryAuditSink auditSink)
    {
        _rules = rules;
        _auditSink = auditSink;
        _adaptersByChannel = adapters.ToDictionary(a => a.Channel);
    }

    public async Task<DispatchSummary> HandleAsync(DomainEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var matches = await _rules.ListMatchingAsync(envelope.TenantId, envelope.AggregateType, envelope.SchemaVersion, cancellationToken);

        int delivered = 0, deduplicated = 0, deadLettered = 0;
        foreach (var rule in matches)
        {
            // A rule for a channel with no registered adapter cannot be acted on - this
            // never happens in production (every NotificationChannel value has an adapter
            // registered by BioRpt001Module), but a rule store is not the place to assume
            // that invariant holds forever, so it is skipped rather than throwing.
            if (!_adaptersByChannel.TryGetValue(rule.Channel, out var adapter))
            {
                continue;
            }

            if (await _auditSink.HasSucceededAsync(envelope.TenantId, envelope.EventId, rule.RuleId, rule.Channel, cancellationToken))
            {
                deduplicated++;
                await _auditSink.RecordAsync(
                    new DeliveryAuditRecord(
                        envelope.TenantId, envelope.EventId, rule.RuleId, rule.Channel, AttemptNumber: 0,
                        DeliveryAttemptOutcome.Deduplicated, "already delivered for this event/rule/channel", DateTimeOffset.UtcNow),
                    cancellationToken);
                continue;
            }

            var rendered = adapter.Render(envelope, rule);
            var outcome = await AttemptWithRetryAsync(adapter, envelope, rule, rendered, cancellationToken);
            if (outcome == DeliveryAttemptOutcome.Delivered)
            {
                delivered++;
            }
            else if (outcome == DeliveryAttemptOutcome.DeadLettered)
            {
                deadLettered++;
            }
        }

        return new DispatchSummary(matches.Count, delivered, deduplicated, deadLettered);
    }

    private async Task<DeliveryAttemptOutcome> AttemptWithRetryAsync(
        INotificationChannelAdapter adapter,
        DomainEventEnvelope envelope,
        NotificationRule rule,
        RenderedNotification rendered,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var result = await adapter.SendAsync(envelope, rendered, cancellationToken);

            if (result.Outcome == ChannelDeliveryOutcome.Delivered)
            {
                await RecordAsync(DeliveryAttemptOutcome.Delivered, attempt, result.Detail);
                return DeliveryAttemptOutcome.Delivered;
            }

            if (result.Outcome == ChannelDeliveryOutcome.PermanentFailure)
            {
                await RecordAsync(DeliveryAttemptOutcome.DeadLettered, attempt, result.Detail);
                return DeliveryAttemptOutcome.DeadLettered;
            }

            // TransientFailure: retry unless this was the last allowed attempt.
            var isLastAttempt = attempt == MaxAttempts;
            await RecordAsync(isLastAttempt ? DeliveryAttemptOutcome.DeadLettered : DeliveryAttemptOutcome.Retrying, attempt, result.Detail);
            if (isLastAttempt)
            {
                return DeliveryAttemptOutcome.DeadLettered;
            }
        }

        // Unreachable (the loop above always returns), but keeps the compiler happy about
        // every code path producing a value.
        return DeliveryAttemptOutcome.DeadLettered;

        Task RecordAsync(DeliveryAttemptOutcome outcome, int attempt, string detail) => _auditSink.RecordAsync(
            new DeliveryAuditRecord(envelope.TenantId, envelope.EventId, rule.RuleId, rule.Channel, attempt, outcome, detail, DateTimeOffset.UtcNow),
            cancellationToken);
    }
}
