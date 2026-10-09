// Idempotency-claim-race remediation (Tunde/Seyi/Zainab's finding in BIO-INT-002-F01's own
// review round, Kemi-confirmed High severity, ported into this story at god's direction):
// AccessCacheService.IntakePresentationEventAsync originally checked only
// `claimed.State == Completed` and fell through to execute on ANY in-flight claim regardless of
// ownership. These tests prove the fix directly against the service (bypassing the HTTP layer,
// unlike AccessCacheEndpointsIntegrationTests.cs) so the injectable wait-timeout/poll-interval
// constructor parameters can be set to short, deterministic values.
using Biolog.Features.AccessCache.Api;
using Biolog.Features.AccessCache.Audit;
using Biolog.Features.AccessCache.Domain;
using Xunit;

namespace Biolog.Features.AccessCache.Tests;

public sealed class AccessCacheServiceConcurrencyTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private static readonly ResolvedDevice Device = new(DeviceId, TenantId);
    private static readonly AuditActor Actor = new(ActorType.Device, DeviceId);

    private static string NewEventId() => $"evt_{Guid.CreateVersion7():D}";

    private static PresentationEventIntakeRequest Request(string eventId) => new()
    {
        EventId = eventId,
        TenantId = TenantId,
        DeviceId = DeviceId,
        PersonId = PersonId,
        Outcome = "DENIED",
        ReasonCode = "DENIED_REVOKED_CACHED",
        OccurredAt = "2026-09-24T00:00:00.000Z",
        CacheSnapshotAt = "2026-09-23T12:00:00.000Z",
        Stale = false,
    };

    /// <summary>Counts calls, not resulting storage size - <see cref="IPresentationEventLog.AppendAsync"/> is keyed by event id, so a second execution with identical content would silently overwrite rather than duplicate, masking a double-execution bug if only the stored count were checked.</summary>
    private sealed class CountingPresentationEventLog : IPresentationEventLog
    {
        private readonly InMemoryPresentationEventLog _inner = new();
        private int _appendCallCount;
        public int AppendCallCount => _appendCallCount;

        public Task AppendAsync(PresentationEvent presentationEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _appendCallCount);
            return _inner.AppendAsync(presentationEvent, cancellationToken);
        }

        public Task<IReadOnlyList<PresentationEvent>> ListRecentAsync(string tenantId, int limit, CancellationToken cancellationToken = default) =>
            _inner.ListRecentAsync(tenantId, limit, cancellationToken);
    }

    /// <summary>Counts writes and, when <see cref="Gate"/> is set, blocks inside <see cref="WriteAsync"/> until released - lets a test deterministically prove a second caller is genuinely still in flight (not merely fast) before asserting the timeout path.</summary>
    private sealed class GatedAccessCacheAuditSink : IAccessCacheAuditSink
    {
        private readonly InMemoryAccessCacheAuditSink _inner = new();
        private int _writeCallCount;
        public int WriteCallCount => _writeCallCount;
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new();

        public async Task WriteAsync(AccessCacheAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _writeCallCount);
            if (Gate is not null)
            {
                Entered.TrySetResult();
                await Gate.Task;
            }
            await _inner.WriteAsync(auditEvent, cancellationToken);
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            _inner.ExistsAsync(eventId, cancellationToken);
    }

    [Fact]
    public async Task IntakePresentationEventAsync_two_genuinely_concurrent_callers_with_the_same_idempotency_key_execute_exactly_once()
    {
        // Reproduces the exact interleaving Kemi confirmed: two concurrent intake calls for the
        // SAME idempotency key (= event id for this feature), released together via a start gate
        // (not Task.Delay-based timing), both racing to claim. Before the fix, the second caller
        // fell through on State alone and executed a second time, silently replacing the first
        // caller's stored result. After the fix, exactly one caller ever becomes the owner
        // (IsNewClaim) and executes; the other waits for and replays that owner's own result.
        var eventLog = new CountingPresentationEventLog();
        var auditSink = new GatedAccessCacheAuditSink();
        var service = new AccessCacheService(
            new FakeIdentityRevocationFeedGateway(), new InMemoryIdempotencyStore(), eventLog, auditSink);
        var eventId = NewEventId();
        var request = Request(eventId);

        var start = new TaskCompletionSource();
        Task<(PresentationEventAckResponse Result, bool Replayed)> Race() => Task.Run(async () =>
        {
            await start.Task;
            return await service.IntakePresentationEventAsync(Device, request, eventId, Actor, "corr-race", CancellationToken.None);
        });

        var taskA = Race();
        var taskB = Race();
        start.SetResult();
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, eventLog.AppendCallCount); // the presentation event was appended exactly once, never twice
        Assert.Equal(1, auditSink.WriteCallCount); // exactly one audit write, never a silent second one
        Assert.Equal(results[0].Result.EventId, results[1].Result.EventId); // both callers observe the SAME stable event id
        Assert.True(results[0].Replayed || results[1].Replayed); // exactly one caller executed, the other replayed
        Assert.False(results[0].Replayed && results[1].Replayed); // never BOTH replayed - someone had to actually execute
    }

    [Fact]
    public async Task IntakePresentationEventAsync_a_second_caller_times_out_if_the_owners_claim_never_completes()
    {
        // The owner's own attempt genuinely never finishes within this test (the audit sink's own
        // gate is never released) - a second concurrent caller for the same key must wait,
        // bounded, then fail closed with idempotency_wait_timeout rather than waiting forever or
        // (the pre-fix bug) executing a second time regardless. The wait bound is configured short
        // so this test runs fast and deterministically, not by luck of timing.
        var eventLog = new CountingPresentationEventLog();
        var auditSink = new GatedAccessCacheAuditSink { Gate = new TaskCompletionSource() };
        var service = new AccessCacheService(
            new FakeIdentityRevocationFeedGateway(), new InMemoryIdempotencyStore(), eventLog, auditSink,
            idempotencyWaitTimeout: TimeSpan.FromMilliseconds(150), idempotencyWaitPollInterval: TimeSpan.FromMilliseconds(15));
        var eventId = NewEventId();
        var request = Request(eventId);

        var ownerTask = service.IntakePresentationEventAsync(Device, request, eventId, Actor, "corr-owner", CancellationToken.None);
        await auditSink.Entered.Task; // deterministic: the owner has genuinely started executing (entered the audit write), not merely claimed

        var error = await Assert.ThrowsAsync<AccessCacheError>(() =>
            service.IntakePresentationEventAsync(Device, request, eventId, Actor, "corr-waiter", CancellationToken.None));
        Assert.Equal("idempotency_wait_timeout", error.Code);
        Assert.Equal(503, error.Status);

        // Release the owner so its own call can finish cleanly - no orphaned background task.
        auditSink.Gate!.SetResult();
        var owner = await ownerTask;
        Assert.False(owner.Replayed);
        Assert.Equal(1, eventLog.AppendCallCount); // the timed-out caller never executed at all
    }
}
