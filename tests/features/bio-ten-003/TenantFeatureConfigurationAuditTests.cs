using Biolog.Features.TenantFeatureConfig;
using Biolog.Features.TenantFeatureConfig.Api;
using Biolog.Features.TenantFeatureConfig.Audit;
using Biolog.Features.TenantFeatureConfig.Domain;
using Biolog.Features.TenantFeatureConfig.Identifiers;
using Xunit;

namespace Biolog.Features.TenantFeatureConfig.Tests;

/// <summary>BIO-TEN-003-AC03: audit-before-commit discipline, copied from BIO-TEN-001-QA-01's
/// own proven pattern (<c>TenantAuditTests</c>) and adapted to this feature's own aggregate.</summary>
public sealed class TenantFeatureConfigurationAuditTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "admin_alice");
    private static string NewTenantId() => $"ten_{Guid.CreateVersion7():D}";

    private sealed class FailingAuditSink : ITenantFeatureConfigurationAuditSink
    {
        public Task WriteAsync(TenantFeatureConfigurationAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated audit outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class DurableButThrowsOnAcknowledgementSink : ITenantFeatureConfigurationAuditSink
    {
        private readonly InMemoryTenantFeatureConfigurationAuditSink _inner = new();

        public async Task WriteAsync(TenantFeatureConfigurationAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(auditEvent, cancellationToken); // durably lands...
            throw new InvalidOperationException("simulated ack failure"); // ...but throws anyway
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => _inner.ExistsAsync(eventId, cancellationToken);
    }

    [Fact]
    public async Task A_change_is_never_committed_when_the_audit_sink_genuinely_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryTenantFeatureConfigurationStore();
        var service = new TenantFeatureConfigurationService(store, new FailingAuditSink());
        var tenantId = NewTenantId();

        var error = await Assert.ThrowsAsync<TenantFeatureConfigurationError>(
            () => service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, VisitorEnabled = true }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Null(await store.GetAsync(tenantId, ct));
    }

    [Fact]
    public async Task An_audit_write_that_durably_lands_but_throws_on_acknowledgement_still_commits_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryTenantFeatureConfigurationStore();
        var service = new TenantFeatureConfigurationService(store, new DurableButThrowsOnAcknowledgementSink());
        var tenantId = NewTenantId();

        var updated = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, VisitorEnabled = true }, Actor, CanonicalId.NewEventId(), ct);

        Assert.True(updated.VisitorEnabled);
        Assert.Equal(1, updated.Version);
    }

    [Fact]
    public async Task Audit_event_records_only_the_fields_that_actually_changed_with_tenant_id_set()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantFeatureConfigurationAuditSink();
        var service = new TenantFeatureConfigurationService(new InMemoryTenantFeatureConfigurationStore(), sink);
        var tenantId = NewTenantId();

        await service.UpdateAsync(
            tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, VisitorEnabled = true, AttendanceEnabled = false },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Single(sink.Events);
        var evt = sink.Events[0];
        Assert.Equal(tenantId, evt.TenantId); // tenant-plane, unlike BIO-TEN-001's always-null TenantId
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
        Assert.True(evt.Detail.ContainsKey("visitor_enabled"));
        Assert.False(evt.Detail.ContainsKey("attendance_enabled")); // false -> false is not a change
    }

    [Fact]
    public async Task Two_updates_to_different_fields_produce_two_separate_audit_events()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantFeatureConfigurationAuditSink();
        var service = new TenantFeatureConfigurationService(new InMemoryTenantFeatureConfigurationStore(), sink);
        var tenantId = NewTenantId();

        var first = await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = 0, VisitorEnabled = true }, Actor, CanonicalId.NewEventId(), ct);
        await service.UpdateAsync(tenantId, new UpdateTenantFeatureConfigurationRequest { Version = first.Version, AttendanceEnabled = true }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(2, sink.Events.Count);
    }
}
