using Biolog.Features.Notifications;
using Biolog.Features.Notifications.Adapters;
using Xunit;

namespace Biolog.Features.Notifications.Tests;

/// <summary>BIO-RPT-001-AC02 ("Channel adapters are separated") and AC04 ("Sensitive
/// information is minimized per channel").</summary>
public sealed class ChannelAdapterMinimizationTests
{
    private static readonly NotificationRule Rule = new()
    {
        RuleId = "rule-1", TenantId = "ten_a", Name = "Test rule", AggregateType = AggregateType.Device,
        Channel = NotificationChannel.Email, MinSchemaVersion = 1, MaxSchemaVersion = 1, Enabled = true,
        Version = 1, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    public static readonly TheoryData<INotificationChannelAdapter, NotificationChannel> AllAdapters = new()
    {
        { new EmailChannelAdapter(), NotificationChannel.Email },
        { new SmsChannelAdapter(), NotificationChannel.Sms },
        { new PushChannelAdapter(), NotificationChannel.Push },
        { new TeamsChannelAdapter(), NotificationChannel.Teams },
        { new WebhookChannelAdapter(), NotificationChannel.Webhook },
    };

    [Theory]
    [MemberData(nameof(AllAdapters))]
    public void AC02_each_adapter_declares_the_channel_it_belongs_to(INotificationChannelAdapter adapter, NotificationChannel expected)
    {
        Assert.Equal(expected, adapter.Channel);
    }

    [Fact]
    public void AC04_a_sensitive_classified_event_carries_no_payload_fields_on_any_channel()
    {
        var envelope = EnvelopeFixtures.Build(classification: EventClassification.Sensitive);
        INotificationChannelAdapter[] adapters = [new EmailChannelAdapter(), new SmsChannelAdapter(), new PushChannelAdapter(), new TeamsChannelAdapter(), new WebhookChannelAdapter()];

        foreach (var adapter in adapters)
        {
            var rendered = adapter.Render(envelope, Rule);
            Assert.DoesNotContain("summary", rendered.Fields.Keys);
            Assert.DoesNotContain("action", rendered.Fields.Keys);
            Assert.DoesNotContain("reference", rendered.Fields.Keys);
            Assert.DoesNotContain(envelope.Payload["summary"], rendered.Summary);
        }
    }

    [Fact]
    public void AC04_a_personal_classified_event_carries_no_payload_fields_on_any_channel()
    {
        var envelope = EnvelopeFixtures.Build(classification: EventClassification.Personal);
        INotificationChannelAdapter[] adapters = [new EmailChannelAdapter(), new SmsChannelAdapter(), new PushChannelAdapter(), new TeamsChannelAdapter(), new WebhookChannelAdapter()];

        foreach (var adapter in adapters)
        {
            var rendered = adapter.Render(envelope, Rule);
            Assert.Empty(rendered.Fields.Keys.Except(["event_id", "tenant_id", "aggregate_type"]));
        }
    }

    [Fact]
    public void AC04_sms_never_carries_the_full_summary_even_for_an_internal_classified_event()
    {
        var envelope = EnvelopeFixtures.Build(classification: EventClassification.Internal);

        var rendered = new SmsChannelAdapter().Render(envelope, Rule);

        Assert.DoesNotContain("summary", rendered.Fields.Keys);
        Assert.DoesNotContain(envelope.Payload["summary"], rendered.Summary);
    }

    [Fact]
    public void AC04_email_carries_the_full_summary_for_an_internal_classified_event()
    {
        var envelope = EnvelopeFixtures.Build(classification: EventClassification.Internal);

        var rendered = new EmailChannelAdapter().Render(envelope, Rule);

        Assert.Equal(envelope.Payload["summary"], rendered.Summary);
    }

    [Fact]
    public void AC04_webhook_never_leaks_a_key_outside_its_own_declared_allowlist_plus_metadata()
    {
        var payload = new Dictionary<string, string>
        {
            ["action"] = "status_changed",
            ["summary"] = "visible",
            ["reference"] = "ref-1",
            ["not_allowlisted_secret_looking_field"] = "should never appear",
        };
        var envelope = EnvelopeFixtures.Build(classification: EventClassification.Internal, payload: payload);

        var rendered = new WebhookChannelAdapter().Render(envelope, Rule);

        Assert.DoesNotContain("not_allowlisted_secret_looking_field", rendered.Fields.Keys);
        Assert.DoesNotContain("should never appear", rendered.Summary);
    }

    [Theory]
    [MemberData(nameof(AllAdapters))]
    public async Task AC02_AC03_each_adapter_deterministically_simulates_every_delivery_outcome(INotificationChannelAdapter adapter, NotificationChannel _)
    {
        var delivered = EnvelopeFixtures.Build(payload: new Dictionary<string, string>());
        var transient = EnvelopeFixtures.Build(payload: new Dictionary<string, string> { ["simulate"] = "transient_failure" });
        var permanent = EnvelopeFixtures.Build(payload: new Dictionary<string, string> { ["simulate"] = "permanent_failure" });
        var rendered = new RenderedNotification("s", new Dictionary<string, string>());

        Assert.Equal(ChannelDeliveryOutcome.Delivered, (await adapter.SendAsync(delivered, rendered, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(ChannelDeliveryOutcome.TransientFailure, (await adapter.SendAsync(transient, rendered, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(ChannelDeliveryOutcome.PermanentFailure, (await adapter.SendAsync(permanent, rendered, TestContext.Current.CancellationToken)).Outcome);
    }
}
