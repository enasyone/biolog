using Biolog.Features.ApiSecurity;
using Xunit;

namespace Biolog.Features.ApiSecurity.Tests;

public sealed class ApiAuditTests
{
    [Fact]
    public async Task A_written_event_is_listed_for_its_own_tenant_only()
    {
        var sink = new InMemoryApiAuditSink();
        var eventA = new ApiAuditEvent(Guid.NewGuid().ToString(), 1, "ten_a", ApiAuditAction.TokenIssued, "client-1", null, null, "corr-1", DateTimeOffset.UtcNow);
        var eventB = new ApiAuditEvent(Guid.NewGuid().ToString(), 1, "ten_b", ApiAuditAction.TokenIssued, "client-2", null, null, "corr-2", DateTimeOffset.UtcNow);

        await sink.WriteAsync(eventA, CancellationToken.None);
        await sink.WriteAsync(eventB, CancellationToken.None);

        var tenantAEvents = await sink.ListForTenantAsync("ten_a", CancellationToken.None);

        Assert.Single(tenantAEvents);
        Assert.Equal(eventA.EventId, tenantAEvents[0].EventId);
    }

    [Fact]
    public async Task ExistsAsync_finds_a_written_event_by_id()
    {
        var sink = new InMemoryApiAuditSink();
        var auditEvent = new ApiAuditEvent(Guid.NewGuid().ToString(), 1, "ten_a", ApiAuditAction.RateLimitExceeded, "client-1", null, "oauth:token", "corr-1", DateTimeOffset.UtcNow);

        await sink.WriteAsync(auditEvent, CancellationToken.None);

        Assert.True(await sink.ExistsAsync(auditEvent.EventId, CancellationToken.None));
        Assert.False(await sink.ExistsAsync("unknown-event-id", CancellationToken.None));
    }

    [Fact]
    public async Task No_audit_event_ever_carries_a_secret_or_token_shaped_field()
    {
        // BIO-INT-003-AC04/AC03: ApiAuditEvent's own shape has no field capable of holding a raw
        // secret or bearer token - this test documents that invariant at the type level rather
        // than only hoping no caller ever passes one through `Detail`.
        var auditEvent = new ApiAuditEvent(Guid.NewGuid().ToString(), 1, "ten_a", ApiAuditAction.ServiceAccountCreated, "client-1", ActorId: null, Detail: null, "corr-1", DateTimeOffset.UtcNow);
        var sink = new InMemoryApiAuditSink();
        await sink.WriteAsync(auditEvent, CancellationToken.None);

        var properties = typeof(ApiAuditEvent).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Secret", properties);
        Assert.DoesNotContain("PlaintextSecret", properties);
        Assert.DoesNotContain("AccessToken", properties);
        Assert.DoesNotContain("ClientSecret", properties);
    }
}
