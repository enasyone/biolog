using Xunit;

namespace Biolog.Features.TransportNotifications.Tests;

internal sealed class FakeTransportEventFeedGateway(IReadOnlyList<TransportFeedItem> items) : ITransportEventFeedGateway
{
    public Task<IReadOnlyList<TransportFeedItem>> ListRecentAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(items);
}

/// <summary>Deterministic fault injection - the same "no naturally-occurring failure in an in-memory demo channel" shape BIO-RPT-001's own channel adapters and BIO-INT-004's own audit-sink test doubles already use.</summary>
internal sealed class FakeInAppNotificationSender(Func<int, NotificationSendResult> resultForAttempt) : IInAppNotificationSender
{
    public int CallCount { get; private set; }

    public Task<NotificationSendResult> SendAsync(TransportNotificationPayload payload, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(resultForAttempt(CallCount));
    }

    public static FakeInAppNotificationSender AlwaysDelivers() => new(_ => new NotificationSendResult(NotificationSendOutcome.Delivered, "delivered (test)"));

    public static FakeInAppNotificationSender AlwaysPermanentlyFails(string detail) => new(_ => new NotificationSendResult(NotificationSendOutcome.PermanentFailure, detail));

    public static FakeInAppNotificationSender FailsTransientlyThenDelivers(int failureCount) =>
        new(attempt => attempt <= failureCount
            ? new NotificationSendResult(NotificationSendOutcome.TransientFailure, $"transient failure (test, attempt {attempt})")
            : new NotificationSendResult(NotificationSendOutcome.Delivered, "delivered (test)"));

    public static FakeInAppNotificationSender AlwaysTransientlyFails() => new(attempt => new NotificationSendResult(NotificationSendOutcome.TransientFailure, $"transient failure (test, attempt {attempt})"));
}

/// <summary>
/// BIO-TRN-003-AC01 (rule matching), AC02 (minimizes before sending - proven at the redactor
/// level in <see cref="TransportNotificationPayloadRedactorTests"/>, re-confirmed here end to end),
/// AC03 (visible retry/dead-letter) and AC04 (read-only against the feed gateway, which has no
/// write member at all).
/// </summary>
public sealed class TransportNotificationDispatchServiceTests
{
    private static readonly AuditActor TestActor = new(ActorType.User, "per_test_actor");
    private const string TenantId = "ten_a";

    private static TransportFeedItem BoardItem(string sourceEventId = "evt_board_1", string personId = "per_1") => new()
    {
        SourceEventId = sourceEventId,
        PersonId = personId,
        RouteId = "route_1",
        VehicleId = "vehicle_1",
        Location = "Synthetic Stop",
        EventType = TransportNotificationEventType.Board,
        OccurredAt = DateTimeOffset.Parse("2026-10-07T08:00:00Z"),
    };

    private static TransportNotificationDispatchService Build(
        IReadOnlyList<TransportFeedItem> feed,
        ITransportNotificationRuleRepository? ruleRepository = null,
        IInAppNotificationSender? sender = null,
        ITransportNotificationDeliveryAuditSink? auditSink = null,
        ITransportNotificationDispatchLedger? ledger = null,
        IPermissionGateway? permissionGateway = null) =>
        new(
            ruleRepository ?? new InMemoryTransportNotificationRuleRepository(),
            new FakeTransportEventFeedGateway(feed),
            ledger ?? new InMemoryTransportNotificationDispatchLedger(),
            sender ?? FakeInAppNotificationSender.AlwaysDelivers(),
            auditSink ?? new InMemoryTransportNotificationDeliveryAuditSink(),
            permissionGateway ?? new FakePermissionGateway());

    private static async Task<TransportNotificationRule> CreateRuleAsync(
        ITransportNotificationRuleRepository repository, params TransportNotificationEventType[] eventTypes)
    {
        var rule = TransportNotificationRule.Create(TenantId, "Test rule", new HashSet<TransportNotificationEventType>(eventTypes));
        await repository.AddAsync(rule, CancellationToken.None);
        return rule;
    }

    [Fact]
    public async Task A_rule_selecting_board_only_delivers_board_items_and_ignores_alight_items()
    {
        var repository = new InMemoryTransportNotificationRuleRepository();
        await CreateRuleAsync(repository, TransportNotificationEventType.Board);

        var alightItem = BoardItem("evt_alight_1") with { EventType = TransportNotificationEventType.Alight };
        var service = Build([BoardItem(), alightItem], repository);

        var deliveries = await service.DispatchAndListAsync(TenantId, TestActor, CancellationToken.None);

        var delivery = Assert.Single(deliveries);
        Assert.Equal(TransportNotificationEventType.Board, delivery.EventType);
        Assert.Equal(DeliveryAttemptOutcome.Delivered, delivery.Outcome);
    }

    [Fact]
    public async Task A_disabled_rule_never_delivers_even_when_its_event_types_match()
    {
        var repository = new InMemoryTransportNotificationRuleRepository();
        var rule = await CreateRuleAsync(repository, TransportNotificationEventType.Board);
        await repository.TryReplaceAsync(rule, rule.WithEnabled(false, rule.Version + 1), CancellationToken.None);

        var service = Build([BoardItem()], repository);

        var deliveries = await service.DispatchAndListAsync(TenantId, TestActor, CancellationToken.None);

        Assert.Empty(deliveries);
    }

