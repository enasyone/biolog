// Idempotency-claim-race remediation (Tunde/Seyi/Zainab's finding in BIO-INT-002-F01's own
// review round, Kemi-confirmed High severity, ported into this story at god's direction):
// EnrolmentPackageService.SubmitPackageAsync originally checked only
// `claimed.State == Completed` and fell through to execute on ANY in-flight claim regardless of
// ownership. These tests prove the fix directly against the service, mirroring
// tests/features/bio-acc-003/AccessCacheServiceConcurrencyTests.cs's own shape for the identical
// defect class, with short injectable wait-timeout/poll-interval constructor parameters so the
// timeout path is deterministic and fast rather than waiting multiple real seconds.
using Biolog.Features.OfflineEnrolment.Api;
using Biolog.Features.OfflineEnrolment.Audit;
using Biolog.Features.OfflineEnrolment.Domain;
using Xunit;

namespace Biolog.Features.OfflineEnrolment.Tests;

public sealed class EnrolmentPackageServiceConcurrencyTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private static readonly ResolvedDevice Device = new(DeviceId, TenantId);
    private static readonly AuditActor Actor = new(ActorType.Device, DeviceId);

    private static string NewPackageId() => $"evt_{Guid.CreateVersion7():D}";

    private static EnrolmentPackageSubmitRequest Request(string packageId) => new()
    {
        PackageId = packageId,
        TenantId = TenantId,
        DeviceId = DeviceId,
        FieldValues = new Dictionary<string, string> { [KnownFields.FirstName] = "Ada" },
        PhotoCaptured = false,
        BiometricModalitiesCaptured = [],
        SubmittedAt = "2026-09-23T00:00:00.000Z",
    };

    /// <summary>Counts calls, not resulting storage size - <see cref="IEnrolmentPackageRepository.CreateAsync"/> is keyed by package id, so a second execution with identical content would silently overwrite rather than duplicate, masking a double-execution bug if only the stored count were checked.</summary>
    private sealed class CountingEnrolmentPackageRepository : IEnrolmentPackageRepository
    {
        private readonly InMemoryEnrolmentPackageRepository _inner = new();
        private int _createCallCount;
        public int CreateCallCount => _createCallCount;

        public Task<EnrolmentPackage?> GetAsync(string tenantId, string packageId, CancellationToken cancellationToken = default) =>
            _inner.GetAsync(tenantId, packageId, cancellationToken);

        public Task CreateAsync(EnrolmentPackage package, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _createCallCount);
            return _inner.CreateAsync(package, cancellationToken);
        }

        public Task ReplaceAsync(EnrolmentPackage package, CancellationToken cancellationToken = default) =>
            _inner.ReplaceAsync(package, cancellationToken);

        public Task<IReadOnlyList<EnrolmentPackage>> ListAcceptedAsync(string tenantId, CancellationToken cancellationToken = default) =>
            _inner.ListAcceptedAsync(tenantId, cancellationToken);

        public Task<IReadOnlyList<EnrolmentPackage>> ListByStatusAsync(string tenantId, EnrolmentPackageStatus status, CancellationToken cancellationToken = default) =>
            _inner.ListByStatusAsync(tenantId, status, cancellationToken);
    }

    /// <summary>Counts writes and, when <see cref="Gate"/> is set, blocks inside <see cref="WriteAsync"/> until released - lets a test deterministically prove a second caller is genuinely still in flight (not merely fast) before asserting the timeout path.</summary>
    private sealed class GatedEnrolmentAuditSink : IEnrolmentAuditSink
    {
        private readonly InMemoryEnrolmentAuditSink _inner = new();
        private int _writeCallCount;
        public int WriteCallCount => _writeCallCount;
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new();

        public async Task WriteAsync(EnrolmentAuditEvent auditEvent, CancellationToken cancellationToken = default)
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

    private static EnrolmentPackageService BuildService(
        IEnrolmentPackageRepository packageRepository, IEnrolmentAuditSink auditSink,
        TimeSpan? idempotencyWaitTimeout = null, TimeSpan? idempotencyWaitPollInterval = null) => new(
        new InMemoryEnrolmentCapturePolicyRepository(), packageRepository, new InMemoryEnrolmentDuplicateCandidateRepository(),
        new EnrolmentDuplicateReviewService(), new InMemoryIdempotencyStore(), auditSink,
        idempotencyWaitTimeout, idempotencyWaitPollInterval);

    [Fact]
    public async Task SubmitPackageAsync_two_genuinely_concurrent_callers_with_the_same_idempotency_key_execute_exactly_once()
    {
        // Reproduces the exact interleaving Kemi confirmed: two concurrent submit calls for the
        // SAME idempotency key, released together via a start gate (not Task.Delay-based
        // timing), both racing to claim. Before the fix, the second caller fell through on State
        // alone and executed a second time, silently replacing the first caller's stored result.
        // After the fix, exactly one caller ever becomes the owner (IsNewClaim) and executes; the
        // other waits for and replays that owner's own result.
        var packageRepository = new CountingEnrolmentPackageRepository();
        var auditSink = new GatedEnrolmentAuditSink();
        var service = BuildService(packageRepository, auditSink);
        var packageId = NewPackageId();
        var idempotencyKey = $"idem-{packageId}";
        var request = Request(packageId);

        var start = new TaskCompletionSource();
        Task<(EnrolmentPackageAckResponse Result, bool Replayed)> Race() => Task.Run(async () =>
        {
            await start.Task;
            return await service.SubmitPackageAsync(Device, request, idempotencyKey, Actor, "corr-race", CancellationToken.None);
        });

        var taskA = Race();
        var taskB = Race();
        start.SetResult();
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, packageRepository.CreateCallCount); // the package was created exactly once, never twice
        Assert.Equal(1, auditSink.WriteCallCount); // exactly one audit write, never a silent second one
        Assert.Equal(results[0].Result.PackageId, results[1].Result.PackageId); // both callers observe the SAME stable package id
        Assert.True(results[0].Replayed || results[1].Replayed); // exactly one caller executed, the other replayed
        Assert.False(results[0].Replayed && results[1].Replayed); // never BOTH replayed - someone had to actually execute
    }

    [Fact]
    public async Task SubmitPackageAsync_a_second_caller_times_out_if_the_owners_claim_never_completes()
    {
        // The owner's own attempt genuinely never finishes within this test (the audit sink's own
        // gate is never released) - a second concurrent caller for the same key must wait,
        // bounded, then fail closed with idempotency_wait_timeout rather than waiting forever or
        // (the pre-fix bug) executing a second time regardless. The wait bound is configured short
        // so this test runs fast and deterministically, not by luck of timing.
        var packageRepository = new CountingEnrolmentPackageRepository();
        var auditSink = new GatedEnrolmentAuditSink { Gate = new TaskCompletionSource() };
        var service = BuildService(
            packageRepository, auditSink,
            idempotencyWaitTimeout: TimeSpan.FromMilliseconds(150), idempotencyWaitPollInterval: TimeSpan.FromMilliseconds(15));
        var packageId = NewPackageId();
        var idempotencyKey = $"idem-{packageId}";
        var request = Request(packageId);

        var ownerTask = service.SubmitPackageAsync(Device, request, idempotencyKey, Actor, "corr-owner", CancellationToken.None);
        await auditSink.Entered.Task; // deterministic: the owner has genuinely started executing (entered the audit write), not merely claimed

        var error = await Assert.ThrowsAsync<EnrolmentError>(() =>
            service.SubmitPackageAsync(Device, request, idempotencyKey, Actor, "corr-waiter", CancellationToken.None));
        Assert.Equal("idempotency_wait_timeout", error.Code);
        Assert.Equal(503, error.Status);

        // Release the owner so its own call can finish cleanly - no orphaned background task.
        auditSink.Gate!.SetResult();
        var owner = await ownerTask;
        Assert.False(owner.Replayed);
        Assert.Equal(1, packageRepository.CreateCallCount); // the timed-out caller never executed at all
    }
}
