// Developer tests for TD-CRD-005-IDEMPOTENCY-RACE (High severity, Kemi-confirmed 2026-10-06).
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
// deliberately reinstated copy of the historical State-only decision does not. This project also
// hosts the ONE representative bounded-timeout test for the whole TD (Zainab's own concession:
// the timeout branch is identical across all five call sites, so one behavioral proof suffices).
using Biolog.Features.CardIssuance.Audit;
using Biolog.Features.CardIssuance.Domain;
using Biolog.Features.CardIssuance.Identifiers;
using Xunit;

namespace Biolog.Features.CardIssuance.Tests;

/// <summary>
/// Wraps a real <see cref="IIdempotencyStore"/> and holds the WINNING caller (the one whose own
/// <see cref="IIdempotencyStore.Claim"/> call returns <c>IsNewClaim: true</c>) blocked immediately
/// after its claim becomes durably visible in the inner store, until the test releases it. Every
/// losing caller's own <c>Claim</c> call (observing <c>IsNewClaim: false</c> against a still-
/// <c>InFlight</c> record) is signalled and passes straight through - this is what lets the test
/// deterministically confirm "the loser has genuinely observed the winner's still-pending claim"
/// before the winner is ever allowed to proceed and execute. Also doubles as the fixture for the
/// bounded-timeout test below: simply never calling <see cref="ReleaseOwner"/> reproduces "the
/// owner's attempt never completes" deterministically.
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

    public Task WaitForOwnerClaimAsync() => _ownerClaimed.Task;
    public Task WaitForLoserToObserveInFlightAsync() => _loserObservedInFlight.Task;
    public void ReleaseOwner() => _releaseOwner.TrySetResult();
}

public sealed class TdIdempotencyRaceTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private static AuditActor Operator() => new(ActorType.User, "operator-1");

    private static IssueCardRequest IssueRequest() => new() { TemplateRef = "template-1", PrinterRef = "printer-1" };

    [Fact]
    public async Task ActivateAsync_the_loser_must_genuinely_observe_the_winners_InFlight_claim_before_either_resolves()
    {
        var repository = new InMemoryCardIssueRepository();
        var auditSink = new InMemoryCardIssueAuditSink();
        var barrierStore = new ClaimBarrierIdempotencyStore(new InMemoryIdempotencyStore());
        var service = new CardIssueService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(),
            auditSink, new CardIssueLockRegistry(), barrierStore,
            idempotencyWaitTimeout: TimeSpan.FromSeconds(2), idempotencyWaitPollInterval: TimeSpan.FromMilliseconds(10));

        var issued = await service.IssueAsync(PersonId, CardClass, IssueRequest(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Task<CardIssue> Attempt() => service.ActivateAsync(PersonId, CardClass, issued.SequenceNumber, issued.Version, "idem-race", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var ownerTask = Task.Run(Attempt);
        await barrierStore.WaitForOwnerClaimAsync(); // the owner's claim is now durably InFlight - the owner itself is blocked, has NOT yet executed

        var loserTask = Task.Run(Attempt);
        await barrierStore.WaitForLoserToObserveInFlightAsync(); // the loser has now genuinely called Claim and seen the SAME still-InFlight record

        barrierStore.ReleaseOwner(); // only now may the owner proceed to execute
        var results = await Task.WhenAll(ownerTask, loserTask);

        Assert.Equal(results[0].Version, results[1].Version); // both callers observe the SAME stable activated card
        Assert.Equal(CardIssueStatus.Active, results[0].Status);

        var stored = repository.Get(TenantId, PersonId, CardClass, issued.SequenceNumber);
        Assert.Equal(2, stored!.Version); // exactly one activation from the Issued (v1) baseline, never a doubled execution
        Assert.Single(auditSink.Events, e => e.Action == "card_issue.activate");
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

    /// <summary>
    /// The ONE representative behavioral timeout test for the whole TD-IDEMPOTENCY-RACE-NGOZI
    /// remediation (Zainab's own concession: independent timeout tests at all five call sites are
    /// not required, since the timeout branch's loop shape and injectable settings are identical
    /// everywhere). Reuses <see cref="ClaimBarrierIdempotencyStore"/> from the race test above -
    /// simply never releasing the owner deterministically reproduces "the owner's attempt never
    /// completes," which is exactly what the claim-loser's bounded wait exists to fail closed
    /// against.
    /// </summary>
    [Fact]
    public async Task ActivateAsync_a_second_caller_times_out_if_the_owners_claim_never_completes()
    {
        var repository = new InMemoryCardIssueRepository();
        var barrierStore = new ClaimBarrierIdempotencyStore(new InMemoryIdempotencyStore());
        var service = new CardIssueService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(),
            new InMemoryCardIssueAuditSink(), new CardIssueLockRegistry(), barrierStore,
            idempotencyWaitTimeout: TimeSpan.FromMilliseconds(150), idempotencyWaitPollInterval: TimeSpan.FromMilliseconds(15));

        var issued = await service.IssueAsync(PersonId, CardClass, IssueRequest(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var ownerTask = Task.Run(() => service.ActivateAsync(PersonId, CardClass, issued.SequenceNumber, issued.Version, "idem-timeout", Operator(), CanonicalId.NewEventId(), CancellationToken.None));
        await barrierStore.WaitForOwnerClaimAsync(); // the owner is now genuinely blocked mid-flight and never released this test

        var error = await Assert.ThrowsAsync<CardIssueError>(() =>
            service.ActivateAsync(PersonId, CardClass, issued.SequenceNumber, issued.Version, "idem-timeout", Operator(), CanonicalId.NewEventId(), CancellationToken.None));
        Assert.Equal("idempotency_wait_timeout", error.Code);

        // Release the owner so the first call can finish cleanly - no orphaned background task.
        barrierStore.ReleaseOwner();
        var owner = await ownerTask;
        Assert.Equal(CardIssueStatus.Active, owner.Status);
        Assert.Equal(2, repository.Get(TenantId, PersonId, CardClass, issued.SequenceNumber)!.Version); // exactly one activation from the Issued (v1) baseline - the timed-out caller never executed at all
    }
}
