// Developer tests for TD-PHO-002-IDEMPOTENCY-RACE (High severity, Kemi-confirmed 2026-10-06).
// SYNTHETIC fixtures only.
//
// Post-review remediation (Zainab CHANGES_REQUIRED, test proof): the first round of this test
// used only a shared start-of-task TaskCompletionSource, which gates when the two tasks are
// SCHEDULED, not when either actually reaches Claim(). The scheduler could still run the whole
// winning attempt to completion before the losing attempt ever calls Claim() - at which point the
// loser just takes the ordinary Completed-replay path, which the OLD defective State-only check
// (`if (claimed.State == Completed) return cached; else proceed and execute`) would ALSO pass.
// That gave the test no real discriminating power. Fixed per Zainab's exact direction, porting
// the pattern BIO-VIS-002 closed the identical gap with (rounds 9-10, Tunde/Seyi):
// `ClaimBarrierIdempotencyStore` below forces the real contested sequence deterministically -
// the winning caller is held immediately after its claim becomes durably InFlight, until the
// test has independently confirmed the losing caller has itself called Claim and observed that
// SAME still-InFlight record, before releasing the winner. A separate, minimal
// `Mutant_discrimination_proof` test then proves this harness shape genuinely discriminates: the
// real IsNewClaim-based decision survives a forced simultaneous-observation race, while a
// deliberately reinstated copy of the historical State-only decision does not.
using Biolog.Features.PhotoBooth.Audit;
using Biolog.Features.PhotoBooth.Domain;
using Xunit;

namespace Biolog.Features.PhotoBooth.Tests;

/// <summary>
/// Wraps a real <see cref="IIdempotencyStore"/> and holds the WINNING caller (the one whose own
/// <see cref="IIdempotencyStore.Claim"/> call returns <c>IsNewClaim: true</c>) blocked immediately
/// after its claim becomes durably visible in the inner store, until the test releases it. Every
/// losing caller's own <c>Claim</c> call (observing <c>IsNewClaim: false</c> against a still-
/// <c>InFlight</c> record) is signalled and passes straight through - this is what lets the test
/// deterministically confirm "the loser has genuinely observed the winner's still-pending claim"
/// before the winner is ever allowed to proceed and execute.
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
            _releaseOwner.Task.GetAwaiter().GetResult(); // block the real owner here, strictly before it proceeds to its own side effect
        }
        else if (claim.Record.State == IdempotencyRecordState.InFlight)
        {
            _loserObservedInFlight.TrySetResult();
        }

        return claim;
    }

    public void Complete(string tenantId, string operation, string idempotencyKey, object? result) => inner.Complete(tenantId, operation, idempotencyKey, result);

    public void Release(string tenantId, string operation, string idempotencyKey) => inner.Release(tenantId, operation, idempotencyKey);

    public IdempotencyRecord? Get(string tenantId, string operation, string idempotencyKey) => inner.Get(tenantId, operation, idempotencyKey);

    public Task WaitForOwnerClaimAsync() => _ownerClaimed.Task;
    public Task WaitForLoserToObserveInFlightAsync() => _loserObservedInFlight.Task;
    public void ReleaseOwner() => _releaseOwner.TrySetResult();
}

