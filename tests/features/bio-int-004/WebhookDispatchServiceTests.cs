using System.Text.Json;
using Biolog.Features.Webhooks;
using Xunit;

namespace Biolog.Features.Webhooks.Tests;

/// <summary>Deterministic stand-in for real HTTP delivery (BIO-RPT-001's own channel-adapter separation reasoning, applied here): queues one outcome per call, defaulting to <see cref="WebhookDeliveryOutcome.Delivered"/> once the queue is empty. Records every call's body for AC02 wire-shape assertions.</summary>
internal sealed class FakeWebhookSender : IWebhookSender
{
    private readonly Queue<WebhookDeliveryOutcome> _outcomes = new();
    public List<string> SentBodies { get; } = [];

    public void Enqueue(WebhookDeliveryOutcome outcome) => _outcomes.Enqueue(outcome);

    public Task<WebhookSendResult> SendAsync(Uri url, string body, string signatureHeader, CancellationToken cancellationToken)
    {
        SentBodies.Add(body);
        var outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : WebhookDeliveryOutcome.Delivered;
        return Task.FromResult(new WebhookSendResult(outcome, outcome.ToString()));
    }
}

/// <summary>BIO-INT-004-ZB-02 (Zainab, High - "do not use the caller cancellation token for
/// post-send completion recording"): a real audit sink could legitimately honour cancellation
/// (unlike <see cref="InMemoryWebhookDeliveryAuditSink"/>, which never checks the token at all,
/// so it cannot independently prove this fix). This fake does what a cancellation-aware
/// implementation would - throwing if handed an already-cancelled token - so a test can prove
/// the dispatch service's own terminal-outcome writes use <see cref="CancellationToken.None"/>,
/// never the caller's token, regardless of what the caller's token is doing.</summary>
internal sealed class CancellationThrowingAuditSink : IWebhookDeliveryAuditSink
{
    private readonly List<WebhookDeliveryAuditRecord> _records = [];

    public Task RecordAsync(WebhookDeliveryAuditRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records.Add(record);
        return Task.CompletedTask;
    }

    public Task<bool> TryRecordOnceAsync(WebhookDeliveryAuditRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records.Add(record);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<WebhookDeliveryAuditRecord>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WebhookDeliveryAuditRecord>>([.. _records.Where(r => r.TenantId == tenantId)]);
}

/// <summary>BIO-INT-004-ZB-03 (Zainab, High, round 2): wraps a real sink and fails the NEXT
/// <paramref name="failuresRemaining"/> calls to <see cref="RecordAsync"/> with a genuine
/// (non-cancellation) exception - simulating "the receiver accepted the event, but the audit
/// sink itself is flaky" - the exact gap round 1's <c>CancellationToken.None</c> fix did not
/// close (that fix only stopped the CALLER's cancellation from suppressing the write; it never
/// made the write infallible).</summary>
internal sealed class FlakyAuditSink(IWebhookDeliveryAuditSink inner, int failuresRemaining) : IWebhookDeliveryAuditSink
{
    private int _failuresRemaining = failuresRemaining;

    public Task RecordAsync(WebhookDeliveryAuditRecord record, CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
        {
            throw new InvalidOperationException("simulated audit sink failure - a disk-full/timeout/connection-reset, not caller cancellation");
        }

        return inner.RecordAsync(record, cancellationToken);
    }

    public Task<bool> TryRecordOnceAsync(WebhookDeliveryAuditRecord record, CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
        {
            throw new InvalidOperationException("simulated audit sink failure - a disk-full/timeout/connection-reset, not caller cancellation");
        }

        return inner.TryRecordOnceAsync(record, cancellationToken);
    }

    public Task<IReadOnlyList<WebhookDeliveryAuditRecord>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken) =>
        inner.ListForTenantAsync(tenantId, cancellationToken);
}

