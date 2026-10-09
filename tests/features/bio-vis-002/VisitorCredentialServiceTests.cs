using Biolog.Features.VisitorCredentials.Audit;
using Biolog.Features.VisitorCredentials.Domain;
using Biolog.Features.VisitorCredentials.Identifiers;
using Xunit;

namespace Biolog.Features.VisitorCredentials.Tests;

public sealed class VisitorCredentialServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string CorrelationId = "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25";
    private static readonly AuditActor Actor = new(ActorType.User, "security-desk-1");

    // Computed relative to the real clock (not hardcoded literals) so this suite never goes stale.
    private static readonly string VisitExpiresAt = UtcInstant.Render(DateTime.UtcNow.AddHours(8));
    private static readonly string CredentialExpiresAt = UtcInstant.Render(DateTime.UtcNow.AddHours(4));
    private static readonly string CredentialExpiresAtBeyondVisit = UtcInstant.Render(DateTime.UtcNow.AddHours(10));

    private sealed record Harness(
        VisitorCredentialService Service, FakeVisitorRecordGateway VisitorGateway, FakeCardIssuanceGateway CardGateway,
        FaultInjectingVisitorCredentialRepository Repository, InMemoryVisitorCredentialAuditSink AuditSink, FakePermissionGateway PermissionGateway,
        InMemoryStrandedCardLedger StrandedCardLedger, FaultInjectingVisitorCredentialAuditSink AuditSinkFaultInjector);

    private static Harness Build(IReadOnlySet<(string, string)>? grantedPermissions = null)
    {
        var tenantProvider = new FakeCurrentTenantProvider(TenantId);
        // BIO-VIS-002-F01's own remediation: the real in-memory repository, wrapped by a
        // fault-injecting decorator a specific test can arm - transparent (no injected failure)
        // unless a test explicitly sets it.
        var repository = new FaultInjectingVisitorCredentialRepository(new InMemoryVisitorCredentialRepository());
        var visitorGateway = new FakeVisitorRecordGateway();
        var cardGateway = new FakeCardIssuanceGateway();
        var auditSink = new InMemoryVisitorCredentialAuditSink();
        // F07's own remediation test hook: the real sink, wrapped by a fault-injecting decorator a
        // specific test can arm - transparent (no injected failure) unless a test explicitly sets
        // it, same convention as the repository/card-gateway decorators above.
        var auditSinkFaultInjector = new FaultInjectingVisitorCredentialAuditSink(auditSink);
        var lockRegistry = new VisitorCredentialLockRegistry();
        var permissionGateway = new FakePermissionGateway(grantedPermissions);
        var strandedCardLedger = new InMemoryStrandedCardLedger();

        var service = new VisitorCredentialService(tenantProvider, repository, visitorGateway, cardGateway, auditSinkFaultInjector, lockRegistry, permissionGateway, strandedCardLedger);
        return new Harness(service, visitorGateway, cardGateway, repository, auditSink, permissionGateway, strandedCardLedger, auditSinkFaultInjector);
    }

    private static VisitorRecordView CheckedInVisitor(string? personId = PersonId, string? visitExpiresAt = null, string visitorId = "v1") => new()
    {
        Id = visitorId,
        TenantId = TenantId,
        PersonId = personId,
        FullName = "Ada Visitor",
        HostApprovalStatusWire = "approved",
        VisitStatusWire = "checked-in",
        CheckedInAt = UtcInstant.Render(DateTime.UtcNow.AddHours(-1)),
        ExpiresAt = visitExpiresAt ?? VisitExpiresAt,
        CreatedAt = "2026-06-01T08:00:00.000Z",
        Version = 2,
    };

    // --- AC02: grant - expiry and scope, backed by a real card ---

    [Fact]
    public async Task GrantAsync_requires_the_manage_permission()
    {
        var h = Build(grantedPermissions: new HashSet<(string, string)>());
        h.VisitorGateway.Seed(CheckedInVisitor());

        var request = new Api.GrantVisitorCredentialRequest { Scope = "general", ExpiresAt = CredentialExpiresAt };
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.Equal("permission_required", ex.Code);
    }

    [Fact]
    public async Task GrantAsync_fails_when_the_visitor_record_does_not_exist()
    {
        var h = Build();
        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.GrantAsync("missing", request, Actor, CorrelationId));
        Assert.Equal("visitor_not_found", ex.Code);
    }

    [Fact]
    public async Task GrantAsync_fails_when_the_visitor_has_no_linked_person()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(personId: null));

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.Equal("visitor_has_no_linked_person", ex.Code);
        Assert.Equal(0, h.CardGateway.IssueCallCount); // never even attempts BIO-CRD-005's own IssueAsync
    }

    [Fact]
    public async Task GrantAsync_fails_when_the_visitor_is_not_checked_in()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor() with { VisitStatusWire = "preregistered" });

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.Equal("not_checked_in", ex.Code);
    }

    [Fact]
    public async Task GrantAsync_rejects_an_expiry_later_than_the_visits_own_expiry()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitExpiresAt: CredentialExpiresAt));

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAtBeyondVisit };
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.Equal("expires_at_exceeds_visit", ex.Code);
    }

    [Fact]
    public async Task GrantAsync_rejects_an_invalid_scope()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());

        var request = new Api.GrantVisitorCredentialRequest { Scope = "not-a-scope", ExpiresAt = CredentialExpiresAt };
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.Equal("validation_failed", ex.Code);
    }

    [Fact]
    public async Task GrantAsync_rejects_a_second_grant_while_one_is_already_active()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await h.Service.GrantAsync("v1", request, Actor, CorrelationId);

        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.Equal("credential_already_granted", ex.Code);
    }

    [Fact]
    public async Task GrantAsync_issues_and_activates_a_real_backing_card_and_persists_a_credential()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());

        var request = new Api.GrantVisitorCredentialRequest { Scope = "escorted", ExpiresAt = CredentialExpiresAt };
        var credential = await h.Service.GrantAsync("v1", request, Actor, CorrelationId);

        Assert.Equal(VisitorCredentialStatus.Granted, credential.Status);
        Assert.Equal(VisitorCredentialScope.Escorted, credential.Scope);
        Assert.Equal(CredentialExpiresAt, credential.ExpiresAt);
        Assert.Equal(1, h.CardGateway.IssueCallCount);
        Assert.Equal(1, h.CardGateway.ActivateCallCount);

        var backingCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", credential.CardSequenceNumber!.Value);
        Assert.Equal("active", backingCard!.StatusWire);

        var stored = await h.Repository.GetAsync(TenantId, credential.Id);
        Assert.NotNull(stored);
        Assert.Contains(h.AuditSink.All(), e => e.Action == "visitor_credential.granted");
    }

    // --- BIO-VIS-002-F01 remediation: grant failure-injection, Seyi's FAIL on 45f4736 ---

    [Fact]
    public async Task GrantAsync_adopts_an_already_active_card_from_a_prior_partial_failure_instead_of_issuing_a_second_one()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());

        // Simulates the exact stranded state the original bug produced: a real, Active
        // BIO-CRD-005 card for this (person, card class) that no local credential row ever
        // referenced (e.g. a prior attempt's local write failed before this test's harness
        // existed to compensate). A fresh grant must find and adopt it, never mint a second card.
        var strandedIssue = await h.CardGateway.IssueAsync(PersonId, "visitor_temporary", "t", "p", "system", "system", "evt_seed", default);
        await h.CardGateway.ActivateAsync(PersonId, "visitor_temporary", strandedIssue.SequenceNumber, strandedIssue.Version, "seed-key", "system", "system", "evt_seed", default);
        Assert.Equal(1, h.CardGateway.IssueCallCount);

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        var credential = await h.Service.GrantAsync("v1", request, Actor, CorrelationId);

        Assert.Equal(VisitorCredentialStatus.Granted, credential.Status);
        Assert.Equal(strandedIssue.SequenceNumber, credential.CardSequenceNumber);
        Assert.Equal(1, h.CardGateway.IssueCallCount); // never issued a second card
        Assert.Equal(1, h.CardGateway.ActivateCallCount); // the stranded card was already Active - never re-activated
    }

    [Fact]
    public async Task GrantAsync_adopts_and_activates_a_stranded_issued_but_not_yet_active_card()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());

        var strandedIssue = await h.CardGateway.IssueAsync(PersonId, "visitor_temporary", "t", "p", "system", "system", "evt_seed", default);
        Assert.Equal(1, h.CardGateway.IssueCallCount);
        Assert.Equal(0, h.CardGateway.ActivateCallCount);

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        var credential = await h.Service.GrantAsync("v1", request, Actor, CorrelationId);

        Assert.Equal(VisitorCredentialStatus.Granted, credential.Status);
        Assert.Equal(strandedIssue.SequenceNumber, credential.CardSequenceNumber);
        Assert.Equal(1, h.CardGateway.IssueCallCount); // never issued a second card
        Assert.Equal(1, h.CardGateway.ActivateCallCount); // activated the stranded card, exactly once
    }

    [Fact]
    public async Task GrantAsync_when_the_final_local_commit_fails_after_the_card_is_active_the_card_is_compensated_and_the_record_converges_to_failed()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        h.Repository.ThrowOnNextPendingToGrantedReplace = true;

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));

        Assert.Equal(1, h.CardGateway.IssueCallCount);
        Assert.Equal(1, h.CardGateway.ActivateCallCount);
        Assert.Equal(1, h.CardGateway.DeactivateCallCount); // best-effort compensation ran

        var credentials = await h.Repository.ListForVisitorAsync(TenantId, "v1");
        var failed = Assert.Single(credentials);
        Assert.Equal(VisitorCredentialStatus.Failed, failed.Status);
        Assert.NotNull(failed.CardSequenceNumber); // traceable - never left with no reference to the card it attempted
        Assert.Equal("local_commit_failed_after_card_active", failed.FailureReason);

        var backingCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", failed.CardSequenceNumber!.Value);
        Assert.Equal("deactivated", backingCard!.StatusWire); // compensated, not left Active
    }

    [Fact]
    public async Task GrantAsync_a_fresh_attempt_after_a_compensated_failure_succeeds_with_a_new_card()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        h.Repository.ThrowOnNextPendingToGrantedReplace = true;
        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));

        // The first attempt's card is now Deactivated (compensated) - a fresh attempt is not
        // blocked by the Failed local row (only Granted rows block a new grant), and correctly
        // issues a NEW card rather than trying to reuse a dead one.
        var credential = await h.Service.GrantAsync("v1", request, Actor, CorrelationId);

        Assert.Equal(VisitorCredentialStatus.Granted, credential.Status);
        Assert.Equal(2, h.CardGateway.IssueCallCount);
        Assert.Equal(2, h.CardGateway.ActivateCallCount);
    }

    [Fact]
    public async Task GrantAsync_never_calls_issue_when_the_visitor_has_no_linked_person_even_with_a_stranded_card_present()
    {
        // Regression guard: the new pre-issue ListAsync check must never run ahead of the
        // existing PersonId precondition.
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(personId: null));

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.Equal("visitor_has_no_linked_person", ex.Code);
        Assert.Equal(0, h.CardGateway.IssueCallCount);
    }

    [Fact]
    public async Task GrantAsync_when_cancelled_right_after_activation_still_compensates_and_converges_to_failed()
    {
        // BIO-VIS-002-F03 (Zainab's round-2 FAIL): the OLD code's catch-block recovery (the
        // reread, and MarkFailedBestEffortAsync's own card lookup/deactivate) reused the
        // caller's own cancellation token. Cancel the token from INSIDE the fake card gateway's
        // IssueAsync hook - i.e. genuinely AFTER the person-scoped lock is already held and a
        // card is about to be issued/activated, not before (an already-cancelled token passed
        // in from the start would just fail at the unrelated lock-acquire check and never reach
        // this scenario at all). Combined with the same final-commit fault injection as the test
        // above: under the OLD code, the catch block's own `reread` call would throw
        // OperationCanceledException from reusing the now-cancelled token, escaping before
        // MarkFailedBestEffortAsync ever ran - the card would be left genuinely Active with the
        // local row stuck Pending forever. The fix (CancellationToken.None throughout the
        // recovery path) must converge to the identical Failed/compensated outcome regardless.
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        h.Repository.ThrowOnNextPendingToGrantedReplace = true;

        using var cts = new CancellationTokenSource();
        h.CardGateway.BeforeFirstIssueAsync = () =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        };

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId, cts.Token));

        Assert.Equal(1, h.CardGateway.IssueCallCount);
        Assert.Equal(1, h.CardGateway.ActivateCallCount);
        Assert.Equal(1, h.CardGateway.DeactivateCallCount); // compensation ran despite the cancelled token

        var credentials = await h.Repository.ListForVisitorAsync(TenantId, "v1");
        var failed = Assert.Single(credentials);
        Assert.Equal(VisitorCredentialStatus.Failed, failed.Status); // converged, not stuck Pending
        Assert.NotNull(failed.CardSequenceNumber);

        var backingCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", failed.CardSequenceNumber!.Value);
        Assert.Equal("deactivated", backingCard!.StatusWire);
    }

    [Fact]
    public async Task GrantAsync_two_visitor_records_for_the_same_person_sequentially_never_share_a_card_and_revoking_one_never_touches_the_other()
    {
        // BIO-VIS-002-F03 (Seyi's round-2 FAIL), sequential reproduction: person P has two
        // checked-in visitor records V1 and V2 (two separate visits - entirely legitimate,
        // nothing in this story forbids it). V1's grant must not leave V2 able to adopt the
        // same card, and revoking V1's credential must never deactivate V2's own card.
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v2"));
        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };

        var c1 = await h.Service.GrantAsync("v1", request, Actor, CorrelationId);
        var c2 = await h.Service.GrantAsync("v2", request, Actor, CorrelationId);

        Assert.Equal(2, h.CardGateway.IssueCallCount); // never adopted V1's card for V2 - a genuinely new one was issued
        Assert.NotEqual(c1.CardSequenceNumber, c2.CardSequenceNumber);

        await h.Service.RevokeAsync(c1.Id, new Api.RevokeVisitorCredentialRequest { Version = c1.Version, Reason = "lost badge" }, Actor, CorrelationId);

        var v2Card = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", c2.CardSequenceNumber!.Value);
        Assert.Equal("active", v2Card!.StatusWire); // V2's own card is untouched by V1's revoke
    }

    [Fact]
    public async Task GrantAsync_concurrent_grants_for_two_visitor_records_of_the_same_person_never_race_into_sharing_a_card()
    {
        // BIO-VIS-002-F03 (Seyi's round-2 FAIL), concurrent reproduction: the sequential test
        // above proves the ownership check alone, but Seyi specifically flagged that the SAME
        // bug can also race concurrently, because two different visitor records' own
        // per-visitor locks never contend. This test forces a genuine async suspension point
        // mid-way through V1's grant (still holding whatever lock it acquired), then proves V2's
        // own grant attempt for the SAME person cannot even start issuing a card until V1 fully
        // releases - deterministic proof the lock is now keyed by PERSON, not by visitor id (if
        // it were still keyed by visitor id, V2 would race ahead immediately, uncontended,
        // because "v1" and "v2" are different lock keys).
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v2"));
        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };

        var gate = new TaskCompletionSource();
        h.CardGateway.BeforeFirstIssueAsync = () => gate.Task; // V1 pauses here, mid-flight, still holding the (TenantId, PersonId) lock

        var grant1 = h.Service.GrantAsync("v1", request, Actor, CorrelationId);
        var grant2 = h.Service.GrantAsync("v2", request, Actor, CorrelationId);

        // Deterministic, not timing-based: V1 is paused inside IssueAsync (so nothing has
        // consumed the gate yet) and still holds the person lock; V2's own grant must therefore
        // still be blocked trying to acquire that SAME lock, not racing ahead to its own
        // ListAsync/IssueAsync call.
        Assert.False(grant2.IsCompleted);

        gate.SetResult();
        var results = await Task.WhenAll(grant1, grant2);

        Assert.Equal(2, h.CardGateway.IssueCallCount); // each visitor ended up with its OWN card - no sharing
        Assert.NotEqual(results[0].CardSequenceNumber, results[1].CardSequenceNumber);
        Assert.All(results, c => Assert.Equal(VisitorCredentialStatus.Granted, c.Status));
    }

    // --- BIO-VIS-002-F04 remediation: the genuinely pathological stranded-card backstop, Seyi's round-3 FAIL ---

    [Fact]
    public async Task GrantAsync_when_both_local_persistence_and_compensation_are_exhausted_the_card_is_recorded_stranded_and_a_later_visitor_cannot_adopt_it()
    {
        // Reproduces exactly the scenario evidence/BIO-VIS-002/codex/REVIEW-Seyi-round3.md demands:
        // V1's card becomes genuinely Active, but NEITHER the early card-correlation attach NOR
        // the later Pending->Failed fallback can be persisted locally, AND the compensating
        // deactivate also fails - the local store ends up with a Pending row that still has a
        // null CardSequenceNumber, so FindByCardAsync(..., S) returns null for the genuinely
        // Active card S. Without the stranded-card ledger, a later grant for a DIFFERENT visitor
        // of the SAME person would see S as unowned and adopt it.
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v2"));
        h.Repository.AlwaysThrowOnPendingCardAttach = true;
        h.CardGateway.AlwaysThrowOnDeactivate = true;

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));

        // V1's own local bookkeeping genuinely never converged - this is the disclosed boundary,
        // not a hidden one: no credential row for v1 exists at all (CreateAsync's own Pending
        // insert is the only write that ever landed), and the card itself is still genuinely
        // Active, never deactivated.
        var v1Credentials = await h.Repository.ListForVisitorAsync(TenantId, "v1");
        var stuckPending = Assert.Single(v1Credentials);
        Assert.Equal(VisitorCredentialStatus.Pending, stuckPending.Status);
        Assert.Null(stuckPending.CardSequenceNumber);
        var strandedCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", 1);
        Assert.Equal("active", strandedCard!.StatusWire);

        // The stranded-card ledger - not FindByCardAsync, which would wrongly say "unowned" here -
        // is what must now refuse adoption.
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));
        Assert.Null(await h.Repository.FindByCardAsync(TenantId, PersonId, "visitor_temporary", 1)); // confirms this is the exact ambiguity the ledger exists for

        // Clear the injected faults - V2's own grant attempt must succeed normally, with its OWN
        // genuinely new card, never adopting the stranded one.
        h.Repository.AlwaysThrowOnPendingCardAttach = false;
        h.CardGateway.AlwaysThrowOnDeactivate = false;

        var c2 = await h.Service.GrantAsync("v2", request, Actor, CorrelationId);

        Assert.Equal(2, h.CardGateway.IssueCallCount); // a genuinely new card was issued for v2 - card 1 was never adopted
        Assert.NotEqual(1, c2.CardSequenceNumber);
        Assert.Equal(VisitorCredentialStatus.Granted, c2.Status);
    }

    // --- Post-review remediation round 5 (BIO-VIS-002-F05, Seyi round-4 FAIL / Zainab round-3 FAIL) ---

    [Fact]
    public async Task ReconcileStrandedCardsAsync_deactivates_a_ledgered_card_once_the_card_service_recovers_and_the_ledger_entry_converges_to_resolved()
    {
        // Reproduces the exact pathological state the F04 test above leaves behind, then proves
        // F05's own remediation: once BIO-CRD-005 (the card gateway) recovers, reconciliation
        // actually neutralizes the stranded card - not merely excludes it from adoption.
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.Repository.AlwaysThrowOnPendingCardAttach = true;
        h.CardGateway.AlwaysThrowOnDeactivate = true;

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));

        var strandedCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", 1);
        Assert.Equal("active", strandedCard!.StatusWire); // still genuinely active - this is the F05 hazard itself
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));

        // "The card service becomes available" - clear the injected faults.
        h.Repository.AlwaysThrowOnPendingCardAttach = false;
        h.CardGateway.AlwaysThrowOnDeactivate = false;

        await h.Service.ReconcileStrandedCardsAsync(TenantId, PersonId, "visitor_temporary", Actor, CorrelationId);

        var reconciledCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", 1);
        Assert.Equal("deactivated", reconciledCard!.StatusWire); // genuinely neutralized, not just excluded from adoption
        Assert.False(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1)); // converged to resolved
    }

    [Fact]
    public async Task GrantAsync_opportunistically_reconciles_this_persons_own_stranded_cards_before_proceeding()
    {
        // Proves the actual wired trigger, not just the standalone method: a SUBSEQUENT GrantAsync
        // call for a different visitor of the same person self-heals the first visitor's own
        // stranded card, without anyone needing to invoke reconciliation explicitly.
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v2"));
        h.Repository.AlwaysThrowOnPendingCardAttach = true;
        h.CardGateway.AlwaysThrowOnDeactivate = true;

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));

        h.Repository.AlwaysThrowOnPendingCardAttach = false;
        h.CardGateway.AlwaysThrowOnDeactivate = false;

        await h.Service.GrantAsync("v2", request, Actor, CorrelationId); // never calls ReconcileStrandedCardsAsync directly

        var card1 = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", 1);
        Assert.Equal("deactivated", card1!.StatusWire); // reconciled as a side effect of v2's own grant
        Assert.False(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));
    }

    [Fact]
    public async Task ReconcileStrandedCardsAsync_leaves_an_entry_unresolved_and_retries_next_time_while_deactivation_keeps_failing()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.Repository.AlwaysThrowOnPendingCardAttach = true;
        h.CardGateway.AlwaysThrowOnDeactivate = true;

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));

        // The dependency is still down - reconciliation must not throw out to the caller, and the
        // entry must still be reported stranded afterward (retryable, not abandoned).
        await h.Service.ReconcileStrandedCardsAsync(TenantId, PersonId, "visitor_temporary", Actor, CorrelationId);
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));

        var stillActiveCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", 1);
        Assert.Equal("active", stillActiveCard!.StatusWire);
    }

    // --- Post-review remediation round 6 (BIO-VIS-002-F06, Seyi round-5 FAIL / Zainab round-4 FAIL) ---

    [Fact]
    public async Task GrantAsync_blocks_issuing_a_new_card_while_an_unresolved_stranded_card_remains_for_the_same_person()
    {
        // BIO-VIS-002-F06 (Zainab's round-4 FAIL): reproduce F05's exact triple-failure state for
        // v1 (card active, stranded, unresolved), then grant for a SECOND visitor of the SAME
        // person WHILE the card service is still down - the opportunistic reconciliation attempt
        // inside this very call also fails, and the fix must fail closed rather than let v2 end up
        // with a brand new card on top of the still-active, unaccounted-for one.
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v2"));
        h.Repository.AlwaysThrowOnPendingCardAttach = true;
        h.CardGateway.AlwaysThrowOnDeactivate = true;

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));

        // Card service is STILL down - v2's own opportunistic reconciliation attempt fails too.
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.GrantAsync("v2", request, Actor, CorrelationId));
        Assert.Equal("stranded_card_unresolved", ex.Code);

        Assert.Equal(1, h.CardGateway.IssueCallCount); // no second card was ever issued for v2
        Assert.Equal(1, h.CardGateway.ActivateCallCount);
    }

    [Fact]
    public async Task ReconcileStrandedCardsForPersonAsync_neutralizes_a_stranded_card_without_any_grant_checkout_or_revoke()
    {
        // BIO-VIS-002-F06 (Seyi's round-5 FAIL): the guaranteed-availability operator-recovery
        // trigger, proven to work completely independently of any grant/checkout/revoke for this
        // person - the exact gap Seyi's review named ("a person who never returns").
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.Repository.AlwaysThrowOnPendingCardAttach = true;
        h.CardGateway.AlwaysThrowOnDeactivate = true;

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));

        h.CardGateway.AlwaysThrowOnDeactivate = false; // "the dependency becomes available"

        var result = await h.Service.ReconcileStrandedCardsForPersonAsync(PersonId, Actor, CorrelationId);

        Assert.Contains(1, result.Resolved);
        Assert.Empty(result.StillUnresolved);
        var card = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", 1);
        Assert.Equal("deactivated", card!.StatusWire);
        Assert.False(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));
    }

    [Fact]
    public async Task ReconcileStrandedCardsForPersonAsync_requires_the_manage_permission()
    {
        var h = Build(grantedPermissions: new HashSet<(string, string)>());
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.ReconcileStrandedCardsForPersonAsync(PersonId, Actor, CorrelationId));
        Assert.Equal("permission_required", ex.Code);
    }

    [Fact]
    public async Task CheckOutAsync_opportunistically_reconciles_this_persons_own_stranded_cards_too()
    {
        // Proves the widened trigger (a bonus over the operator endpoint, not the primary fix):
        // v2 never requested a credential at all - checking it out is an unrelated action for the
        // SAME person as v1's stranded card, and must still trigger reconciliation as a side
        // effect, exactly like GrantAsync already does.
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v2"));
        h.Repository.AlwaysThrowOnPendingCardAttach = true;
        h.CardGateway.AlwaysThrowOnDeactivate = true;

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));

        h.CardGateway.AlwaysThrowOnDeactivate = false;

        await h.Service.CheckOutAsync("v2", expectedVersion: 2, Actor, CorrelationId);

        var card = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", 1);
        Assert.Equal("deactivated", card!.StatusWire);
        Assert.False(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));
    }

    // --- Post-review remediation round 7 (BIO-VIS-002-F07, Seyi round-7 FAIL / Zainab round-5 FAIL) ---

    [Fact]
    public async Task ReconcileStrandedCardsAsync_when_the_audit_write_fails_after_deactivation_succeeds_a_retry_writes_exactly_one_audit_record_and_the_ledger_converges_to_resolved()
    {
        // Reproduces F07 exactly: attempt 1 deactivates the card successfully but the audit write
        // then fails (a real failure, not ambiguous - ExistsAsync genuinely returns false). The
        // ledger must stay unresolved and no audit record must exist yet. Attempt 2 (the card is
        // now already inactive) must still write the audit record before resolving - never skip it
        // just because "nothing is left to neutralize" - and must never deactivate a second time.
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.Repository.AlwaysThrowOnPendingCardAttach = true;
        h.CardGateway.AlwaysThrowOnDeactivate = true;

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));

        // The card service recovers, but the audit sink does not - attempt 1.
        h.Repository.AlwaysThrowOnPendingCardAttach = false;
        h.CardGateway.AlwaysThrowOnDeactivate = false;
        h.AuditSinkFaultInjector.ThrowOnNextWrite = true;

        await h.Service.ReconcileStrandedCardsAsync(TenantId, PersonId, "visitor_temporary", Actor, CorrelationId);

        var afterAttempt1 = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", 1);
        Assert.Equal("deactivated", afterAttempt1!.StatusWire); // deactivation itself DID succeed
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1)); // still unresolved - audit never confirmed
        Assert.DoesNotContain(h.AuditSink.All(), e => e.Action == "visitor_credential.stranded_card_reconciled"); // F07's exact gap: no audit record at all

        // GrantAsync's own failed compensating deactivate attempt (card service still down at that
        // point) already incremented this once; attempt 1 above incremented it again on the
        // genuine deactivation. Captured here, after deactivation is already known to be done, so
        // attempt 2 below can assert it never deactivates a SECOND time on top of that.
        var deactivateCallCountAfterAttempt1 = h.CardGateway.DeactivateCallCount;

        // The audit sink recovers too - attempt 2. The card is already inactive this time.
        await h.Service.ReconcileStrandedCardsAsync(TenantId, PersonId, "visitor_temporary", Actor, CorrelationId);

        Assert.False(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1)); // now genuinely resolved
        Assert.Single(h.AuditSink.All(), e => e.Action == "visitor_credential.stranded_card_reconciled"); // exactly one audit record, never duplicated
        Assert.Equal(deactivateCallCountAfterAttempt1, h.CardGateway.DeactivateCallCount); // never deactivated a second time
    }

    // --- Post-review remediation round 8 (BIO-VIS-002-F08, Seyi round-8 FAIL / Zainab round-6 FAIL) ---

    [Fact]
    public async Task CreateIfAbsentAsync_two_genuinely_concurrent_writers_for_the_same_event_id_converge_to_exactly_one_immutable_payload()
    {
        // F08's exact gap, proven at the sink's own boundary - and F09's own remediation (Seyi
        // round-9 FAIL) on top of it: a single start-of-task signal isn't enough, because the
        // scheduler can still run caller A fully through CreateIfAbsentAsync before caller B even
        // starts, letting the OLD defective ExistsAsync-then-WriteAsync sequence pass by accident.
        // BarrierGatedVisitorCredentialAuditSink closes that: both callers block at the real create
        // boundary itself until BOTH have arrived, then are released together - no slack for the
        // scheduler to serialize them. Asserts an invariant that must hold for EVERY interleaving:
        // exactly one payload is ever durably stored, both callers observe that SAME winning
        // object, and the losing caller's own correlation id is never subsequently observable.
        var innerSink = new InMemoryVisitorCredentialAuditSink();
        var barrier = new Barrier(2);
        var sink = new BarrierGatedVisitorCredentialAuditSink(innerSink, barrier);
        var eventId = CanonicalId.NewEventId();
        var correlationIdFromCallerA = CorrelationId;
        var correlationIdFromCallerB = CanonicalId.NewEventId();

        VisitorCredentialAuditEvent BuildEvent(string correlationId) => VisitorCredentialAuditEventBuilder.Build(
            TenantId, Actor, "visitor_credential.stranded_card_reconciled", new AuditTarget("visitor_credential", $"{PersonId}:visitor_temporary:1"),
            AuditOutcome.Allowed, "stranded_card_reconciled", correlationId, null,
            new Dictionary<string, (object?, string)>()) with { EventId = eventId };

        var eventFromCallerA = BuildEvent(correlationIdFromCallerA);
        var eventFromCallerB = BuildEvent(correlationIdFromCallerB);

        var taskA = Task.Run(() => sink.CreateIfAbsentAsync(eventFromCallerA));
        var taskB = Task.Run(() => sink.CreateIfAbsentAsync(eventFromCallerB));
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Same(results[0], results[1]); // both callers observe the exact same winning object
        Assert.Equal(results[0].CorrelationId, results[1].CorrelationId);
        Assert.Single(innerSink.All(), e => e.EventId == eventId); // exactly one durable record, never two, never silently replaced

        var losingCorrelationId = results[0].CorrelationId == correlationIdFromCallerA ? correlationIdFromCallerB : correlationIdFromCallerA;
        Assert.DoesNotContain(innerSink.All(), e => e.CorrelationId == losingCorrelationId); // the losing payload is never subsequently observable
    }

    [Fact]
    public async Task ReconcileStrandedCardsAsync_two_concurrent_callers_for_the_same_stranded_card_converge_to_exactly_one_audit_record()
    {
        // F08's real-world shape: ReconcileStrandedCardsAsync itself holds no lock - its various
        // callers (grant/the operator endpoint vs. checkout/revoke) hold DIFFERENT locks that do
        // not contend with each other, so two legitimate concurrent triggers for the SAME stranded
        // card is a genuine, reachable race, not a hypothetical. F09's own remediation (Seyi
        // round-9 FAIL): a single start-of-task signal isn't a real barrier - the scheduler could
        // still run caller A fully through reconciliation's audit write before caller B even
        // starts. A second service instance, identical except its audit sink is wrapped in
        // BarrierGatedVisitorCredentialAuditSink, forces both callers to reach the real contested
        // create call back-to-back instead.
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor(visitorId: "v1"));
        h.Repository.AlwaysThrowOnPendingCardAttach = true;
        h.CardGateway.AlwaysThrowOnDeactivate = true;

        var request = new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.GrantAsync("v1", request, Actor, CorrelationId));
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1));

        h.Repository.AlwaysThrowOnPendingCardAttach = false;
        h.CardGateway.AlwaysThrowOnDeactivate = false;

        // Deactivate the stranded card ONCE, single-threaded, BEFORE racing - reproducing the
        // exact documented F07/F08 scenario (a prior attempt already neutralized the card but
        // never got as far as a durable audit record) and deliberately keeping the race confined
        // to the one boundary this test is actually about. FakeCardIssuanceGateway's backing
        // Dictionary is not thread-safe and DeactivateAsync is version-checked - letting two
        // concurrent callers race the deactivate step itself (rather than just the audit write)
        // would make this test's outcome depend on an unrelated fake-infrastructure race and could
        // leave one caller short-circuited by a concurrency-conflict exception before it ever
        // reaches the barrier, hanging the other caller on it forever.
        var strandedCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", 1) ?? throw new InvalidOperationException("test setup: stranded card must exist");
        Assert.Equal("active", strandedCard.StatusWire);
        await h.CardGateway.DeactivateAsync(PersonId, "visitor_temporary", 1, strandedCard.Version, "test-setup", "system", CanonicalId.NewEventId());
        Assert.True(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1)); // ledger entry deliberately still unresolved - only the card itself was neutralized

        var barrier = new Barrier(2);
        var barrierGatedSink = new BarrierGatedVisitorCredentialAuditSink(h.AuditSink, barrier);
        var racingService = new VisitorCredentialService(
            new FakeCurrentTenantProvider(TenantId), h.Repository, h.VisitorGateway, h.CardGateway,
            barrierGatedSink, new VisitorCredentialLockRegistry(), h.PermissionGateway, h.StrandedCardLedger);

        var operatorActor = Actor;
        var checkoutActor = new AuditActor(ActorType.System, "visitor-checkout-orchestrator");
        var checkoutCorrelationId = CanonicalId.NewEventId();

        var operatorTask = Task.Run(() => racingService.ReconcileStrandedCardsAsync(TenantId, PersonId, "visitor_temporary", operatorActor, CorrelationId));
        var checkoutTask = Task.Run(() => racingService.ReconcileStrandedCardsAsync(TenantId, PersonId, "visitor_temporary", checkoutActor, checkoutCorrelationId));
        await Task.WhenAll(operatorTask, checkoutTask);

        Assert.False(await h.StrandedCardLedger.IsStrandedAsync(TenantId, PersonId, "visitor_temporary", 1)); // resolved only after the atomic result is durable
        var survivingRecords = h.AuditSink.All().Where(e => e.Action == "visitor_credential.stranded_card_reconciled").ToList();
        Assert.Single(survivingRecords); // exactly one record survives the race, never two, never clobbered
        Assert.Contains(survivingRecords[0].CorrelationId, new[] { CorrelationId, checkoutCorrelationId }); // the surviving record is one caller's own, not a corrupted mix
    }

    // --- Post-review remediation round 10 (BIO-VIS-002-F09 continued, Seyi round-10 FAIL: mutation/differential proof) ---

    [Fact]
    public async Task CreateIfAbsent_mutation_proof_the_atomic_GetOrAdd_factory_based_sink_survives_forced_concurrent_factory_invocation()
    {
        // F09's final remediation, exactly as directed: the barrier lives INSIDE the factory
        // delegate passed to ConcurrentDictionary.GetOrAdd, not merely before delegating to it.
        // Under genuine two-thread contention .NET's documented contract allows this factory to be
        // invoked on BOTH threads before one result wins - placing the barrier here forces exactly
        // that interleaving (both factories must be invoked - i.e. both "observed absent" - before
        // either can return), which is the real contested sequence a plain pre-call barrier cannot
        // reach. Despite that, the atomic sink must still converge to exactly one winning payload.
        var factoryBarrier = new Barrier(2);
        var sink = new AtomicFactoryRaceSink(factoryBarrier);
        var eventId = CanonicalId.NewEventId();
        var candidateA = BuildReconciliationAuditEvent(eventId, CorrelationId);
        var candidateB = BuildReconciliationAuditEvent(eventId, CanonicalId.NewEventId());

        var taskA = Task.Run(() => sink.CreateIfAbsent(eventId, candidateA));
        var taskB = Task.Run(() => sink.CreateIfAbsent(eventId, candidateB));
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Same(results[0], results[1]); // both callers observe the exact same winning object, even though both factories ran
        var winner = sink.Get(eventId);
        Assert.NotNull(winner);
        Assert.True(winner!.CorrelationId == candidateA.CorrelationId || winner.CorrelationId == candidateB.CorrelationId); // the winner is one caller's genuine, uncorrupted payload
    }

    [Fact]
    public async Task CreateIfAbsent_mutation_proof_the_historical_check_then_write_algorithm_violates_immutability_once_both_callers_observe_absent()
    {
        // The other half of the mutation proof: this test MUST fail its own final assertion if run
        // against the atomic sink above, and MUST pass it here, against a deliberately reinstated
        // copy of the exact historical bug (ExistsAsync observed absent by both callers, then a
        // plain unconditional overwrite) - proving this methodology has genuine discriminating
        // power, not merely that the real implementation happens to behave. Both callers complete
        // their own absence observation (against an empty store, so both legitimately see
        // "absent") and are released together via checkBarrier; a semaphore pair then forces
        // caller B's write to land strictly after caller A's already-completed write, reproducing
        // "both observed absent, then first A writes, then B writes" without re-introducing an
        // unrelated race into the proof itself.
        var checkBarrier = new Barrier(2);
        var sink = new RacyCheckThenWriteSink(checkBarrier);
        var eventId = CanonicalId.NewEventId();
        var candidateA = BuildReconciliationAuditEvent(eventId, CorrelationId);
        var candidateB = BuildReconciliationAuditEvent(eventId, CanonicalId.NewEventId());
        var aHasWritten = new SemaphoreSlim(0, 1);

        var taskA = Task.Run(() => sink.CreateIfAbsent(eventId, candidateA, waitBeforeWrite: null, signalAfterWrite: aHasWritten));
        var taskB = Task.Run(() => sink.CreateIfAbsent(eventId, candidateB, waitBeforeWrite: aHasWritten, signalAfterWrite: null));
        await Task.WhenAll(taskA, taskB);

        var stored = sink.Get(eventId);
        Assert.NotNull(stored);
        Assert.Equal(candidateB.CorrelationId, stored!.CorrelationId); // B's later write silently clobbered A's
        Assert.NotEqual(candidateA.CorrelationId, stored.CorrelationId); // A's payload - though it legitimately observed "absent" first - is permanently lost, proving the historical bug this whole chain fixed
    }

    private static VisitorCredentialAuditEvent BuildReconciliationAuditEvent(string eventId, string correlationId) =>
        VisitorCredentialAuditEventBuilder.Build(
            TenantId, Actor, "visitor_credential.stranded_card_reconciled", new AuditTarget("visitor_credential", $"{PersonId}:visitor_temporary:1"),
            AuditOutcome.Allowed, "stranded_card_reconciled", correlationId, null,
            new Dictionary<string, (object?, string)>()) with { EventId = eventId };

    // --- AC03: revoke - explicit and automatic (checkout) ---

    [Fact]
    public async Task RevokeAsync_requires_the_manage_permission()
    {
        var h = Build(grantedPermissions: new HashSet<(string, string)>());
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() =>
            h.Service.RevokeAsync("missing", new Api.RevokeVisitorCredentialRequest { Version = 1, Reason = "lost badge" }, Actor, CorrelationId));
        Assert.Equal("permission_required", ex.Code);
    }

    [Fact]
    public async Task RevokeAsync_requires_a_reason()
    {
        var h = Build();
        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() =>
            h.Service.RevokeAsync("missing", new Api.RevokeVisitorCredentialRequest { Version = 1, Reason = "" }, Actor, CorrelationId));
        Assert.Equal("validation_failed", ex.Code);
    }

    [Fact]
    public async Task RevokeAsync_deactivates_the_backing_card_and_moves_the_credential_to_revoked()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        var granted = await h.Service.GrantAsync("v1", new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt }, Actor, CorrelationId);

        var revoked = await h.Service.RevokeAsync(
            granted.Id, new Api.RevokeVisitorCredentialRequest { Version = granted.Version, Reason = "badge reported lost" }, Actor, CorrelationId);

        Assert.Equal(VisitorCredentialStatus.Revoked, revoked.Status);
        Assert.Equal("badge reported lost", revoked.RevokeReason);
        var backingCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", revoked!.CardSequenceNumber!.Value);
        Assert.Equal("deactivated", backingCard!.StatusWire);
        Assert.Contains(h.AuditSink.All(), e => e.Action == "visitor_credential.revoked");
    }

    [Fact]
    public async Task RevokeAsync_rejects_a_stale_version()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        var granted = await h.Service.GrantAsync("v1", new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt }, Actor, CorrelationId);

        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() =>
            h.Service.RevokeAsync(granted.Id, new Api.RevokeVisitorCredentialRequest { Version = granted.Version + 1, Reason = "x" }, Actor, CorrelationId));
        Assert.Equal("concurrency_conflict", ex.Code);
    }

    // --- AC03: checkout orchestration - calls through to BIO-VIS-001's own checkout, then revokes ---

    [Fact]
    public async Task CheckOutAsync_calls_through_to_the_real_visitor_checkout()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());

        var (visitor, revoked) = await h.Service.CheckOutAsync("v1", 2, Actor, CorrelationId);

        Assert.Equal("checked-out", visitor.VisitStatusWire);
        Assert.NotNull(visitor.CheckedOutAt);
        Assert.Null(revoked); // nothing was granted
    }

    [Fact]
    public async Task CheckOutAsync_revokes_an_active_credential_and_deactivates_its_backing_card()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        var granted = await h.Service.GrantAsync("v1", new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt }, Actor, CorrelationId);

        var (visitor, revoked) = await h.Service.CheckOutAsync("v1", 2, Actor, CorrelationId);

        Assert.Equal("checked-out", visitor.VisitStatusWire);
        Assert.NotNull(revoked);
        Assert.Equal(granted.Id, revoked!.Id);
        Assert.Equal(VisitorCredentialStatus.Revoked, revoked.Status);
        Assert.Equal("visitor_checked_out", revoked.RevokeReason);
        var backingCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", revoked!.CardSequenceNumber!.Value);
        Assert.Equal("deactivated", backingCard!.StatusWire);
    }

    [Fact]
    public async Task CheckOutAsync_propagates_the_underlying_visitor_checkout_failure_as_a_mapped_error()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor() with { VisitStatusWire = "preregistered" }); // never checked in

        var ex = await Assert.ThrowsAsync<VisitorCredentialError>(() => h.Service.CheckOutAsync("v1", 2, Actor, CorrelationId));
        Assert.Equal("not_checked_in", ex.Code);
    }

    [Fact]
    public async Task CheckOutAsync_never_requires_the_grant_revoke_permission_it_is_audit_attribution_only()
    {
        var h = Build(grantedPermissions: new HashSet<(string, string)>()); // actor holds NO permissions
        h.VisitorGateway.Seed(CheckedInVisitor());

        var (visitor, _) = await h.Service.CheckOutAsync("v1", 2, Actor, CorrelationId);
        Assert.Equal("checked-out", visitor.VisitStatusWire);
    }

    // --- BIO-VIS-002-F02 remediation: checkout failure-injection, Seyi's FAIL on 45f4736 ---

    [Fact]
    public async Task CheckOutAsync_when_revoke_fails_the_underlying_visitor_checkout_is_never_called_and_the_visitor_stays_checked_in()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        await h.Service.GrantAsync("v1", new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt }, Actor, CorrelationId);
        h.CardGateway.ThrowOnNextDeactivate = true;

        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.CheckOutAsync("v1", 2, Actor, CorrelationId));

        Assert.Equal(0, h.VisitorGateway.CheckOutCallCount); // the one-way BIO-VIS-001 checkout was never reached
        var visitor = await h.VisitorGateway.GetAsync("v1");
        Assert.Equal("checked-in", visitor!.VisitStatusWire); // safe, fail-closed: still has access, not yet marked departed
    }

    [Fact]
    public async Task CheckOutAsync_retried_after_the_revoke_failure_is_fixed_succeeds_and_reaches_a_consistent_terminal_state()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        var granted = await h.Service.GrantAsync("v1", new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt }, Actor, CorrelationId);
        h.CardGateway.ThrowOnNextDeactivate = true;
        await Assert.ThrowsAnyAsync<Exception>(() => h.Service.CheckOutAsync("v1", 2, Actor, CorrelationId));

        var (visitor, revoked) = await h.Service.CheckOutAsync("v1", 2, Actor, CorrelationId);

        Assert.Equal("checked-out", visitor.VisitStatusWire);
        Assert.Equal(1, h.VisitorGateway.CheckOutCallCount); // the one-way checkout only ever ran once, on the successful retry
        Assert.NotNull(revoked);
        Assert.Equal(granted.Id, revoked!.Id);
        Assert.Equal(VisitorCredentialStatus.Revoked, revoked.Status);
        var backingCard = await h.CardGateway.GetAsync(PersonId, "visitor_temporary", revoked.CardSequenceNumber!.Value);
        Assert.Equal("deactivated", backingCard!.StatusWire);
    }

    [Fact]
    public async Task CheckOutAsync_called_again_after_the_visitor_is_already_checked_out_is_an_idempotent_no_op()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        await h.Service.CheckOutAsync("v1", 2, Actor, CorrelationId); // no credential involved
        Assert.Equal(1, h.VisitorGateway.CheckOutCallCount);

        var (visitor, revoked) = await h.Service.CheckOutAsync("v1", 2, Actor, CorrelationId);

        Assert.Equal("checked-out", visitor.VisitStatusWire);
        Assert.Null(revoked);
        Assert.Equal(1, h.VisitorGateway.CheckOutCallCount); // the retry never called BIO-VIS-001's own one-way checkout again
    }

    // --- AC05: history ---

    [Fact]
    public async Task GetHistoryAsync_returns_the_visitor_lifecycle_and_every_credential_ever_granted()
    {
        var h = Build();
        h.VisitorGateway.Seed(CheckedInVisitor());
        var granted = await h.Service.GrantAsync("v1", new Api.GrantVisitorCredentialRequest { ExpiresAt = CredentialExpiresAt }, Actor, CorrelationId);
        await h.Service.CheckOutAsync("v1", 2, Actor, CorrelationId);

        var (visitor, credentials) = await h.Service.GetHistoryAsync("v1");

        Assert.Equal("checked-out", visitor.VisitStatusWire);
        Assert.Single(credentials);
        Assert.Equal(granted.Id, credentials[0].Id);
        Assert.Equal(VisitorCredentialStatus.Revoked, credentials[0].Status);
    }
}
