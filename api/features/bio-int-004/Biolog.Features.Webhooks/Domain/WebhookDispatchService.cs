using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biolog.Features.Webhooks;

/// <summary>Aggregate result of dispatching one event against every matching subscription -
/// what an AC05 reporting surface would summarize back to a human (mirrors BIO-RPT-001's own
/// <c>DispatchSummary</c> exactly).</summary>
public sealed record WebhookDispatchSummary(int MatchedSubscriptions, int Delivered, int Deduplicated, int DeadLettered);

/// <summary>BIO-INT-004-ZB-05/SY-INT004-01 (Medium, round 3). Everything
/// <see cref="WebhookDispatchService.EnsureTerminalRecordAsync"/> needs to idempotently
/// create/repair the EXACT terminal audit record for a completed claim, stored as
/// <see cref="IdempotencyRecord.Result"/> - the outcome alone (round 2's own
/// <see cref="DeliveryAttemptOutcome"/>) was not enough to reconstruct the record a later
/// duplicate dispatch might need to repair, since the attempt number and detail string are part
/// of the record's own identity too.</summary>
internal sealed record TerminalDeliveryResult(DeliveryAttemptOutcome Outcome, int AttemptNumber, string Detail);

/// <summary>BIO-INT-004-AC02's wire payload - the EXACT snake_case shape
/// `contracts/schemas/event-envelope.schema.json` requires, explicit `[JsonPropertyName]`
/// attributes rather than an automatic naming policy (the same lesson BIO-INT-003's own
/// `OAuthTokenRequest` already learned the hard way: this host's default camelCase binding
/// silently mismatches a real external wire contract unless named explicitly).</summary>
internal sealed record WebhookPayloadDto(
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("tenant_id")] string TenantId,
    [property: JsonPropertyName("aggregate_type")] string AggregateType,
    [property: JsonPropertyName("aggregate_id")] string AggregateId,
    [property: JsonPropertyName("aggregate_version")] int AggregateVersion,
    [property: JsonPropertyName("correlation_id")] string CorrelationId,
    [property: JsonPropertyName("causation_id")] string? CausationId,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("received_at")] DateTimeOffset ReceivedAt,
    [property: JsonPropertyName("producer")] string Producer,
    [property: JsonPropertyName("payload_classification")] string PayloadClassification,
    [property: JsonPropertyName("payload")] IReadOnlyDictionary<string, string> Payload,
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey);

