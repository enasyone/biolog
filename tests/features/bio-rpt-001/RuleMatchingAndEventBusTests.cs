using Biolog.Features.Notifications;
using Biolog.Features.Notifications.Adapters;
using Xunit;

namespace Biolog.Features.Notifications.Tests;

/// <summary>BIO-RPT-001-AC01: "Rules subscribe to versioned domain events."</summary>
public sealed class RuleMatchingAndEventBusTests
{
    private static NotificationDispatchService Service(INotificationRuleRepository repo) =>
        new(repo, [new EmailChannelAdapter(), new SmsChannelAdapter(), new PushChannelAdapter(), new TeamsChannelAdapter(), new WebhookChannelAdapter()],
            new InMemoryNotificationDeliveryAuditSink());

    [Fact]
    public async Task a_rule_matches_an_event_whose_schema_version_is_within_its_declared_range()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 2), TestContext.Current.CancellationToken);

        var matches = await repo.ListMatchingAsync("ten_a", AggregateType.Device, 2, TestContext.Current.CancellationToken);

        Assert.Single(matches);
    }

    [Fact]
    public async Task a_rule_does_not_match_a_schema_version_outside_its_declared_range()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);

        var matches = await repo.ListMatchingAsync("ten_a", AggregateType.Device, 2, TestContext.Current.CancellationToken);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task a_disabled_rule_never_matches()
    {
        var repo = new InMemoryNotificationRuleRepository();
        var created = await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);
        await repo.UpdateStatusAsync("ten_a", created.RuleId, new NotificationRuleStatusChangeRequest(Enabled: false, created.Version), TestContext.Current.CancellationToken);

        var matches = await repo.ListMatchingAsync("ten_a", AggregateType.Device, 1, TestContext.Current.CancellationToken);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task a_rule_for_a_different_tenant_never_matches()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);

        var matches = await repo.ListMatchingAsync("ten_b", AggregateType.Device, 1, TestContext.Current.CancellationToken);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task a_rule_for_a_different_aggregate_type_never_matches()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Person, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);

        var matches = await repo.ListMatchingAsync("ten_a", AggregateType.Device, 1, TestContext.Current.CancellationToken);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task publishing_an_unsupported_schema_version_dispatches_to_zero_rules_not_processed_blindly()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);
        var service = Service(repo);

        var summary = await service.HandleAsync(
            EnvelopeFixtures.Build(tenantId: "ten_a", schemaVersion: 2), TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.MatchedRules);
        Assert.Equal(0, summary.Delivered);
    }

    [Fact]
    public async Task the_event_bus_delivers_a_published_event_to_a_subscribed_handler()
    {
        var bus = new InMemoryDomainEventBus();
        DomainEventEnvelope? received = null;
        bus.Subscribe((envelope, ct) =>
        {
            received = envelope;
            return Task.CompletedTask;
        });

        var published = EnvelopeFixtures.Build();
        await bus.PublishAsync(published, TestContext.Current.CancellationToken);

        Assert.Equal(published, received);
    }

    [Fact]
    public async Task one_subscribers_exception_does_not_prevent_another_subscriber_from_receiving_the_event()
    {
        var bus = new InMemoryDomainEventBus();
        var secondReceived = false;
        bus.Subscribe((_, _) => throw new InvalidOperationException("first subscriber always fails"));
        bus.Subscribe((_, _) =>
        {
            secondReceived = true;
            return Task.CompletedTask;
        });

        await bus.PublishAsync(EnvelopeFixtures.Build(), TestContext.Current.CancellationToken);

        Assert.True(secondReceived);
    }

    [Fact]
    public async Task disposing_a_subscription_stops_further_delivery_to_it()
    {
        var bus = new InMemoryDomainEventBus();
        var callCount = 0;
        var subscription = bus.Subscribe((_, _) =>
        {
            callCount++;
            return Task.CompletedTask;
        });

        await bus.PublishAsync(EnvelopeFixtures.Build(), TestContext.Current.CancellationToken);
        subscription.Dispose();
        await bus.PublishAsync(EnvelopeFixtures.Build(), TestContext.Current.CancellationToken);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task publishing_through_the_bus_reaches_a_subscribed_dispatch_service_end_to_end()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);
        var service = Service(repo);
        var bus = new InMemoryDomainEventBus();
        DispatchSummary? summary = null;
        bus.Subscribe(async (envelope, ct) => summary = await service.HandleAsync(envelope, ct));

        await bus.PublishAsync(EnvelopeFixtures.Build(tenantId: "ten_a"), TestContext.Current.CancellationToken);

        Assert.NotNull(summary);
        Assert.Equal(1, summary!.MatchedRules);
        Assert.Equal(1, summary.Delivered);
    }
}
