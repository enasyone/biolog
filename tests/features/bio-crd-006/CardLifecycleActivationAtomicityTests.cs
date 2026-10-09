// Developer tests for BIO-CRD-006. SYNTHETIC fixtures only.
//
// Regression coverage for BIO-CRD-006-QA-01 (Chioma's acceptance review round 1): the original
// ActivateReplacementAsync activated the replacement BEFORE sweeping every earlier card to
// PermanentlyRevoked. If a later record's revoke failed, the replacement was left observably
// Active while an earlier card was still Locked/Recovered (reachable by RecoverAsync) or absent
// from the deny list - a real AC04/AC05 violation for the whole failure interval, not merely
// until a retry. Fixed by sweeping every earlier card to PermanentlyRevoked FIRST, and only
// calling the gateway's own ActivateAsync once that whole sweep has fully committed - see
// CardLifecycleService.ActivateReplacementAsync's own header comment. This file proves the fix
// directly: an injected audit failure partway through the sweep must leave the replacement
// never activated at all, not merely eventually consistent.
using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Identifiers;
using Xunit;

namespace Biolog.Features.CardLifecycle.Tests;

public sealed class CardLifecycleActivationAtomicityTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    /// <summary>Fails the audit write for one specific earlier sequence number only, confirming (via ExistsAsync => false) that the write genuinely never persisted - the same "resolve ambiguity, don't assume" shape ADR-0003 Decision 7 requires.</summary>
    private sealed class AuditSinkFailingForSequence(int failingSequenceNumber) : ICardLifecycleAuditSink
    {
        public List<string> WrittenTargetIds { get; } = [];

        public Task WriteAsync(CardLifecycleAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (auditEvent.Target.Id == $"{PersonId}:{CardClass}:{failingSequenceNumber}")
            {
                throw new InvalidOperationException("audit offline");
            }

            WrittenTargetIds.Add(auditEvent.Target.Id!);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private static AuditActor Operator() => new(ActorType.User, "operator-1");

    private static ReplacementCardRequest Request() => new() { TemplateRef = "t", PrinterRef = "p" };

    [Fact]
    public async Task BIOCRD006QA01_a_confirmed_audit_failure_partway_through_the_sweep_leaves_the_replacement_never_activated()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Issued);
        gateway.Seed(PersonId, CardClass, 2, CardIssueLifecycleStatus.Active);
        var failingSink = new AuditSinkFailingForSequence(failingSequenceNumber: 2);
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            failingSink, new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());

        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(3, replacement.SequenceNumber);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));
        Assert.Equal("audit_unavailable", error.Code);

        // The core BIO-CRD-006-QA-01 assertion: the replacement was NEVER activated - there is
        // no observable state where it is Active while sequence 2 is still un-revoked.
        var replacementIssue = await gateway.GetAsync(PersonId, CardClass, replacement.SequenceNumber, CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Issued, replacementIssue!.Status);

        // Sequence 1 (processed before the failing sequence 2) reached RevocationPending - the
        // sweep is allowed to make real, durable progress - but not yet the terminal
        // PermanentlyRevoked, since activation was never even attempted this round (the sweep
        // itself failed before reaching it). RevocationPending is already denied and
        // non-recoverable (QA-03), so QA-01's own invariant still holds throughout.
        var sequenceOne = repository.Get(TenantId, PersonId, CardClass, 1);
        Assert.NotNull(sequenceOne);
        Assert.Equal(CardLockState.RevocationPending, sequenceOne!.LockState);

        // Sequence 2 itself never committed any row at all - the confirmed audit failure during
        // its own BeginRevocationAsync left it exactly where it started, not half-updated.
        var sequenceTwo = repository.Get(TenantId, PersonId, CardClass, 2);
        Assert.Null(sequenceTwo);
    }

    [Fact]
    public async Task BIOCRD006QA01_recovering_a_reported_earlier_card_while_activation_is_still_failing_partway_through_the_sweep_is_still_correctly_governed()
    {
        // Sequence 1 was reported (Locked) before any replacement existed. A failure at sequence
        // 2 must not activate the replacement, so sequence 1 - not yet reached by the sweep in
        // this particular failed attempt - is still legitimately Locked, and AC02's own recovery
        // path still works normally, because no replacement has actually activated yet.
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        gateway.Seed(PersonId, CardClass, 2, CardIssueLifecycleStatus.Issued);
        var workingSink = new InMemoryCardLifecycleAuditSink();
        var workingService = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            workingSink, new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        var reported = await workingService.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var replacement = await workingService.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(3, replacement.SequenceNumber);

        var failingSink = new AuditSinkFailingForSequence(failingSequenceNumber: 1);
        var failingService = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            failingSink, new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());

        await Assert.ThrowsAsync<CardLifecycleError>(() =>
            failingService.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        var replacementIssue = await gateway.GetAsync(PersonId, CardClass, replacement.SequenceNumber, CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Issued, replacementIssue!.Status); // still never activated

        var recovered = await workingService.RecoverAsync(PersonId, CardClass, 1, reported.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(CardLockState.Recovered, recovered.LockState); // legitimately still recoverable - no replacement ever activated
    }

    [Fact]
    public async Task BIOCRD006QA01_a_successful_sweep_still_activates_normally_when_every_audit_write_works()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var activated = await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(CardIssueLifecycleStatus.Active, activated.Status);
        Assert.Equal(CardLockState.PermanentlyRevoked, repository.Get(TenantId, PersonId, CardClass, 1)!.LockState);
    }

    // --- BIO-CRD-006-QA-02 (Chioma's acceptance review round 2): the replacement's own
    // activation preconditions must be rejected BEFORE any earlier-card revocation side effect. ---

    [Fact]
    public async Task BIOCRD006QA02_a_missing_expected_version_on_the_replacement_is_rejected_before_any_earlier_card_is_touched()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        var reported = await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, expectedVersion: null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("precondition_required", error.Code);
        var replacementIssue = await gateway.GetAsync(PersonId, CardClass, replacement.SequenceNumber, CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Issued, replacementIssue!.Status); // never activated
        var earlierRecord = repository.Get(TenantId, PersonId, CardClass, 1);
        Assert.Equal(CardLockState.Locked, earlierRecord!.LockState); // still Locked - AC02's own boundary, never destroyed
        Assert.Equal(reported.Version, earlierRecord.Version); // untouched - not even bumped
    }

    [Fact]
    public async Task BIOCRD006QA02_a_stale_expected_version_on_the_replacement_is_rejected_before_any_earlier_card_is_touched()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, expectedVersion: replacement.Version + 41, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("concurrency_conflict", error.Code);
        Assert.Null(repository.Get(TenantId, PersonId, CardClass, 1)); // never created - sequence 1 was never reported, and the sweep never ran
    }

    [Fact]
    public async Task BIOCRD006QA02_activating_a_nonexistent_replacement_sequence_is_rejected_before_any_earlier_card_is_touched()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ActivateReplacementAsync(PersonId, CardClass, sequenceNumber: 99, expectedVersion: 1, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("card_issue_not_found", error.Code);
        Assert.Null(repository.Get(TenantId, PersonId, CardClass, 1));
    }

    [Fact]
    public async Task BIOCRD006QA02_reusing_an_activate_replacement_idempotency_key_with_different_content_is_rejected_before_any_earlier_card_is_touched()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        var firstReplacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.ActivateReplacementAsync(PersonId, CardClass, firstReplacement.SequenceNumber, firstReplacement.Version, "idem-shared", Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var secondReplacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ActivateReplacementAsync(PersonId, CardClass, secondReplacement.SequenceNumber, secondReplacement.Version, "idem-shared", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("idempotency_key_reuse", error.Code);
        var secondReplacementIssue = await gateway.GetAsync(PersonId, CardClass, secondReplacement.SequenceNumber, CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Issued, secondReplacementIssue!.Status); // the second, distinct replacement was never activated
    }

    [Fact]
    public async Task BIOCRD006QA02_replaying_the_same_activate_replacement_request_short_circuits_before_reaching_the_sweep_at_all()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        // An audit sink that fails on ANY write - if the replay actually re-ran the sweep, this
        // second call would throw; it must not, because the reservation itself short-circuits
        // before ever touching the sweep or the audit sink again.
        var alwaysFailingAfterFirstUse = new FailAfterNCallsAuditSink(allowedWrites: int.MaxValue);
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            alwaysFailingAfterFirstUse, new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var first = await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        alwaysFailingAfterFirstUse.StartFailing();
        var second = await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(first, second); // cached result, no exception - the sweep never re-ran
    }

    private sealed class FailAfterNCallsAuditSink(int allowedWrites) : ICardLifecycleAuditSink
    {
        private int _writes;
        private bool _failing;

        public void StartFailing() => _failing = true;

        public Task WriteAsync(CardLifecycleAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (_failing || _writes >= allowedWrites)
            {
                throw new InvalidOperationException("audit offline");
            }

            _writes++;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    // --- BIO-CRD-006-QA-03 (Chioma's acceptance review round 3): the dry-run precheck reads a
    // snapshot, but only the gateway's own authoritative ActivateAsync compare-and-set is the
    // real source of truth. A concurrent mutation between that read and the authoritative call
    // (simulated here by forcing the FIRST ActivateAsync call to fail, exactly as a real CAS
    // conflict would) must leave earlier-card state unchanged or safely compensated - never
    // destroyed for an activation that never actually happened. ---

    /// <summary>Wraps a real <see cref="FakeCardIssueGateway"/> and fails the first call to <see cref="ActivateAsync"/> with a synthetic concurrency conflict, simulating a concurrent mutation racing the authoritative compare-and-set between this story's own dry-run precheck and the real commit.</summary>
    private sealed class ActivateAsyncFailsOnceGateway(FakeCardIssueGateway inner) : ICardIssueGateway
    {
        private int _activateCalls;

        public Task<CardIssueSnapshot?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default) =>
            inner.GetAsync(personId, cardClass, sequenceNumber, cancellationToken);

        public Task<IReadOnlyList<CardIssueSnapshot>> ListAsync(string personId, string cardClass, CancellationToken cancellationToken = default) =>
            inner.ListAsync(personId, cardClass, cancellationToken);

        public Task<CardIssueSnapshot> IssueReplacementAsync(string personId, string cardClass, ReplacementCardRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
            inner.IssueReplacementAsync(personId, cardClass, request, actor, correlationId, cancellationToken);

        public Task<CardIssueSnapshot> ActivateAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, string idempotencyKey, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
        {
            if (_activateCalls++ == 0)
            {
                throw new CardLifecycleError("simulated concurrent mutation raced the compare-and-set", "concurrency_conflict", 409);
            }

            return inner.ActivateAsync(personId, cardClass, sequenceNumber, expectedVersion, idempotencyKey, actor, correlationId, cancellationToken);
        }

        public Task<CardIssueSnapshot> DeactivateAsync(string personId, string cardClass, int sequenceNumber, int expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
            inner.DeactivateAsync(personId, cardClass, sequenceNumber, expectedVersion, actor, correlationId, cancellationToken);
    }

    [Fact]
    public async Task BIOCRD006QA03_a_concurrent_mutation_that_fails_the_authoritative_activation_compensates_a_previously_Locked_earlier_card_back_to_Locked()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var innerGateway = new FakeCardIssueGateway();
        innerGateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var racyGateway = new ActivateAsyncFailsOnceGateway(innerGateway);
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, racyGateway, new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        var reported = await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));
        Assert.Equal("concurrency_conflict", error.Code);

        var replacementIssue = await innerGateway.GetAsync(PersonId, CardClass, replacement.SequenceNumber, CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Issued, replacementIssue!.Status); // never activated

        // The core BIO-CRD-006-QA-03 assertion: the earlier card is safely COMPENSATED back to
        // Locked, not left destroyed as PermanentlyRevoked (or stuck at RevocationPending) for an
        // activation that never actually happened.
        var record = repository.Get(TenantId, PersonId, CardClass, 1);
        Assert.Equal(CardLockState.Locked, record!.LockState);
        Assert.Null(record.PriorLockStateBeforeRevocationPending);

        // It is genuinely usable again: still recoverable, and a retried activation now succeeds.
        var recovered = await service.RecoverAsync(PersonId, CardClass, 1, record.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(CardLockState.Recovered, recovered.LockState);

        var retried = await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-2", Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Active, retried.Status);
        Assert.Equal(CardLockState.PermanentlyRevoked, repository.Get(TenantId, PersonId, CardClass, 1)!.LockState);
    }

    [Fact]
    public async Task BIOCRD006QA03_a_concurrent_mutation_that_fails_the_authoritative_activation_compensates_a_never_reported_earlier_card_back_to_Unlocked_and_presentation_is_allowed()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var innerGateway = new FakeCardIssueGateway();
        innerGateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var racyGateway = new ActivateAsyncFailsOnceGateway(innerGateway);
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, racyGateway, new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        var record = repository.Get(TenantId, PersonId, CardClass, 1);
        Assert.Equal(CardLockState.Unlocked, record!.LockState); // had no prior report - compensates to Unlocked, not left dangling

        var check = await service.CheckPresentationAsync(PersonId, CardClass, 1, null, Operator(), CancellationToken.None);
        Assert.Equal(PresentationOutcome.Allowed, check.Outcome); // genuinely usable again
    }

    [Fact]
    public async Task BIOCRD006QA03_presentation_denies_a_card_while_it_is_RevocationPending()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Issued);
        gateway.Seed(PersonId, CardClass, 2, CardIssueLifecycleStatus.Active);
        var failingSink = new AuditSinkFailingForSequence(failingSequenceNumber: 2);
        var failingService = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            failingSink, new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        var replacement = await failingService.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(3, replacement.SequenceNumber);
        await Assert.ThrowsAsync<CardLifecycleError>(() =>
            failingService.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));
        Assert.Equal(CardLockState.RevocationPending, repository.Get(TenantId, PersonId, CardClass, 1)!.LockState);

        // A separate, working service instance (different audit sink) reading the SAME
        // repository still correctly denies presentation for the still-pending record.
        var workingService = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        var check = await workingService.CheckPresentationAsync(PersonId, CardClass, 1, null, Operator(), CancellationToken.None);

        Assert.Equal(PresentationOutcome.Denied, check.Outcome);
        Assert.Equal("revocation_pending", check.ReasonCode);
        var denyList = await workingService.GetDenyListAsync(since: null, cancellationToken: CancellationToken.None);
        Assert.Contains(denyList, r => r.SequenceNumber == 1 && r.LockState == CardLockState.RevocationPending);
    }

    // --- BIO-OPS-003-AC04 (Seyi's acceptance run, Kemi's ruling): neither this story's own
    // ActivateReplacementAsync nor BIO-CRD-007's own ActivateAfterProductionAsync composed
    // "production passed" AND "earlier cards revoked" into one check at the one endpoint that
    // actually performs a replacement activation. Fixed by consulting IProductionStatusGateway
    // right after the lock, before the sweep - see CardLifecycleService's own header.

    [Fact]
    public async Task BIOOPS003AC04_activation_is_refused_when_production_has_not_passed_and_the_earlier_card_is_never_swept()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var productionGateway = new FakeProductionStatusGateway();
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), productionGateway, new FakeCardActivationBarrier());
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        productionGateway.Seed(PersonId, CardClass, replacement.SequenceNumber, hasPassed: false, latestJobSequenceNumber: 1, latestStatus: ProductionJobLifecycleStatus.Failed);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));
        Assert.Equal("production_not_passed", error.Code);
        Assert.Equal(1, error.Detail["latest_job_sequence_number"]);
        Assert.Equal("failed", error.Detail["latest_status"]);

        // The core composition-gap assertion: refused BEFORE the sweep ever touched sequence 1 -
        // still genuinely Active, no lifecycle record created for it at all.
        var replacementIssue = await gateway.GetAsync(PersonId, CardClass, replacement.SequenceNumber, CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Issued, replacementIssue!.Status);
        Assert.Null(repository.Get(TenantId, PersonId, CardClass, 1));

        // The idempotency claim is released on this failure too (same catch-all as every other
        // precondition in this method) - a retry with the SAME key, once production genuinely
        // passes, is not stuck waiting out a stale claim.
        productionGateway.Seed(PersonId, CardClass, replacement.SequenceNumber, hasPassed: true);
        var retried = await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Active, retried.Status);
    }

    [Fact]
    public async Task BIOOPS003AC04_activation_still_succeeds_and_sweeps_normally_once_production_has_passed()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var productionGateway = new FakeProductionStatusGateway();
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(), new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), productionGateway, new FakeCardActivationBarrier());
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        productionGateway.Seed(PersonId, CardClass, replacement.SequenceNumber, hasPassed: true, latestJobSequenceNumber: 1, latestStatus: ProductionJobLifecycleStatus.Passed);

        var activated = await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Active, activated.Status);
        Assert.Equal(CardLockState.PermanentlyRevoked, repository.Get(TenantId, PersonId, CardClass, 1)!.LockState);
    }
}