/// <summary>BIO-INT-004-ZB-07/SY-INT004-03 (Medium, round 4): forces two concurrent callers of
/// <see cref="IWebhookDeliveryAuditSink.TryRecordOnceAsync"/> to arrive at the REAL inner sink's
/// own atomic create-if-absent at as close to the same instant as managed code allows - a plain
/// sequential call (or one gated only by a shared start signal) would never actually exercise
/// whether the underlying operation is safe under real contention, only that it behaves
/// correctly when called one at a time.</summary>
internal sealed class AuditRaceBarrierSink(IWebhookDeliveryAuditSink inner) : IWebhookDeliveryAuditSink
{
    private readonly Barrier _barrier = new(2);

    public Task RecordAsync(WebhookDeliveryAuditRecord record, CancellationToken cancellationToken) => inner.RecordAsync(record, cancellationToken);

    public Task<bool> TryRecordOnceAsync(WebhookDeliveryAuditRecord record, CancellationToken cancellationToken)
    {
        _barrier.SignalAndWait(); // both concurrent callers arrive here before EITHER actually attempts the real atomic create-if-absent below
        return inner.TryRecordOnceAsync(record, cancellationToken);
    }

    public Task<IReadOnlyList<WebhookDeliveryAuditRecord>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken) =>
        inner.ListForTenantAsync(tenantId, cancellationToken);
}

/// <summary>
/// Wraps a real <see cref="IIdempotencyStore"/> and holds the WINNING caller (the one whose own
/// <see cref="IIdempotencyStore.Claim"/> call returns <see cref="IdempotencyClaim.IsNewClaim"/>
/// true) blocked immediately after its claim becomes durably visible in the inner store, until
/// the test releases it. Every losing caller's own <c>Claim</c> call (observing
/// <c>IsNewClaim: false</c> against a still-<see cref="IdempotencyRecordState.InFlight"/> record)
/// is signalled and passes straight through - this is what lets a test deterministically confirm
/// "the loser has genuinely observed the winner's still-pending claim" before the winner is ever
/// allowed to proceed and actually send. Ports BIO-CRD-005/006/007's own fix for the identical
/// "start-gate test has no real discriminating power" gap verbatim. Also doubles as the fixture
/// for the bounded-timeout test below: simply never calling <see cref="ReleaseOwner"/> reproduces
/// "the owner's attempt never completes" deterministically.
/// </summary>
internal sealed class ClaimBarrierIdempotencyStore(IIdempotencyStore inner) : IIdempotencyStore
{
    private readonly TaskCompletionSource _ownerClaimed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _loserObservedInFlight = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseOwner = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IdempotencyClaim Claim(string tenantId, string operation, string idempotencyKey, string requestFingerprint)
    {
        var claim = inner.Claim(tenantId, operation, idempotencyKey, requestFingerprint);
        if (claim.IsNewClaim)
        {
            _ownerClaimed.TrySetResult();
            _releaseOwner.Task.GetAwaiter().GetResult(); // block the real owner here, strictly before it proceeds to its own send
        }
        else if (claim.Record.State == IdempotencyRecordState.InFlight)
        {
            _loserObservedInFlight.TrySetResult();
        }

        return claim;
    }

    public void Complete(string tenantId, string operation, string idempotencyKey, object? result) => inner.Complete(tenantId, operation, idempotencyKey, result);

    public void Release(string tenantId, string operation, string idempotencyKey) => inner.Release(tenantId, operation, idempotencyKey);

    public Task WaitForOwnerClaimAsync() => _ownerClaimed.Task;
    public Task WaitForLoserToObserveInFlightAsync() => _loserObservedInFlight.Task;
    public void ReleaseOwner() => _releaseOwner.TrySetResult();
}

/// <summary>BIO-INT-004-AC02 (versioned payloads) and AC04 (auditable retry/dead-letter).</summary>
public sealed class WebhookDispatchServiceTests
{
    private static Uri Url => new("https://example-synthetic.test/webhooks/inbound");

