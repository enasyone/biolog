using Biolog.Features.Notifications;
using Biolog.Features.Notifications.Adapters;
using Xunit;

namespace Biolog.Features.Notifications.Tests;

/// <summary>BIO-RPT-001-AC03: "Retry/deduplication and delivery audit exist."</summary>
public sealed class RetryDedupAuditTests
{
    private static NotificationDispatchService Service(
        INotificationRuleRepository repo, INotificationDeliveryAuditSink auditSink) =>
        new(repo, [new EmailChannelAdapter(), new SmsChannelAdapter(), new PushChannelAdapter(), new TeamsChannelAdapter(), new WebhookChannelAdapter()], auditSink);

    [Fact]
    public async Task a_permanent_failure_is_recorded_as_dead_lettered_after_exactly_one_attempt_never_retried()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);
        var auditSink = new InMemoryNotificationDeliveryAuditSink();
        var service = Service(repo, auditSink);
        var envelope = EnvelopeFixtures.Build(tenantId: "ten_a", payload: new Dictionary<string, string> { ["simulate"] = "permanent_failure" });

        var summary = await service.HandleAsync(envelope, TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.Delivered);
        Assert.Equal(1, summary.DeadLettered);
        var records = await auditSink.ListForTenantAsync("ten_a", TestContext.Current.CancellationToken);
        Assert.Single(records);
        Assert.Equal(DeliveryAttemptOutcome.DeadLettered, records[0].Outcome);
        Assert.Equal(1, records[0].AttemptNumber);
    }

    [Fact]
    public async Task a_transient_failure_is_retried_up_to_the_bound_then_dead_lettered_with_every_attempt_audited()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);
        var auditSink = new InMemoryNotificationDeliveryAuditSink();
        var service = Service(repo, auditSink);
        var envelope = EnvelopeFixtures.Build(tenantId: "ten_a", payload: new Dictionary<string, string> { ["simulate"] = "transient_failure" });

        var summary = await service.HandleAsync(envelope, TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.DeadLettered);
        var records = await auditSink.ListForTenantAsync("ten_a", TestContext.Current.CancellationToken);
        Assert.Equal(3, records.Count); // MaxAttempts
        Assert.Equal([1, 2, 3], records.Select(r => r.AttemptNumber));
        Assert.Equal(DeliveryAttemptOutcome.Retrying, records[0].Outcome);
        Assert.Equal(DeliveryAttemptOutcome.Retrying, records[1].Outcome);
        Assert.Equal(DeliveryAttemptOutcome.DeadLettered, records[2].Outcome);
    }

    [Fact]
    public async Task a_successful_delivery_is_recorded_once_with_attempt_number_one()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);
        var auditSink = new InMemoryNotificationDeliveryAuditSink();
        var service = Service(repo, auditSink);

        var summary = await service.HandleAsync(EnvelopeFixtures.Build(tenantId: "ten_a"), TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.Delivered);
        var records = await auditSink.ListForTenantAsync("ten_a", TestContext.Current.CancellationToken);
        Assert.Single(records);
        Assert.Equal(DeliveryAttemptOutcome.Delivered, records[0].Outcome);
        Assert.Equal(1, records[0].AttemptNumber);
    }

    [Fact]
    public async Task the_same_event_dispatched_twice_is_deduplicated_the_second_time_not_delivered_again()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);
        var auditSink = new InMemoryNotificationDeliveryAuditSink();
        var service = Service(repo, auditSink);
        var envelope = EnvelopeFixtures.Build(tenantId: "ten_a", eventId: "evt_fixed-for-dedup-test");

        var first = await service.HandleAsync(envelope, TestContext.Current.CancellationToken);
        var second = await service.HandleAsync(envelope, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.Delivered);
        Assert.Equal(0, first.Deduplicated);
        Assert.Equal(0, second.Delivered);
        Assert.Equal(1, second.Deduplicated);

        var records = await auditSink.ListForTenantAsync("ten_a", TestContext.Current.CancellationToken);
        Assert.Equal(2, records.Count);
        Assert.Equal(DeliveryAttemptOutcome.Delivered, records[0].Outcome);
        Assert.Equal(DeliveryAttemptOutcome.Deduplicated, records[1].Outcome);
    }

    [Fact]
    public async Task deduplication_is_scoped_per_rule_and_channel_not_just_per_event()
    {
        // Two rules for the SAME event/aggregate, different channels - both must still
        // deliver; dedup must not conflate "already delivered for rule A/channel Email"
        // with "already delivered for rule B/channel Sms".
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Email rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("SMS rule", AggregateType.Device, NotificationChannel.Sms, 1, 1), TestContext.Current.CancellationToken);
        var auditSink = new InMemoryNotificationDeliveryAuditSink();
        var service = Service(repo, auditSink);
        var envelope = EnvelopeFixtures.Build(tenantId: "ten_a");

        var summary = await service.HandleAsync(envelope, TestContext.Current.CancellationToken);

        Assert.Equal(2, summary.MatchedRules);
        Assert.Equal(2, summary.Delivered);
        Assert.Equal(0, summary.Deduplicated);
    }

    [Fact]
    public async Task dedup_is_tenant_scoped_a_different_tenants_identical_event_id_is_not_conflated()
    {
        var repo = new InMemoryNotificationRuleRepository();
        await repo.CreateAsync("ten_a", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);
        await repo.CreateAsync("ten_b", new NotificationRuleRegistrationRequest("Rule", AggregateType.Device, NotificationChannel.Email, 1, 1), TestContext.Current.CancellationToken);
        var auditSink = new InMemoryNotificationDeliveryAuditSink();
        var service = Service(repo, auditSink);

        var forTenantA = await service.HandleAsync(EnvelopeFixtures.Build(tenantId: "ten_a", eventId: "evt_shared-id"), TestContext.Current.CancellationToken);
        var forTenantB = await service.HandleAsync(EnvelopeFixtures.Build(tenantId: "ten_b", eventId: "evt_shared-id"), TestContext.Current.CancellationToken);

        Assert.Equal(1, forTenantA.Delivered);
        Assert.Equal(1, forTenantB.Delivered);
        Assert.Equal(0, forTenantB.Deduplicated);
    }
}