public sealed class TdIdempotencyRaceTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private static readonly AuditActor DeviceActor = new(ActorType.Device, DeviceId);

    private static byte[] CompliantContent() => SyntheticImages.Png(CaptureSpecification.Current.FrameWidthPx, CaptureSpecification.Current.FrameHeightPx, CaptureSpecification.Current.MinimumDpi);

    [Fact]
    public async Task SubmitAsync_the_loser_must_genuinely_observe_the_winners_InFlight_claim_before_either_resolves()
    {
        var repository = new InMemoryBoothCaptureRepository();
        var auditSink = new InMemoryCaptureAuditSink();
        var barrierStore = new ClaimBarrierIdempotencyStore(new InMemoryIdempotencyStore());
        var service = new CaptureService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(), new FakePhotoMasterPromotionGateway(),
            auditSink, barrierStore, new BoothCaptureLockRegistry(),
            idempotencyWaitTimeout: TimeSpan.FromSeconds(2), idempotencyWaitPollInterval: TimeSpan.FromMilliseconds(10));

        var content = CompliantContent();
        Task<BoothCapture> Attempt() => service.SubmitAsync(PersonId, DeviceId, content, true, true, "idem-race", DeviceActor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken);

        var ownerTask = Task.Run(Attempt);
        await barrierStore.WaitForOwnerClaimAsync(); // the owner's claim is now durably InFlight - the owner itself is blocked, has NOT yet executed

        var loserTask = Task.Run(Attempt);
        await barrierStore.WaitForLoserToObserveInFlightAsync(); // the loser has now genuinely called Claim and seen the SAME still-InFlight record

        barrierStore.ReleaseOwner(); // only now may the owner proceed to execute
        var results = await Task.WhenAll(ownerTask, loserTask);

        Assert.Equal(results[0].SequenceNumber, results[1].SequenceNumber); // both callers observe the SAME stable capture

        var stored = await repository.ListForPersonAsync(TenantId, PersonId, TestContext.Current.CancellationToken);
        Assert.Single(stored); // submitted exactly once, never twice
        Assert.Single(auditSink.Events, e => e.Action == "photo_booth.capture.submit");
    }

    /// <summary>
    /// Proves the harness shape above genuinely discriminates, per Zainab's explicit requirement:
    /// a minimal two-caller race, forced via a plain <see cref="Barrier"/> to make BOTH callers
    /// complete their own <see cref="IIdempotencyStore.Claim"/> call (i.e. both have legitimately
    /// observed the SAME pre-decision state) before EITHER acts on its decision - mirroring
    /// BIO-VIS-002-F09's own <c>checkBarrier</c> placement exactly (barrier after the observation,
    /// before the write). The real decision (<c>IsNewClaim</c>) converges to exactly one execution
    /// under this forced interleaving; a deliberately reinstated copy of the historical decision
    /// (<c>State != Completed</c> alone) does not - proving this test methodology has real
    /// discriminating power, not merely that production happens to behave.
    /// </summary>
    [Fact]
    public async Task Mutant_discrimination_the_historical_State_only_check_lets_both_concurrent_claimants_execute()
    {
        static async Task<int> RaceWithDecision(Func<IdempotencyClaim, bool> shouldExecute)
        {
            var store = new InMemoryIdempotencyStore();
            var barrier = new Barrier(2);
            var executionCount = 0;

            int Attempt()
            {
                var claim = store.Claim(TenantId, "op", "idem-mutant-race", "fp");
                barrier.SignalAndWait(); // both callers have now completed their OWN Claim/observation before either decides
                if (!shouldExecute(claim))
                {
                    return -1;
                }

                var mine = Interlocked.Increment(ref executionCount);
                store.Complete(TenantId, "op", "idem-mutant-race", mine);
                return mine;
            }

            await Task.WhenAll(Task.Run(Attempt), Task.Run(Attempt));
            return executionCount;
        }

        // The real, fixed decision: only the GetOrAdd winner (IsNewClaim) ever executes.
        var realExecutions = await RaceWithDecision(claim => claim.IsNewClaim);
        Assert.Equal(1, realExecutions);

        // The historical bug this story's own call site originally had: checking State alone.
        // Neither caller's claim can be Completed yet at the barrier (by construction - nobody
        // calls Complete before passing the barrier), so BOTH decide to execute.
        var mutantExecutions = await RaceWithDecision(claim => claim.Record.State != IdempotencyRecordState.Completed);
        Assert.Equal(2, mutantExecutions); // the mutant violates the exact invariant the real decision satisfies
    }
}