    private static WebhookEventEnvelope Envelope(
        string tenantId = "ten_a", string eventId = "evt_fixed-for-test", AggregateType aggregateType = AggregateType.DocumentSession) => new(
        EventId: eventId,
        SchemaVersion: 1,
        TenantId: tenantId,
        AggregateType: aggregateType,
        AggregateId: "doc_fixed",
        AggregateVersion: 1,
        CorrelationId: "evt_correlation",
        CausationId: null,
        OccurredAt: DateTimeOffset.FromUnixTimeMilliseconds(1700000000000),
        ReceivedAt: DateTimeOffset.FromUnixTimeMilliseconds(1700000000001),
        Producer: "test-producer",
        PayloadClassification: EventClassification.Internal,
        Payload: new Dictionary<string, string> { ["key"] = "value" });

    private static async Task<WebhookSubscription> SubscribedAsync(
        IWebhookSubscriptionRepository repository, string tenantId = "ten_a", AggregateType eventType = AggregateType.DocumentSession)
    {
        var subscription = new WebhookSubscription(
            subscriptionId: $"whs_{Guid.NewGuid():N}", tenantId: tenantId, url: Url,
            eventTypes: new HashSet<AggregateType> { eventType }, enabled: true,
            signingSecretHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            version: 1, createdAt: DateTimeOffset.UtcNow);
        await repository.AddAsync(subscription, CancellationToken.None);
        return subscription;
    }

    [Fact]
    public async Task A_successful_delivery_is_recorded_once_with_attempt_number_one()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var auditSink = new InMemoryWebhookDeliveryAuditSink();
        var service = new WebhookDispatchService(repository, new FakeWebhookSender(), auditSink, new InMemoryIdempotencyStore());

        var summary = await service.HandleAsync(Envelope(), CancellationToken.None);

