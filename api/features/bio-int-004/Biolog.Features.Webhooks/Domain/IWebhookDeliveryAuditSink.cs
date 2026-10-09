using System.Collections.Concurrent;

namespace Biolog.Features.Webhooks;

/// <summary>BIO-INT-004-AC04's delivery-audit vocabulary - mirrors BIO-RPT-001's own
/// <c>DeliveryAttemptOutcome</c> exactly (same shape, independently duplicated: this project
/// cannot reference that one). <see cref="Deduplicated"/> is recorded, not silently skipped,
/// so the trail can tell "never attempted" apart from "deliberately stopped itself" - both
/// are legitimate audit outcomes, only one of them means the same delivery would otherwise
/// have gone out twice.</summary>
public enum DeliveryAttemptOutcome
{
    Delivered,
    Retrying,
    DeadLettered,
    Deduplicated,
}

/// <summary>One row per delivery ATTEMPT, not one row per event-subscription pair, so a
/// retried delivery's full history is reconstructable (AC04's own "auditable").</summary>
public sealed record WebhookDeliveryAuditRecord(
    string TenantId,
    string EventId,
    string SubscriptionId,
    int AttemptNumber,
    DeliveryAttemptOutcome Outcome,
    string Detail,
    DateTimeOffset OccurredAt);

/// <summary>
/// AC04: "Retry/dead-letter behavior is auditable." The per-attempt audit TRAIL only -
/// BIO-INT-004-ZB-02's fix (see <see cref="WebhookDispatchService"/>'s own header) moved the
/// dedup DECISION to <see cref="IIdempotencyStore"/>'s atomic claim, since a plain "does a
/// record already say this succeeded" read-then-act check (this interface's own former
/// <c>HasSucceededAsync</c>) left a window for two genuinely concurrent deliveries to both pass
/// it. This sink no longer needs to answer "has this already succeeded" - only to keep the
/// historical record of what happened.
///
/// <b>BIO-INT-004-ZB-07/SY-INT004-03 (Medium, round 4).</b> <see cref="TryRecordOnceAsync"/>
/// exists because round 3's own terminal-record repair (<c>WebhookDispatchService</c>'s
/// <c>RepairTerminalRecordAsync</c>) was itself a check-then-write race:
/// <see cref="ListForTenantAsync"/> then <see cref="RecordAsync"/>, with nothing atomic between
/// them - the exact same shape as the ORIGINAL send-side dedup race this story's round 1 already
/// fixed with <see cref="IIdempotencyStore.Claim"/>'s atomic ownership signal, now reappearing
/// one layer up, in the repair path meant to FIX a different race. Two genuinely concurrent
/// repair attempts for the same already-completed claim (e.g. two overlapping duplicate
/// dispatches) could both observe the terminal record absent and both append their own row,
/// corrupting AC04's own one-row-per-attempt trail. <see cref="TryRecordOnceAsync"/> is this
/// sink's own atomic create-if-absent, keyed by a record's full structural identity
/// (<c>TenantId</c>/<c>EventId</c>/<c>SubscriptionId</c>/<c>AttemptNumber</c>/<c>Outcome</c>) -
/// the identical <c>ConcurrentDictionary.GetOrAdd</c>-plus-<c>ReferenceEquals</c> ownership
/// pattern <see cref="IIdempotencyStore.Claim"/> already established, applied to the audit trail
/// itself instead of the delivery decision.
/// </summary>
public interface IWebhookDeliveryAuditSink
{
    Task RecordAsync(WebhookDeliveryAuditRecord record, CancellationToken cancellationToken);

    /// <summary>Atomically records <paramref name="record"/> only if no record with the same
    /// structural key already exists. Returns <c>true</c> only for the caller whose own record
    /// actually won the race to be created - every other concurrent caller for the SAME key sees
    /// <c>false</c>, but either way a record for that key durably exists once this returns
    /// without throwing.</summary>
    Task<bool> TryRecordOnceAsync(WebhookDeliveryAuditRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookDeliveryAuditRecord>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken);
}

public sealed class InMemoryWebhookDeliveryAuditSink : IWebhookDeliveryAuditSink
{
    private readonly List<WebhookDeliveryAuditRecord> _records = [];
    private readonly Lock _gate = new();

    /// <summary>Keyed by the SAME structural identity <see cref="TryRecordOnceAsync"/>'s own
    /// atomicity is scoped to - deliberately a separate index from <see cref="_records"/> (which
    /// stays a plain list so <see cref="ListForTenantAsync"/>'s ordering/shape is unaffected),
    /// not a replacement for it.</summary>
    private readonly ConcurrentDictionary<(string TenantId, string EventId, string SubscriptionId, int AttemptNumber, DeliveryAttemptOutcome Outcome), WebhookDeliveryAuditRecord> _terminalByKey = new();

    public Task RecordAsync(WebhookDeliveryAuditRecord record, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _records.Add(record);
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryRecordOnceAsync(WebhookDeliveryAuditRecord record, CancellationToken cancellationToken)
    {
        var key = (record.TenantId, record.EventId, record.SubscriptionId, record.AttemptNumber, record.Outcome);

        // Same reference-identity ownership test as InMemoryIdempotencyStore.Claim: only the
        // thread whose own factory-created object actually wins GetOrAdd's race ever sees
        // ReferenceEquals(stored, created) == true, regardless of how many threads call this
        // concurrently for the same key.
        WebhookDeliveryAuditRecord? created = null;
        var stored = _terminalByKey.GetOrAdd(key, _ =>
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

    public Task<IReadOnlyList<WebhookDeliveryAuditRecord>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<WebhookDeliveryAuditRecord> result = _records.Where(r => r.TenantId == tenantId).ToList();
            return Task.FromResult(result);
        }
    }
}
