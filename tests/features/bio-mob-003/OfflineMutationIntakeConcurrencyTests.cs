// Idempotency-claim-race remediation (Tunde/Seyi/Zainab's finding in BIO-INT-002-F01's own
// review round, Kemi-confirmed High severity, ported into this story at god's direction):
// OfflineMutationIntakeService.IntakeAsync originally checked only
// `claimed.State == Completed` and fell through to execute on ANY in-flight claim regardless of
// ownership. These tests prove the fix directly against the service, mirroring
// tests/features/bio-acc-003/AccessCacheServiceConcurrencyTests.cs's own shape for the identical
// defect class, with short injectable wait-timeout/poll-interval constructor parameters so the
// timeout path is deterministic and fast rather than waiting multiple real seconds.
using Biolog.Features.OfflineSync.Api;
using Biolog.Features.OfflineSync.Audit;
using Biolog.Features.OfflineSync.Domain;
using Biolog.Features.OfflineSync.Identifiers;
using Xunit;

namespace Biolog.Features.OfflineSync.Tests;

public sealed class OfflineMutationIntakeConcurrencyTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string AggregateId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";

    private static ResolvedDevice Device() => new(DeviceId, TenantId);

    private static AuditActor Operator() => new(ActorType.Device, DeviceId);

    private static OfflineMutationDeltaRequest Delta(string mutationId, int baseVersion, string idempotencyKey, string operation = "ADDITION") => new()
    {
        MutationId = mutationId,
        TenantId = TenantId,
        DeviceId = DeviceId,
        AggregateId = AggregateId,
        BaseVersion = baseVersion,
        IdempotencyKey = idempotencyKey,
        OccurredAt = "2026-09-23T00:00:00.000Z",
        Operation = operation,
        Payload = System.Text.Json.JsonDocument.Parse("""{"field":"SYNTHETIC"}""").RootElement,
    };

    private static string NewMutationId() => $"evt_{Guid.CreateVersion7():D}";

    /// <summary>Counts calls, not resulting ledger state - <see cref="IAggregateVersionLedger.Commit"/> is an unconditional overwrite keyed by (tenant, aggregate), so a second execution computing the SAME new version would silently overwrite with an identical value, masking a double-execution bug if only the final <see cref="Peek"/> value were checked.</summary>
    private sealed class CountingAggregateVersionLedger : IAggregateVersionLedger
    {
        private readonly InMemoryAggregateVersionLedger _inner = new();
        private int _commitCallCount;
        public int CommitCallCount => _commitCallCount;

        public int? Peek(string tenantId, string aggregateId) => _inner.Peek(tenantId, aggregateId);

        public void Commit(string tenantId, string aggregateId, int newVersion)
        {
            Interlocked.Increment(ref _commitCallCount);
            _inner.Commit(tenantId, aggregateId, newVersion);
        }
    }

    /// <summary>Counts writes and, when <see cref="Gate"/> is set, blocks inside <see cref="WriteAsync"/> until released - lets a test deterministically prove a second caller is genuinely still in flight (not merely fast) before asserting the timeout path.</summary>
    private sealed class GatedOfflineSyncAuditSink : IOfflineSyncAuditSink
    {
        private readonly InMemoryOfflineSyncAuditSink _inner = new();
        private int _writeCallCount;
        public int WriteCallCount => _writeCallCount;
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new();

        public async Task WriteAsync(OfflineSyncAuditEvent auditEvent, CancellationToken cancellationToken = default)
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
    public async Task IntakeAsync_two_genuinely_concurrent_callers_with_the_same_idempotency_key_execute_exactly_once()
    {
        // Reproduces the exact interleaving Kemi confirmed: two concurrent intake calls for the
        // SAME idempotency key, released together via a start gate (not Task.Delay-based
        // timing), both racing to claim. Before the fix, the second caller fell through on State
        // alone and executed a second time, silently replacing the first caller's stored result.
        // After the fix, exactly one caller ever becomes the owner (IsNewClaim) and executes; the
        // other waits for and replays that owner's own result.
        var ledger = new CountingAggregateVersionLedger();
        var auditSink = new GatedOfflineSyncAuditSink();
        var service = new OfflineMutationIntakeService(
            new InMemoryIdempotencyStore(), ledger, auditSink, new OfflineMutationLockRegistry());
        var mutationId = NewMutationId();
        var idempotencyKey = $"idem-{mutationId}";
        var request = Delta(mutationId, 1, idempotencyKey);

        var start = new TaskCompletionSource();
        Task<(OfflineMutationAckResult Result, bool Replayed)> Race() => Task.Run(async () =>
        {
            await start.Task;
            return await service.IntakeAsync(Device(), request, idempotencyKey, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        });

        var taskA = Race();
        var taskB = Race();
        start.SetResult();
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, ledger.CommitCallCount); // the version ledger was committed exactly once, never twice
        Assert.Equal(1, auditSink.WriteCallCount); // exactly one audit write, never a silent second one
        Assert.Equal(results[0].Result.Version, results[1].Result.Version); // both callers observe the SAME stable version
        Assert.True(results[0].Replayed || results[1].Replayed); // exactly one caller executed, the other replayed
        Assert.False(results[0].Replayed && results[1].Replayed); // never BOTH replayed - someone had to actually execute
    }

    [Fact]
    public async Task IntakeAsync_a_second_caller_times_out_if_the_owners_claim_never_completes()
    {
        // The owner's own attempt genuinely never finishes within this test (the audit sink's own
        // gate is never released) - a second concurrent caller for the same key must wait,
        // bounded, then fail closed with idempotency_wait_timeout rather than waiting forever or
        // (the pre-fix bug) executing a second time regardless. The wait bound is configured short
        // so this test runs fast and deterministically, not by luck of timing.
        var ledger = new CountingAggregateVersionLedger();
        var auditSink = new GatedOfflineSyncAuditSink { Gate = new TaskCompletionSource() };
        var service = new OfflineMutationIntakeService(
            new InMemoryIdempotencyStore(), ledger, auditSink, new OfflineMutationLockRegistry(),
            idempotencyWaitTimeout: TimeSpan.FromMilliseconds(150), idempotencyWaitPollInterval: TimeSpan.FromMilliseconds(15));
        var mutationId = NewMutationId();
        var idempotencyKey = $"idem-{mutationId}";
        var request = Delta(mutationId, 1, idempotencyKey);

        var ownerTask = service.IntakeAsync(Device(), request, idempotencyKey, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await auditSink.Entered.Task; // deterministic: the owner has genuinely started executing (entered the audit write), not merely claimed

        var error = await Assert.ThrowsAsync<OfflineSyncError>(() =>
            service.IntakeAsync(Device(), request, idempotencyKey, Operator(), CanonicalId.NewEventId(), CancellationToken.None));
        Assert.Equal("idempotency_wait_timeout", error.Code);
        Assert.Equal(503, error.Status);

        // Release the owner so its own call can finish cleanly - no orphaned background task.
        auditSink.Gate!.SetResult();
        var owner = await ownerTask;
        Assert.False(owner.Replayed);
        Assert.Equal(1, ledger.CommitCallCount); // the timed-out caller never executed at all
    }
}