/// <summary>
/// Ties BIO-INT-004-AC01 (subscription matching), AC02 (versioned wire payload), AC03
/// (signing), and AC04 (retry/dedup/audit) together - the same shape BIO-RPT-001's own
/// <c>NotificationDispatchService</c> already established for an analogous "one event fans
/// out to several independent deliveries" problem, substituting one HTTP channel for that
/// story's several SMS/email/push channels.
///
/// Subscribed to the REAL, shared <c>IDomainEventBus</c> by
/// <c>api/Biolog.Api/Modules/BioInt004Module.cs</c> at startup (via a translation from
/// <c>Biolog.Features.Notifications.DomainEventEnvelope</c> into this project's own
/// <see cref="WebhookEventEnvelope"/>) - so any future REAL event publisher triggers this
/// with no change here, the same "no change needed" property that bus's own doc comment
/// already promises.
///
/// <b>BIO-INT-004-ZB-02 (Zainab, High).</b> The original dedup check was
/// check-then-send-then-record (<c>IWebhookDeliveryAuditSink.HasSucceededAsync</c> -> send ->
/// <c>RecordAsync</c>), with no atomic claim between the check and the send - two genuinely
/// concurrent dispatches for the same (tenant, subscription, event) (e.g. a duplicate publish
/// from the event bus) could both pass the check and both deliver, and a send-succeeds-then-
/// audit-write-fails/cancels path could redeliver too. The same shape as this session's
/// BIO-INT-002/BIO-MOB-003/BIO-MOB-005/BIO-ACC-003/BIO-CRD-005/006/007 findings. Fixed by
/// porting that exact pattern: <see cref="IIdempotencyStore.Claim"/>'s atomic
/// <see cref="IdempotencyClaim.IsNewClaim"/> ownership signal gates who may proceed to send at
/// all, scoped to <c>(tenantId, subscriptionId, eventId)</c> - the natural per-delivery key
/// (one subscription id can be the <c>operation</c> dimension because each subscription's own
/// deliveries are independent of every other subscription's). <see cref="IWebhookDeliveryAuditSink"/>
/// remains the per-attempt audit TRAIL (AC04's own "auditable" requirement); it is no longer the
/// dedup DECISION.
/// </summary>
public sealed class WebhookDispatchService(
    IWebhookSubscriptionRepository subscriptions, IWebhookSender sender, IWebhookDeliveryAuditSink auditSink,
    IIdempotencyStore idempotencyStore, TimeSpan? idempotencyWaitTimeout = null, TimeSpan? idempotencyWaitPollInterval = null)
{
    /// <summary>Bounded retry (AC04): a transient failure is retried up to this many total
    /// attempts before the delivery is dead-lettered - mirrors
    /// <c>NotificationDispatchService.MaxAttempts</c> exactly.</summary>
    private const int MaxAttempts = 3;

    /// <summary>How long a claim-loser (a concurrent dispatch for the same tenant/subscription/
    /// event whose claim is already owned by another in-flight attempt) waits for that owner's
    /// result before giving up and dead-lettering. Both are injectable (defaulting to
    /// production-sized values) so a test can prove the timeout path deterministically and fast,
    /// without actually waiting multiple real seconds - same shape as every sibling
    /// idempotency-race remediation this session.</summary>
    private readonly TimeSpan _idempotencyWaitTimeout = idempotencyWaitTimeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _idempotencyWaitPollInterval = idempotencyWaitPollInterval ?? TimeSpan.FromMilliseconds(20);

    public async Task<WebhookDispatchSummary> HandleAsync(WebhookEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var matches = await subscriptions.ListMatchingAsync(envelope.TenantId, envelope.AggregateType, cancellationToken);

        int delivered = 0, deduplicated = 0, deadLettered = 0;
        foreach (var subscription in matches)
        {
            switch (await DeliverToSubscriptionAsync(subscription, envelope, cancellationToken))
            {
                case DeliveryAttemptOutcome.Delivered: delivered++; break;
                case DeliveryAttemptOutcome.Deduplicated: deduplicated++; break;
                case DeliveryAttemptOutcome.DeadLettered: deadLettered++; break;
            }
        }

        return new WebhookDispatchSummary(matches.Count, delivered, deduplicated, deadLettered);
    }

    /// <summary>One subscription's full delivery decision: wait for ownership of this exact
    /// (tenant, subscription, event) claim (bounded by <see cref="_idempotencyWaitTimeout"/>),
    /// then either replay a dedup result, attempt real delivery as the sole owner, or dead-letter
    /// if the owner never finished in time. Never returns <see cref="DeliveryAttemptOutcome.Retrying"/> -
    /// that is an internal state <see cref="AttemptWithRetryAsync"/> only ever audits, never
    /// returns as a final outcome.</summary>
    private async Task<DeliveryAttemptOutcome> DeliverToSubscriptionAsync(
        WebhookSubscription subscription, WebhookEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _idempotencyWaitTimeout;
        while (true)
        {
            var claim = idempotencyStore.Claim(envelope.TenantId, subscription.SubscriptionId, envelope.EventId, envelope.EventId);

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                // BIO-INT-004-ZB-05/SY-INT004-01 (round 3): a PRIOR call's own terminal audit
                // write may never have landed (round 2's recheck-on-throw only distinguishes
                // "already recorded" from "genuinely missing" - it does not retry writing it).
                // Repair that gap here, before adding this dispatch's own Deduplicated
                // observation, so AC04's audit trail eventually converges to a real terminal
                // record for every delivery decision, not "deduplicated" forever with nothing
                // underneath it.
                var recovered = (TerminalDeliveryResult)claim.Record.Result!;
                await EnsureTerminalRecordAsync(subscription, envelope, recovered.Outcome, recovered.AttemptNumber, recovered.Detail);
                await RecordAsync(subscription, envelope, DeliveryAttemptOutcome.Deduplicated, attempt: 0, "already delivered for this event/subscription", CancellationToken.None);
                return DeliveryAttemptOutcome.Deduplicated;
            }

            if (claim.IsNewClaim)
            {
                break; // this call is the sole owner - proceed to actually attempt delivery below.
            }

            if (DateTime.UtcNow >= deadline)
            {
                await RecordAsync(subscription, envelope, DeliveryAttemptOutcome.DeadLettered, attempt: 0, "timed out waiting for a concurrent in-flight delivery attempt to this subscription", CancellationToken.None);
                return DeliveryAttemptOutcome.DeadLettered;
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        var body = Render(envelope);
        try
        {
            // AttemptWithRetryAsync's own CompleteTerminalAsync calls idempotencyStore.Complete
            // BEFORE attempting the terminal audit write (ZB-INT004-03) - so by the time ANY
            // exception could propagate out of this call, either no terminal outcome was ever
            // reached (safe to Release below) or one already was and is durably Completed
            // (Release below is then a safe no-op - see InMemoryIdempotencyStore.Release's own
            // defense-in-depth check - never undoing a completion that already happened).
            return await AttemptWithRetryAsync(subscription, envelope, body, cancellationToken);
        }
        catch
        {
            idempotencyStore.Release(envelope.TenantId, subscription.SubscriptionId, envelope.EventId);
            throw;
        }
    }

    private static string Render(WebhookEventEnvelope envelope)
    {
        var dto = new WebhookPayloadDto(
            envelope.EventId, envelope.SchemaVersion, envelope.TenantId, ToWireString(envelope.AggregateType),
            envelope.AggregateId, envelope.AggregateVersion, envelope.CorrelationId, envelope.CausationId,
            envelope.OccurredAt, envelope.ReceivedAt, envelope.Producer, ToWireString(envelope.PayloadClassification),
            envelope.Payload, envelope.IdempotencyKey);
        return JsonSerializer.Serialize(dto);
    }

    /// <summary>Explicit mapping, not an automatic enum-naming policy - certainty over convenience for a wire contract (same lesson as <see cref="WebhookPayloadDto"/>'s own header).</summary>
    private static string ToWireString(AggregateType value) => value switch
    {
        AggregateType.Tenant => "tenant",
        AggregateType.Person => "person",
        AggregateType.Credential => "credential",
        AggregateType.Device => "device",
        AggregateType.DocumentSession => "document_session",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unmapped AggregateType"),
    };

    private static string ToWireString(EventClassification value) => value switch
    {
        EventClassification.Public => "public",
        EventClassification.Internal => "internal",
        EventClassification.Personal => "personal",
        EventClassification.Sensitive => "sensitive",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unmapped EventClassification"),
    };

    private async Task<DeliveryAttemptOutcome> AttemptWithRetryAsync(
        WebhookSubscription subscription, WebhookEventEnvelope envelope, string body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var signature = WebhookSigner.ComputeSignatureHeader(subscription.SigningSecretHex, body, DateTimeOffset.UtcNow);
            var result = await sender.SendAsync(subscription.Url, body, signature, cancellationToken);

            if (result.Outcome == WebhookDeliveryOutcome.Delivered)
            {
                await CompleteTerminalAsync(subscription, envelope, DeliveryAttemptOutcome.Delivered, attempt, result.Detail);
                return DeliveryAttemptOutcome.Delivered;
            }

            if (result.Outcome == WebhookDeliveryOutcome.PermanentFailure)
            {
                await CompleteTerminalAsync(subscription, envelope, DeliveryAttemptOutcome.DeadLettered, attempt, result.Detail);
                return DeliveryAttemptOutcome.DeadLettered;
            }

            // TransientFailure: retry unless this was the last allowed attempt.
            if (attempt == MaxAttempts)
            {
                await CompleteTerminalAsync(subscription, envelope, DeliveryAttemptOutcome.DeadLettered, attempt, result.Detail);
                return DeliveryAttemptOutcome.DeadLettered;
            }

            // Non-terminal: nothing irreversible has been decided yet - a cancelled retry loop
            // simply stops retrying, so the live token is fine here (unlike every terminal write
            // below, which must survive the caller's own token cancelling).
            await RecordAsync(subscription, envelope, DeliveryAttemptOutcome.Retrying, attempt, result.Detail, cancellationToken);
        }

        return DeliveryAttemptOutcome.DeadLettered;
    }

    /// <summary>
    /// BIO-INT-004-ZB-03 (Zainab, High - round 2). The real send has ALREADY happened and its
    /// outcome is FINAL by the time this is called - <see cref="IIdempotencyStore.Complete"/> is
    /// called FIRST, before the audit write is even attempted, so an audit-sink failure
    /// afterward can never cause <see cref="DeliverToSubscriptionAsync"/>'s own catch block to
    /// release this claim for an event that has genuinely already been delivered (or
    /// definitively dead-lettered). Round 1's <c>CancellationToken.None</c> fix only closed the
    /// caller-cancellation half of this - it never made the audit write itself infallible, and a
    /// released claim for an already-sent event is exactly how a later dispatch would win a
    /// fresh claim and deliver the SAME event to the SAME subscription a second time. The full
    /// <see cref="TerminalDeliveryResult"/> (not just the bare outcome) is stored on the claim so
    /// a LATER duplicate dispatch can repair this exact record if the write below never lands.
    /// </summary>
    private Task CompleteTerminalAsync(
        WebhookSubscription subscription, WebhookEventEnvelope envelope, DeliveryAttemptOutcome outcome, int attempt, string detail)
    {
        idempotencyStore.Complete(envelope.TenantId, subscription.SubscriptionId, envelope.EventId, new TerminalDeliveryResult(outcome, attempt, detail));
        return EnsureTerminalRecordAsync(subscription, envelope, outcome, attempt, detail);
    }

    /// <summary>
    /// BIO-INT-004-ZB-07/SY-INT004-03 (Medium, round 4). The SOLE mechanism for ensuring a
    /// terminal audit record exists for a given (tenant, subscription, event, attempt, outcome) -
    /// used both for the very first write right after a real send (via
    /// <see cref="CompleteTerminalAsync"/>) AND by <see cref="DeliverToSubscriptionAsync"/>'s own
    /// dedup branch to repair a record a PRIOR call never managed to land. Round 3's own version
    /// of this method was <c>ListForTenantAsync</c>-then-<c>RecordAsync</c> - a check-then-write
    /// race no different in shape from the ORIGINAL send-side dedup race this story's round 1
    /// already fixed with an atomic claim: two genuinely concurrent calls (e.g. two overlapping
    /// duplicate dispatches both finding the claim already <c>Completed</c>) could both observe
    /// the record absent and both append their own row, corrupting AC04's one-row-per-attempt
    /// trail. Fixed by giving the audit sink itself an atomic create-if-absent operation
    /// (<see cref="IWebhookDeliveryAuditSink.TryRecordOnceAsync"/>), keyed by the record's own
    /// structural identity - the identical <c>ConcurrentDictionary.GetOrAdd</c>-plus-
    /// <c>ReferenceEquals</c> ownership pattern <see cref="IIdempotencyStore.Claim"/> already
    /// established, just applied to the audit trail instead of the delivery decision. The
    /// recheck-on-throw below is the same "ExistsAsync verification on an ambiguous write throw"
    /// discipline ADR-0003 Decision 7 already applies everywhere else in this codebase.
    /// </summary>
    private async Task EnsureTerminalRecordAsync(
        WebhookSubscription subscription, WebhookEventEnvelope envelope, DeliveryAttemptOutcome outcome, int attempt, string detail)
    {
        var record = new WebhookDeliveryAuditRecord(envelope.TenantId, envelope.EventId, subscription.SubscriptionId, attempt, outcome, detail, DateTimeOffset.UtcNow);

        // Two attempts at the atomic write before falling back to a recheck - preserves round
        // 3's own "a single transient failure self-heals within the same dispatch call" property
        // (Zainab's own explicit ask for a fail-once regression), now on top of round 4's atomic
        // TryRecordOnceAsync rather than round 3's plain RecordAsync.
        for (var writeAttempt = 1; writeAttempt <= 2; writeAttempt++)
        {
            try
            {
                await auditSink.TryRecordOnceAsync(record, CancellationToken.None);
                return;
            }
            catch
            {
                // Ambiguous write - retry once, then recheck before surfacing, rather than
                // assuming a single failure means it never landed.
            }
        }

        var records = await auditSink.ListForTenantAsync(envelope.TenantId, CancellationToken.None);
        var alreadyRecorded = records.Any(r =>
            r.EventId == envelope.EventId && r.SubscriptionId == subscription.SubscriptionId && r.AttemptNumber == attempt && r.Outcome == outcome);
        if (!alreadyRecorded)
        {
            throw new WebhookAuditUnavailableException(envelope.EventId, subscription.SubscriptionId);
        }
    }

    private Task RecordAsync(
        WebhookSubscription subscription, WebhookEventEnvelope envelope, DeliveryAttemptOutcome outcome, int attempt, string detail, CancellationToken cancellationToken) =>
        auditSink.RecordAsync(
            new WebhookDeliveryAuditRecord(envelope.TenantId, envelope.EventId, subscription.SubscriptionId, attempt, outcome, detail, DateTimeOffset.UtcNow),
            cancellationToken);
}

/// <summary>Thrown by <see cref="WebhookDispatchService.EnsureTerminalRecordAsync"/> when a
/// terminal delivery outcome's audit write fails AND a recheck confirms it genuinely never
/// landed - the
/// delivery itself (or dead-letter decision) is already durably reflected in
/// <see cref="IIdempotencyStore"/> regardless of this exception, so no duplicate send can result
/// from it; this only signals that the audit TRAIL is missing an entry, for whatever caller
/// might want to know that (today, <see cref="Biolog.Features.Notifications.IDomainEventBus"/>'s
/// own per-handler isolation swallows it, same as any other handler exception).</summary>
public sealed class WebhookAuditUnavailableException(string eventId, string subscriptionId)
    : Exception($"webhook delivery audit record for event '{eventId}'/subscription '{subscriptionId}' could not be confirmed durable");
