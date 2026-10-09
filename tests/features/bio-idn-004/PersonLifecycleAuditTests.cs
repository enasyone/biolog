using Biolog.Features.PersonLifecycle.Api;
using Biolog.Features.PersonLifecycle.Audit;
using Biolog.Features.PersonLifecycle.Domain;
using Biolog.Features.PersonLifecycle.Identifiers;
using Xunit;

namespace Biolog.Features.PersonLifecycle.Tests;

/// <summary>
/// ADR-0003 Decision 7's audit-atomicity pattern, adapted for this story's cross-feature write
/// boundary - see <c>PersonLifecycleService</c>'s header for the full reasoning. The gateway
/// call (the real commit, owned by BIO-IDN-001) happens FIRST for every transition, including a
/// suspension; this feature's own supplementary audit/revocation records are written AFTER, so
/// an <c>audit_unavailable</c> here means something different from every sibling story: the
/// underlying status change already committed. The <c>BIOIDN004_QA01_*</c> tests below cover
/// the three-round history of getting suspension/revocation atomicity right - see that class's
/// header "Suspension/revocation atomicity".
/// </summary>
public sealed class PersonLifecycleAuditTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    private static TransitionRequest Request(string targetStatus, int version, string? reason = null) =>
        new() { TargetStatus = targetStatus, Version = version, Reason = reason };

    private static PersonLifecycleService NewService(
        FakePersonLifecycleGateway gateway, IPersonLifecycleAuditSink auditSink, ICredentialAccessRevocationEventSink revocationSink,
        IPendingSuspensionReconciliationStore? pendingStore = null, PersonLifecycleLockRegistry? lockRegistry = null) =>
        new(new FakeCurrentTenantProvider(TenantId), gateway.ForTenant(TenantId), auditSink, revocationSink,
            pendingStore ?? new InMemoryPendingSuspensionReconciliationStore(), lockRegistry ?? new PersonLifecycleLockRegistry());

    [Fact]
    public async Task A_successful_transition_records_exactly_one_allowed_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Verified);
        var sink = new InMemoryPersonLifecycleAuditSink();
        var service = NewService(gateway, sink, new InMemoryCredentialAccessRevocationEventSink());

        await service.TransitionAsync(PersonId, Request("ACTIVE", 1), null, Actor, CanonicalId.NewEventId(), ct);

        var evt = Assert.Single(sink.Events);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
        Assert.Equal(TenantId, evt.TenantId);
        Assert.Equal("person", evt.Target.Type);
        Assert.Equal(PersonId, evt.Target.Id);
    }

    [Fact]
    public async Task A_transition_rejected_before_the_gateway_call_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.PreEnrolled);
        var sink = new InMemoryPersonLifecycleAuditSink();
        var service = NewService(gateway, sink, new InMemoryCredentialAccessRevocationEventSink());

        await Assert.ThrowsAsync<PersonLifecycleError>(
            () => service.TransitionAsync(PersonId, Request("ACTIVE", 1), null, Actor, CanonicalId.NewEventId(), ct));

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task A_gateway_failure_records_no_audit_event()
    {
        // The gateway call IS the commit, and it happens before any audit write - so a genuine
        // gateway-reported failure (here, a stale version) can never leave a dangling event.
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Active, version: 5);
        var sink = new InMemoryPersonLifecycleAuditSink();
        var service = NewService(gateway, sink, new InMemoryCredentialAccessRevocationEventSink());

        await Assert.ThrowsAsync<PersonLifecycleError>(
            () => service.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, CanonicalId.NewEventId(), ct));

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task When_this_feature_s_own_audit_sink_fails_the_underlying_status_change_has_already_committed()
    {
        // The defining, novel behaviour of this story's audit-atomicity ordering: unlike every
        // sibling story, audit_unavailable here does NOT mean nothing happened.
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Verified);
        var service = NewService(gateway, new ThrowingAuditSink(), new InMemoryCredentialAccessRevocationEventSink());

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => service.TransitionAsync(PersonId, Request("ACTIVE", 1), null, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Equal(PersonStatus.Active, gateway[PersonId]!.Status); // committed anyway
        Assert.Equal(2, gateway[PersonId]!.Version);
    }

    [Fact]
    public async Task An_audit_write_that_durably_lands_but_throws_on_acknowledgement_still_reports_success()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Verified);
        var plainSink = new InMemoryPersonLifecycleAuditSink();
        var service = NewService(gateway, new DurablyWritesThenThrowsAuditSink(plainSink), new InMemoryCredentialAccessRevocationEventSink());

        var updated = await service.TransitionAsync(PersonId, Request("ACTIVE", 1), null, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(PersonStatus.Active, updated.Status);
        Assert.Single(plainSink.Events);
    }

    [Fact]
    public async Task BIOIDN004_QA01_round2_a_rejected_suspension_never_emits_a_false_revocation_event()
    {
        // BIO-IDN-004-QA-01 (Seyi round 2, High): the round-1 fix published the revocation
        // event BEFORE calling the gateway, which meant a REJECTED commit (here, a stale
        // version - an ordinary, expected outcome, not a rare fault) left a false revocation
        // event for a suspension that never happened. The gateway is now called FIRST - a
        // rejection must produce zero revocation events, always.
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Active, version: 5);
        var auditSink = new InMemoryPersonLifecycleAuditSink();
        var revocationSink = new InMemoryCredentialAccessRevocationEventSink();
        var service = NewService(gateway, auditSink, revocationSink);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => service.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("concurrency_conflict", error.Code);
        Assert.Empty(revocationSink.Events); // no false revocation for a suspension that never happened
        Assert.Empty(auditSink.Events);
        Assert.Equal(PersonStatus.Active, gateway[PersonId]!.Status); // untouched
    }

    [Fact]
    public async Task BIOIDN004_QA01_round3_when_the_revocation_sink_fails_the_suspension_still_commits_and_enters_pending_reconciliation()
    {
        // BIO-IDN-004-QA-01 (Seyi round 2, High, closure): committing first (round 2's fix)
        // reintroduces round 1's original problem for the success path - a revocation publish
        // failure now happens AFTER a genuine commit. The gap is closed not by choosing an
        // ordering, but by making the resulting state explicit, observable, and idempotently
        // reconcilable - see PersonLifecycleService's header "Suspension/revocation atomicity".
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Active);
        var auditSink = new InMemoryPersonLifecycleAuditSink();
        var service = NewService(gateway, auditSink, new ThrowingRevocationSink());

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => service.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("revocation_pending_reconciliation", error.Code);
        Assert.Equal(PersonStatus.Suspended, gateway[PersonId]!.Status); // the suspension IS real - never rolled back
        Assert.Equal(2, gateway[PersonId]!.Version);
        Assert.Single(auditSink.Events); // the transition itself is still traceable (AC04's ethos)
    }

    [Fact]
    public async Task BIOIDN004_QA01_a_reconciliation_retry_after_the_sink_recovers_completes_the_pending_revocation_exactly_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Active);
        var auditSink = new InMemoryPersonLifecycleAuditSink();
        var revocationSink = new InMemoryCredentialAccessRevocationEventSink();
        var pendingStore = new InMemoryPendingSuspensionReconciliationStore(); // shared across both services, matching production (a Singleton)
        var failingService = NewService(gateway, auditSink, new ThrowingRevocationSink(), pendingStore);
        await Assert.ThrowsAsync<PersonLifecycleError>(
            () => failingService.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, CanonicalId.NewEventId(), ct));
        Assert.True(await failingService.HasPendingRevocationsAsync(PersonId, ct));

        var recoveredService = NewService(gateway, auditSink, revocationSink, pendingStore);
        var reconciled = await recoveredService.ReconcilePendingRevocationsAsync(PersonId, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(PersonStatus.Suspended, reconciled.Status);
        Assert.Equal(2, reconciled.Version); // unchanged - reconciliation never mutates status
        Assert.Single(revocationSink.Events);
        Assert.False(await recoveredService.HasPendingRevocationsAsync(PersonId, ct));

        // A THIRD reconcile call must be a safe no-op - never a second revocation event.
        await recoveredService.ReconcilePendingRevocationsAsync(PersonId, Actor, CanonicalId.NewEventId(), ct);
        Assert.Single(revocationSink.Events);
    }

    [Fact]
    public async Task BIOIDN004_QA01_round4_the_pending_fact_survives_a_reactivation_and_stays_observable_and_reconcilable()
    {
        // BIO-IDN-004-QA-01 (Seyi round 3, High): round 3 derived "is reconciliation pending"
        // from the person's CURRENT status being SUSPENDED. A permissioned reactivation
        // succeeding BEFORE reconciliation silently erased both the observability and the only
        // reconciliation path - the missing revocation became permanently unreported and
        // unrecoverable, the exact class of gap round 1 already found once, reached by a
        // different path. The pending fact must now survive ANY subsequent transition.
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Active);
        var auditSink = new InMemoryPersonLifecycleAuditSink();
        var revocationSink = new InMemoryCredentialAccessRevocationEventSink();
        var pendingStore = new InMemoryPendingSuspensionReconciliationStore();
        var failingService = NewService(gateway, auditSink, new ThrowingRevocationSink(), pendingStore);
        await Assert.ThrowsAsync<PersonLifecycleError>(
            () => failingService.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, CanonicalId.NewEventId(), ct));
        Assert.True(await failingService.HasPendingRevocationsAsync(PersonId, ct));

        // A legitimate, fully-permissioned reactivation succeeds WITHOUT reconciliation ever
        // having happened - this must be allowed (see class header "AC03"), but must not erase
        // the pending fact.
        var reactivationService = NewService(gateway, auditSink, revocationSink, pendingStore);
        var reactivated = await reactivationService.TransitionAsync(
            PersonId, Request("ACTIVE", 2, reason: "cleared by security review"), "person.reactivate", Actor, CanonicalId.NewEventId(), ct);
        Assert.Equal(PersonStatus.Active, reactivated.Status);

        // The pending fact is STILL reported, even though the person is no longer SUSPENDED.
        Assert.True(await reactivationService.HasPendingRevocationsAsync(PersonId, ct));

        // And it is STILL reconcilable, even though the person is no longer SUSPENDED.
        await reactivationService.ReconcilePendingRevocationsAsync(PersonId, Actor, CanonicalId.NewEventId(), ct);
        Assert.False(await reactivationService.HasPendingRevocationsAsync(PersonId, ct));
        Assert.Single(revocationSink.Events);
    }

    [Fact]
    public async Task BIOIDN004_QA01_round5_an_audit_sink_outage_during_a_suspension_still_leaves_a_pending_record_behind()
    {
        // BIO-IDN-004-QA-01 (Seyi round 4, High): the pending record was previously marked
        // AFTER the ordinary supplementary lifecycle audit write. An audit-sink outage at that
        // point aborted the method (as audit_unavailable) before the pending record was ever
        // created - the person was left SUSPENDED with no revocation event AND no pending
        // record for ReconcilePendingRevocationsAsync to find, the gap undetectable and
        // unrepairable. The pending record must now be marked before EITHER write that can
        // throw, so it always survives regardless of which one (or both) fail. Both sinks are
        // down here specifically to isolate that property from "the revocation publish happened
        // to succeed anyway" (see the next test for that independent-attempts property).
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Active);
        var pendingStore = new InMemoryPendingSuspensionReconciliationStore();
        var service = NewService(gateway, new ThrowingAuditSink(), new ThrowingRevocationSink(), pendingStore);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => service.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, CanonicalId.NewEventId(), ct));

        // The revocation failure is surfaced (it is AC02's own security-critical write, and
        // takes priority over the also-failed, merely-supplementary audit write).
        Assert.Equal("revocation_pending_reconciliation", error.Code);
        Assert.Equal(PersonStatus.Suspended, gateway[PersonId]!.Status); // the suspension IS real
        Assert.True(await service.HasPendingRevocationsAsync(PersonId, ct)); // and the gap is detectable

        // The gap is also repairable, via the dedicated reconciliation operation, using a fresh
        // service instance with a healthy audit sink and a healthy revocation sink, sharing the
        // SAME pending store (matching production, where it is a Singleton).
        var recoveredService = NewService(gateway, new InMemoryPersonLifecycleAuditSink(), new InMemoryCredentialAccessRevocationEventSink(), pendingStore);
        await recoveredService.ReconcilePendingRevocationsAsync(PersonId, Actor, CanonicalId.NewEventId(), ct);
        Assert.False(await recoveredService.HasPendingRevocationsAsync(PersonId, ct));
    }

    [Fact]
    public async Task An_audit_sink_outage_during_a_suspension_still_attempts_the_revocation_publish()
    {
        // The ordinary lifecycle audit write and the revocation publish are attempted
        // INDEPENDENTLY - a failure in the (lower-priority) audit write must never prevent the
        // (security-critical) revocation publish from being attempted.
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Active);
        var revocationSink = new InMemoryCredentialAccessRevocationEventSink();
        var service = NewService(gateway, new ThrowingAuditSink(), revocationSink);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => service.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, CanonicalId.NewEventId(), ct));

        // The audit failure is reported (it is the only one that occurred - the revocation
        // publish succeeded), and the revocation event was genuinely recorded despite it.
        Assert.Equal("audit_unavailable", error.Code);
        Assert.Single(revocationSink.Events);
    }

    [Fact]
    public async Task Two_genuinely_concurrent_transitions_of_the_same_person_never_both_succeed()
    {
        var ct = TestContext.Current.CancellationToken;
        var gateway = new FakePersonLifecycleGateway();
        gateway.Seed(PersonId, TenantId, PersonStatus.Active);
        var sink = new InMemoryPersonLifecycleAuditSink();
        var lockRegistry = new PersonLifecycleLockRegistry();
        var service = NewService(gateway, sink, new InMemoryCredentialAccessRevocationEventSink(), lockRegistry: lockRegistry);

        var taskA = TryRunAsync(() => service.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, CanonicalId.NewEventId(), ct));
        var taskB = TryRunAsync(() => service.TransitionAsync(PersonId, Request("EXPIRED", 1), null, Actor, CanonicalId.NewEventId(), ct));

        var (resultA, errorA) = await taskA;
        var (resultB, errorB) = await taskB;

        // The per-person lock fully serializes the two callers, and this service re-reads a
        // FRESH snapshot under that lock (never trusting a snapshot read before acquiring it) -
        // so whichever caller runs second sees the WINNER's already-committed new status, not
        // the state it originally observed. Depending on timing that surfaces as either
        // transition_not_allowed (the winner's new status no longer permits the loser's
        // requested target - the common case here, since ACTIVE -> SUSPENDED and
        // ACTIVE -> EXPIRED cannot both still be valid once one of them has already happened)
        // or concurrency_conflict (a stale version the gateway itself rejects) - either is a
        // safe, fail-closed outcome; what this test actually proves is that EXACTLY ONE caller
        // succeeds and only ONE audit event was ever recorded, never two.
        var winner = resultA ?? resultB;
        var loserError = errorA ?? errorB;
        Assert.NotNull(winner);
        Assert.NotNull(loserError);
        Assert.Contains(loserError!.Code, new[] { "concurrency_conflict", "transition_not_allowed" });
        Assert.Single(sink.Events);
    }

    private static async Task<(PersonSnapshot? Result, PersonLifecycleError? Error)> TryRunAsync(Func<Task<PersonSnapshot>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (PersonLifecycleError ex)
        {
            return (null, ex);
        }
    }

    private sealed class ThrowingAuditSink : IPersonLifecycleAuditSink
    {
        public Task WriteAsync(PersonLifecycleAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");

        public Task<IReadOnlyList<PersonLifecycleAuditEvent>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersonLifecycleAuditEvent>>([]);
    }

    private sealed class DurablyWritesThenThrowsAuditSink(IPersonLifecycleAuditSink inner) : IPersonLifecycleAuditSink
    {
        public async Task WriteAsync(PersonLifecycleAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            throw new InvalidOperationException("synthetic ack failure after a successful durable write");
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);

        public Task<IReadOnlyList<PersonLifecycleAuditEvent>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default) =>
            inner.ListByPersonAsync(tenantId, personId, cancellationToken);
    }

    private sealed class ThrowingRevocationSink : ICredentialAccessRevocationEventSink
    {
        public Task PublishAsync(CredentialAccessRevocationEvent revocationEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");

        public Task<bool> ExistsForSuspensionAsync(string tenantId, string personId, int suspendedAtVersion, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