        Assert.Equal(1, summary.MatchedSubscriptions);
        Assert.Equal(1, summary.Delivered);
        var records = await auditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Single(records);
        Assert.Equal(DeliveryAttemptOutcome.Delivered, records[0].Outcome);
        Assert.Equal(1, records[0].AttemptNumber);
    }

    [Fact]
    public async Task A_permanent_failure_is_dead_lettered_after_exactly_one_attempt_never_retried()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var sender = new FakeWebhookSender();
        sender.Enqueue(WebhookDeliveryOutcome.PermanentFailure);
        var auditSink = new InMemoryWebhookDeliveryAuditSink();
        var service = new WebhookDispatchService(repository, sender, auditSink, new InMemoryIdempotencyStore());

        var summary = await service.HandleAsync(Envelope(), CancellationToken.None);

        Assert.Equal(0, summary.Delivered);
        Assert.Equal(1, summary.DeadLettered);
        var records = await auditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Single(records);
        Assert.Equal(DeliveryAttemptOutcome.DeadLettered, records[0].Outcome);
        Assert.Equal(1, records[0].AttemptNumber);
        Assert.Single(sender.SentBodies);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_up_to_the_bound_then_dead_lettered_with_every_attempt_audited()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var sender = new FakeWebhookSender();
        sender.Enqueue(WebhookDeliveryOutcome.TransientFailure);
        sender.Enqueue(WebhookDeliveryOutcome.TransientFailure);
        sender.Enqueue(WebhookDeliveryOutcome.TransientFailure);
        var auditSink = new InMemoryWebhookDeliveryAuditSink();
        var service = new WebhookDispatchService(repository, sender, auditSink, new InMemoryIdempotencyStore());

        var summary = await service.HandleAsync(Envelope(), CancellationToken.None);

        Assert.Equal(1, summary.DeadLettered);
        var records = await auditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Equal(3, records.Count); // MaxAttempts
        Assert.Equal([1, 2, 3], records.Select(r => r.AttemptNumber));
        Assert.Equal(DeliveryAttemptOutcome.Retrying, records[0].Outcome);
        Assert.Equal(DeliveryAttemptOutcome.Retrying, records[1].Outcome);
        Assert.Equal(DeliveryAttemptOutcome.DeadLettered, records[2].Outcome);
        Assert.Equal(3, sender.SentBodies.Count);
    }

    [Fact]
    public async Task A_transient_failure_that_recovers_before_the_bound_is_delivered_with_every_attempt_audited()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var sender = new FakeWebhookSender();
        sender.Enqueue(WebhookDeliveryOutcome.TransientFailure);
        sender.Enqueue(WebhookDeliveryOutcome.Delivered);
        var auditSink = new InMemoryWebhookDeliveryAuditSink();
        var service = new WebhookDispatchService(repository, sender, auditSink, new InMemoryIdempotencyStore());

        var summary = await service.HandleAsync(Envelope(), CancellationToken.None);

        Assert.Equal(1, summary.Delivered);
        Assert.Equal(0, summary.DeadLettered);
        var records = await auditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Equal(2, records.Count);
        Assert.Equal(DeliveryAttemptOutcome.Retrying, records[0].Outcome);
        Assert.Equal(DeliveryAttemptOutcome.Delivered, records[1].Outcome);
    }

    [Fact]
    public async Task The_same_event_dispatched_twice_is_deduplicated_the_second_time_not_delivered_again()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var sender = new FakeWebhookSender();
        var auditSink = new InMemoryWebhookDeliveryAuditSink();
        var service = new WebhookDispatchService(repository, sender, auditSink, new InMemoryIdempotencyStore());
        var envelope = Envelope(eventId: "evt_fixed-for-dedup-test");

        var first = await service.HandleAsync(envelope, CancellationToken.None);
        var second = await service.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(1, first.Delivered);
        Assert.Equal(0, first.Deduplicated);
        Assert.Equal(0, second.Delivered);
        Assert.Equal(1, second.Deduplicated);
        Assert.Single(sender.SentBodies); // never re-sent over the wire the second time
    }

    [Fact]
    public async Task Only_subscriptions_matching_the_events_tenant_and_aggregate_type_are_dispatched_to()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository, tenantId: "ten_a", eventType: AggregateType.DocumentSession);
        await SubscribedAsync(repository, tenantId: "ten_a", eventType: AggregateType.Device); // different event type, same tenant
        await SubscribedAsync(repository, tenantId: "ten_b", eventType: AggregateType.DocumentSession); // same event type, different tenant
        var sender = new FakeWebhookSender();
        var auditSink = new InMemoryWebhookDeliveryAuditSink();
        var service = new WebhookDispatchService(repository, sender, auditSink, new InMemoryIdempotencyStore());

        var summary = await service.HandleAsync(Envelope(tenantId: "ten_a", aggregateType: AggregateType.DocumentSession), CancellationToken.None);

        Assert.Equal(1, summary.MatchedSubscriptions);
        Assert.Equal(1, summary.Delivered);
    }

    [Fact]
    public async Task The_delivered_body_is_the_versioned_snake_case_wire_payload_not_the_internal_shape()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var sender = new FakeWebhookSender();
        var auditSink = new InMemoryWebhookDeliveryAuditSink();
        var service = new WebhookDispatchService(repository, sender, auditSink, new InMemoryIdempotencyStore());

        await service.HandleAsync(Envelope(), CancellationToken.None);

        var body = Assert.Single(sender.SentBodies);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("evt_fixed-for-test", root.GetProperty("event_id").GetString());
        Assert.Equal("document_session", root.GetProperty("aggregate_type").GetString());
        Assert.Equal("internal", root.GetProperty("payload_classification").GetString());
        Assert.False(root.TryGetProperty("TenantId", out _)); // never the internal PascalCase shape
    }

    // BIO-INT-004-ZB-02 (Zainab, High): the original dedup check was check-then-send-then-record
    // (HasSucceededAsync -> send -> RecordAsync), with no atomic claim between the check and the
    // send - two genuinely concurrent HandleAsync calls for the SAME event could both pass the
    // check and both deliver. A test gated only by a shared start-of-task signal (e.g. a
    // TaskCompletionSource both tasks await before calling HandleAsync) would have NO real
    // discriminating power against that bug: the scheduler could run the whole winning attempt to
    // completion before the losing attempt ever reaches the claim/check at all, at which point the
    // loser just takes the ordinary "already succeeded" replay path - which the OLD defective
    // check-then-act code would also take correctly. ClaimBarrierIdempotencyStore below forces the
    // real contested sequence deterministically instead (same pattern BIO-CRD-005/006/007 port).

    [Fact]
    public async Task HandleAsync_the_loser_must_genuinely_observe_the_winners_InFlight_claim_before_either_resolves()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var sender = new FakeWebhookSender();
        var barrierStore = new ClaimBarrierIdempotencyStore(new InMemoryIdempotencyStore());
        var service = new WebhookDispatchService(repository, sender, new InMemoryWebhookDeliveryAuditSink(), barrierStore);
        var envelope = Envelope(eventId: "evt_fixed-for-barrier-race");

        var ownerTask = Task.Run(() => service.HandleAsync(envelope, CancellationToken.None));
        await barrierStore.WaitForOwnerClaimAsync(); // the owner's claim is now durably InFlight - the owner itself is blocked, has NOT yet sent

        var loserTask = Task.Run(() => service.HandleAsync(envelope, CancellationToken.None));
        await barrierStore.WaitForLoserToObserveInFlightAsync(); // the loser has now genuinely called Claim and seen the SAME still-InFlight record

        barrierStore.ReleaseOwner(); // only now may the owner proceed to send
        var results = await Task.WhenAll(ownerTask, loserTask);

        Assert.Equal(1, results[0].Delivered + results[1].Delivered); // exactly one real delivery across both calls
        Assert.Equal(1, results[0].Deduplicated + results[1].Deduplicated); // and exactly one deduplicated
        Assert.Single(sender.SentBodies); // never sent over the wire twice
    }

    /// <summary>
    /// Proves the harness shape above genuinely discriminates: a minimal two-caller race, forced
    /// via a plain <see cref="Barrier"/> to make BOTH callers complete their own
    /// <see cref="IIdempotencyStore.Claim"/> call (both have legitimately observed the SAME
    /// pre-decision state) before EITHER acts on its decision - mirroring BIO-CRD-005's own
    /// placement exactly. The real, fixed decision (<see cref="IdempotencyClaim.IsNewClaim"/>)
    /// converges to exactly one execution under this forced interleaving; a deliberately
    /// reinstated copy of the ORIGINAL defective decision this story shipped with (an audit-sink
    /// "has this already succeeded" read, which by construction cannot be true yet for either
    /// caller at the barrier - nobody has completed anything) does not - both callers execute.
    /// </summary>
    [Fact]
    public async Task Mutant_discrimination_the_historical_check_then_act_pattern_lets_both_concurrent_claimants_execute()
    {
        static async Task<int> RaceWithDecision(Func<IdempotencyClaim, bool> shouldSend)
        {
            var store = new InMemoryIdempotencyStore();
            var barrier = new Barrier(2);
            var sendCount = 0;

            int Attempt()
            {
                var claim = store.Claim("ten_a", "whs_mutant", "evt_mutant-race", "evt_mutant-race");
                barrier.SignalAndWait(); // both callers have now completed their OWN Claim/observation before either decides
                if (!shouldSend(claim))
                {
                    return -1;
                }

                var mine = Interlocked.Increment(ref sendCount);
                store.Complete("ten_a", "whs_mutant", "evt_mutant-race", mine);
                return mine;
            }

            await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));
            return sendCount;
        }

        // The real, fixed decision: only the GetOrAdd winner (IsNewClaim) ever sends.
        var realSends = await RaceWithDecision(claim => claim.IsNewClaim);
        Assert.Equal(1, realSends);

        // The historical bug this story originally shipped: an audit-sink "has this succeeded
        // yet" read in place of an atomic claim. Neither caller's record can be Completed yet at
        // the barrier (by construction - nobody calls Complete before passing the barrier), so
        // BOTH decide "not yet succeeded, I must send" and both send.
        var mutantSends = await RaceWithDecision(claim => claim.Record.State != IdempotencyRecordState.Completed);
        Assert.Equal(2, mutantSends); // the mutant violates the exact invariant the real decision satisfies
    }

    /// <summary>
    /// The bounded-timeout path: a second caller whose claim is already owned by someone else's
    /// in-flight attempt gives up and is recorded dead-lettered once <c>idempotencyWaitTimeout</c>
    /// elapses, rather than waiting forever. Reuses <see cref="ClaimBarrierIdempotencyStore"/> -
    /// simply never calling <see cref="ClaimBarrierIdempotencyStore.ReleaseOwner"/> deterministically
    /// reproduces "the owner's attempt never completes."
    /// </summary>
    [Fact]
    public async Task HandleAsync_a_second_caller_is_dead_lettered_if_the_owners_claim_never_completes_in_time()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var sender = new FakeWebhookSender();
        var barrierStore = new ClaimBarrierIdempotencyStore(new InMemoryIdempotencyStore());
        var auditSink = new InMemoryWebhookDeliveryAuditSink();
        var service = new WebhookDispatchService(
            repository, sender, auditSink, barrierStore,
            idempotencyWaitTimeout: TimeSpan.FromMilliseconds(150), idempotencyWaitPollInterval: TimeSpan.FromMilliseconds(15));
        var envelope = Envelope(eventId: "evt_fixed-for-timeout-test");

        var ownerTask = Task.Run(() => service.HandleAsync(envelope, CancellationToken.None));
        await barrierStore.WaitForOwnerClaimAsync(); // the owner is now genuinely blocked mid-flight and never released in this test

        var loserSummary = await service.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(1, loserSummary.DeadLettered);
        Assert.Equal(0, loserSummary.Delivered);

        barrierStore.ReleaseOwner(); // let the owner finish cleanly - no orphaned background task
        var ownerSummary = await ownerTask;
        Assert.Equal(1, ownerSummary.Delivered);
        Assert.Single(sender.SentBodies); // the timed-out caller never sent at all
    }

    [Fact]
    public async Task A_successful_delivery_is_still_recorded_even_when_the_callers_own_token_is_already_cancelled()
    {
        // ZB-INT004-02 (Zainab): the send already happened (the receiver accepted the event) by
        // the time we go to record it - the caller's own cancellation must never cause that
        // acceptance to go unrecorded. FakeWebhookSender ignores its own cancellationToken
        // (simulating "the wire send already completed"); CancellationThrowingAuditSink proves
        // the record write itself does not use that same (cancelled) token.
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var sender = new FakeWebhookSender();
        var auditSink = new CancellationThrowingAuditSink();
        var service = new WebhookDispatchService(repository, sender, auditSink, new InMemoryIdempotencyStore());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var summary = await service.HandleAsync(Envelope(), cts.Token);

        Assert.Equal(1, summary.Delivered);
        var records = await auditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Single(records);
        Assert.Equal(DeliveryAttemptOutcome.Delivered, records[0].Outcome);
    }

    /// <summary>
    /// BIO-INT-004-ZB-03 (Zainab, High, round 2): "sender delivers -> audit throws -> the outer
    /// catch releases the claim -> the next dispatch wins a fresh claim and sends AGAIN." Proves
    /// the fix: the idempotency claim is Completed BEFORE the audit write is even attempted, so
    /// a genuine (non-cancellation) audit failure after a successful send can no longer cause a
    /// duplicate real delivery - a second dispatch of the SAME event is deduplicated, and the
    /// sender is called exactly once, even though the first call's audit write definitively
    /// failed and the first call itself threw.
    /// </summary>
    [Fact]
    public async Task A_genuine_audit_failure_after_a_successful_send_does_not_cause_a_duplicate_delivery_on_the_next_dispatch()
    {
        // Two failures: CompleteTerminalAsync's own immediate first-failure repair attempt is
        // the FIRST retry (consumes one), so a single simulated failure would self-heal within
        // the very first HandleAsync call and never reach the regression this test exists to
        // prove - two failures force the gap to survive into a second, later dispatch.
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var sender = new FakeWebhookSender();
        var realAuditSink = new InMemoryWebhookDeliveryAuditSink();
        var flakyAuditSink = new FlakyAuditSink(realAuditSink, failuresRemaining: 2);
        var idempotencyStore = new InMemoryIdempotencyStore();
        var service = new WebhookDispatchService(repository, sender, flakyAuditSink, idempotencyStore);
        var envelope = Envelope(eventId: "evt_fixed-for-audit-failure-test");

        // First dispatch: the send succeeds, but both the terminal write AND its own immediate
        // repair attempt genuinely fail (not caller cancellation) - the old bug released the
        // claim here, reopening the window for a duplicate send; the fix completes the claim
        // first, so this exception is "the audit trail is missing an entry," never "the delivery
        // itself needs to be redone." At this point the real audit sink is still empty - the
        // original ZB-INT004-05 gap.
        await Assert.ThrowsAsync<WebhookAuditUnavailableException>(() => service.HandleAsync(envelope, CancellationToken.None));
        Assert.Single(sender.SentBodies); // the real send already happened exactly once
        Assert.Empty(await realAuditSink.ListForTenantAsync("ten_a", CancellationToken.None));

        // Second dispatch of the SAME event: must be deduplicated, never a second real send
        // (ZB-INT004-03's own regression), AND must REPAIR the missing terminal record
        // (ZB-INT004-05's own regression - round 2's fix proved the first half but left the
        // trail saying "deduplicated" with nothing underneath it).
        var secondSummary = await service.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(1, secondSummary.Deduplicated);
        Assert.Equal(0, secondSummary.Delivered);
        Assert.Single(sender.SentBodies); // still exactly one real send across both dispatches

        var records = await realAuditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Contains(records, r => r.EventId == envelope.EventId && r.Outcome == DeliveryAttemptOutcome.Delivered && r.AttemptNumber == 1);
        Assert.Contains(records, r => r.EventId == envelope.EventId && r.Outcome == DeliveryAttemptOutcome.Deduplicated);
    }

    /// <summary>ZB-INT004-05/SY-INT004-01's own explicit ask: "a fail-once audit regression that
    /// ends with one send and one durable terminal record carrying the original outcome/attempt."
    /// A single transient audit-write failure self-heals within the SAME dispatch call now
    /// (CompleteTerminalAsync's own immediate repair attempt), so the common case needs no second
    /// dispatch at all to converge.</summary>
    [Fact]
    public async Task A_single_transient_audit_failure_self_heals_within_the_same_dispatch_leaving_one_durable_terminal_record()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        await SubscribedAsync(repository);
        var sender = new FakeWebhookSender();
        var realAuditSink = new InMemoryWebhookDeliveryAuditSink();
        var flakyAuditSink = new FlakyAuditSink(realAuditSink, failuresRemaining: 1);
        var service = new WebhookDispatchService(repository, sender, flakyAuditSink, new InMemoryIdempotencyStore());
        var envelope = Envelope(eventId: "evt_fixed-for-fail-once-test");

        var summary = await service.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(1, summary.Delivered);
        Assert.Single(sender.SentBodies);
        var records = await realAuditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        var record = Assert.Single(records);
        Assert.Equal(DeliveryAttemptOutcome.Delivered, record.Outcome);
        Assert.Equal(1, record.AttemptNumber);
        Assert.Equal(envelope.EventId, record.EventId);
    }

    /// <summary>
    /// BIO-INT-004-ZB-07/SY-INT004-03 (Medium, round 4, both reviewers independently found this).
    /// Round 3's own terminal-record repair was itself a check-then-write race: a genuinely
    /// concurrent pair of duplicate dispatches, BOTH finding the claim already completed with its
    /// terminal record missing, could both observe "absent" and both append their own row -
    /// corrupting AC04's one-row-per-attempt trail. Starts from a completed claim with no terminal
    /// record at all (simulating "the original write never landed and nobody has repaired it
    /// yet"), then fires two genuinely concurrent dispatches, barrier-forced to reach the real
    /// atomic create-if-absent at the same instant, and proves exactly one terminal row results.
    /// </summary>
    [Fact]
    public async Task EnsureTerminalRecordAsync_two_genuinely_concurrent_repairs_create_exactly_one_terminal_row()
    {
        var repository = new InMemoryWebhookSubscriptionRepository();
        var subscription = await SubscribedAsync(repository);
        var realAuditSink = new InMemoryWebhookDeliveryAuditSink();
        var barrierSink = new AuditRaceBarrierSink(realAuditSink);
        var idempotencyStore = new InMemoryIdempotencyStore();
        var sender = new FakeWebhookSender();
        var service = new WebhookDispatchService(repository, sender, barrierSink, idempotencyStore);
        var envelope = Envelope(eventId: "evt_fixed-for-repair-race-test");

        // Pre-complete the claim directly (bypassing a real send), simulating "the send already
        // happened but its terminal audit record never landed" - exactly the starting state
        // EnsureTerminalRecordAsync exists to converge.
        idempotencyStore.Claim("ten_a", subscription.SubscriptionId, envelope.EventId, envelope.EventId);
        idempotencyStore.Complete("ten_a", subscription.SubscriptionId, envelope.EventId, new TerminalDeliveryResult(DeliveryAttemptOutcome.Delivered, 1, "200 OK"));

        // Task.Run is required, not optional: everything under HandleAsync completes
        // synchronously (every underlying Task is already-completed), so without forcing each
        // call onto its own thread, the first call's own blocking Barrier.SignalAndWait would
        // deadlock the test thread against itself before the second call's statement is ever
        // reached - the same reason every other barrier-based test in this file uses Task.Run.
        var first = Task.Run(() => service.HandleAsync(envelope, CancellationToken.None));
        var second = Task.Run(() => service.HandleAsync(envelope, CancellationToken.None));
        await Task.WhenAll(first, second);

        Assert.Empty(sender.SentBodies); // both calls see the claim already Completed - neither ever re-sends
        var records = await realAuditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Single(records, r => r.Outcome == DeliveryAttemptOutcome.Delivered); // exactly one terminal row, not two
    }

    [Fact]
    public async Task Release_is_a_no_op_against_an_already_completed_claim_defense_in_depth()
    {
        // BIO-INT-004-ZB-03's own defense-in-depth layer, tested directly against the primitive:
        // even a wrongly-placed Release call after a genuine Complete must never resurrect the
        // claim as available for a fresh (duplicate) send.
        var store = new InMemoryIdempotencyStore();
        var claim = store.Claim("ten_a", "whs_x", "evt_x", "evt_x");
        Assert.True(claim.IsNewClaim);
        store.Complete("ten_a", "whs_x", "evt_x", "delivered");

        store.Release("ten_a", "whs_x", "evt_x");

        var afterRelease = store.Claim("ten_a", "whs_x", "evt_x", "evt_x");
        Assert.False(afterRelease.IsNewClaim); // still the same completed record, not a fresh claim
        Assert.Equal(IdempotencyRecordState.Completed, afterRelease.Record.State);
    }
}