    [Fact]
    public async Task A_rule_selecting_both_board_and_exception_delivers_only_once_for_the_same_underlying_event()
    {
        // AC04's own dedup-across-classification guarantee - see
        // TransportNotificationDispatchService's own header for why a rule selecting both never
        // double-notifies about the same physical happening.
        var repository = new InMemoryTransportNotificationRuleRepository();
        await CreateRuleAsync(repository, TransportNotificationEventType.Board, TransportNotificationEventType.Exception);

        var boardItem = BoardItem("evt_shared");
        var exceptionItem = boardItem with { EventType = TransportNotificationEventType.Exception };
        var service = Build([boardItem, exceptionItem], repository);

        var deliveries = await service.DispatchAndListAsync(TenantId, TestActor, CancellationToken.None);

        Assert.Single(deliveries);
    }

    [Fact]
    public async Task A_transient_failure_that_recovers_within_the_retry_budget_is_recorded_as_delivered()
    {
        var repository = new InMemoryTransportNotificationRuleRepository();
        await CreateRuleAsync(repository, TransportNotificationEventType.Board);
        var sender = FakeInAppNotificationSender.FailsTransientlyThenDelivers(failureCount: 1);

        var service = Build([BoardItem()], repository, sender: sender);
        var deliveries = await service.DispatchAndListAsync(TenantId, TestActor, CancellationToken.None);

        var delivery = Assert.Single(deliveries);
        Assert.Equal(DeliveryAttemptOutcome.Delivered, delivery.Outcome);
        Assert.Equal(2, delivery.AttemptCount);
        Assert.Equal(2, sender.CallCount);
    }

    /// <summary>AC03: "Delivery failure is visible." A permanently-failing channel is dead-lettered after the bounded retry budget - visible in the durable trail, not silently dropped.</summary>
    [Fact]
    public async Task A_send_that_keeps_failing_is_dead_lettered_after_the_retry_budget_is_exhausted()
    {
        var repository = new InMemoryTransportNotificationRuleRepository();
        await CreateRuleAsync(repository, TransportNotificationEventType.Board);
        var sender = FakeInAppNotificationSender.AlwaysTransientlyFails();

        var service = Build([BoardItem()], repository, sender: sender);
        var deliveries = await service.DispatchAndListAsync(TenantId, TestActor, CancellationToken.None);

        var delivery = Assert.Single(deliveries);
        Assert.Equal(DeliveryAttemptOutcome.DeadLettered, delivery.Outcome);
        Assert.Equal(TransportNotificationDispatchService.MaxAttempts, delivery.AttemptCount);
        Assert.Equal(TransportNotificationDispatchService.MaxAttempts, sender.CallCount);
    }

    [Fact]
    public async Task A_permanent_failure_is_dead_lettered_immediately_without_exhausting_the_retry_budget()
    {
        var repository = new InMemoryTransportNotificationRuleRepository();
        await CreateRuleAsync(repository, TransportNotificationEventType.Board);
        var sender = FakeInAppNotificationSender.AlwaysPermanentlyFails("rejected (test)");

        var service = Build([BoardItem()], repository, sender: sender);
        var deliveries = await service.DispatchAndListAsync(TenantId, TestActor, CancellationToken.None);

        var delivery = Assert.Single(deliveries);
        Assert.Equal(DeliveryAttemptOutcome.DeadLettered, delivery.Outcome);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(1, sender.CallCount);
    }

    /// <summary>
    /// Unlike BIO-INT-004's own multi-step webhook pipeline (claim, THEN an async send with a
    /// real I/O window, THEN a separate audit write - a genuine window where two independent
    /// claims could each believe they own the terminal write), this story's own
    /// <see cref="InMemoryTransportNotificationDispatchLedger.Claim"/> is a single synchronous
    /// <c>ConcurrentDictionary.GetOrAdd</c> call that resolves BEFORE any send is ever attempted -
    /// of two genuinely concurrent callers, only the winner's thread ever reaches
    /// <see cref="IInAppNotificationSender.SendAsync"/> at all, so a barrier gating the sender (the
    /// shape BIO-INT-004's own round-4 test used) would deadlock here: the losing caller returns
    /// immediately and never arrives at the barrier. The dictionary's own documented single-writer
    /// guarantee is what this story leans on instead - proven functionally by two SEQUENTIAL calls
    /// below, the real-world shape this matters for (a tenant loading the panel more than once).
    /// </summary>
    [Fact]
    public async Task Dispatching_twice_for_the_same_tenant_never_delivers_the_same_source_event_a_second_time()
    {
        var repository = new InMemoryTransportNotificationRuleRepository();
        await CreateRuleAsync(repository, TransportNotificationEventType.Board);
        var ledger = new InMemoryTransportNotificationDispatchLedger();
        var auditSink = new InMemoryTransportNotificationDeliveryAuditSink();
        var sender = FakeInAppNotificationSender.AlwaysDelivers();

        var service = Build([BoardItem()], repository, sender: sender, auditSink: auditSink, ledger: ledger);
        await service.DispatchAndListAsync(TenantId, TestActor, CancellationToken.None);
        var deliveries = await service.DispatchAndListAsync(TenantId, TestActor, CancellationToken.None);

        Assert.Single(deliveries);
        Assert.Equal(1, sender.CallCount);
    }

    [Fact]
    public async Task Dispatching_without_the_read_permission_is_rejected()
    {
        var gateway = new FakePermissionGateway();
        gateway.DeniedPermissionKeys.Add(TransportNotificationRuleService.ReadPermission);
        var service = Build([], permissionGateway: gateway);

        await Assert.ThrowsAsync<PermissionRequiredException>(() => service.DispatchAndListAsync(TenantId, TestActor, CancellationToken.None));
    }
}
