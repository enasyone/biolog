// Developer tests for BIO-CRD-005. SYNTHETIC fixtures only.
//
// Regression coverage for BIO-CRD-005-QA-01 (Chioma's acceptance review round 1): the original
// IssueAsync committed the card-issue row via a single AppendAsync call BEFORE calling
// AuditAsync, so a confirmed audit failure still left an unaudited row committed - the exact
// ADR-0003 Decision 7 "commit before audit" violation the pattern exists to prevent. Fixed by
// splitting the repository call into NextSequenceNumber (a pure read) and Create (the commit),
// with AuditAsync run in between - see CardIssueService.IssueAsync's own comment. This file
// proves the fix directly at the service level, the same shape as the acceptance evidence's
// own audit-failure-probe script (evidence/BIO-CRD-005/acceptance/audit-failure-probe/Program.cs),
// plus confirms every other mutation already held the same discipline.
using Biolog.Features.CardIssuance.Audit;
using Biolog.Features.CardIssuance.Domain;
using Biolog.Features.CardIssuance.Identifiers;
using Biolog.Features.CardIssuance.Tenancy;
using Xunit;

namespace Biolog.Features.CardIssuance.Tests;

public sealed class CardIssueAuditAtomicityTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private sealed class AlwaysFailingAuditSink : ICardIssueAuditSink
    {
        public Task WriteAsync(CardIssueAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("audit offline");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false); // confirms the write genuinely never persisted, not merely ambiguous
    }

    private static (CardIssueService Service, InMemoryCardIssueRepository Repository) BuildServiceWithFailingAudit()
    {
        var repository = new InMemoryCardIssueRepository();
        var service = new CardIssueService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(),
            new AlwaysFailingAuditSink(), new CardIssueLockRegistry(), new InMemoryIdempotencyStore());
        return (service, repository);
    }

    private static AuditActor Operator() => new(ActorType.User, "operator-1");

    private static IssueCardRequest IssueRequest() => new() { TemplateRef = "template-1", PrinterRef = "printer-1" };

    [Fact]
    public async Task BIOCRD005QA01_a_confirmed_audit_failure_during_issuance_leaves_no_card_issue_committed()
    {
        var (service, repository) = BuildServiceWithFailingAudit();

        var error = await Assert.ThrowsAsync<CardIssueError>(() =>
            service.IssueAsync(PersonId, CardClass, IssueRequest(), Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Empty(repository.List(TenantId, PersonId, CardClass)); // the exact assertion the acceptance probe made
    }

    [Fact]
    public async Task A_confirmed_audit_failure_during_activation_leaves_the_card_issue_unmodified()
    {
        // Issue successfully first (with a working audit sink), then swap in a failing one to
        // isolate activation's own audit-atomicity, mirroring how a real outage could strike
        // mid-lifecycle rather than only at issuance.
        var repository = new InMemoryCardIssueRepository();
        var workingService = new CardIssueService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(),
            new InMemoryCardIssueAuditSink(), new CardIssueLockRegistry(), new InMemoryIdempotencyStore());
        var issued = await workingService.IssueAsync(PersonId, CardClass, IssueRequest(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var failingService = new CardIssueService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(),
            new AlwaysFailingAuditSink(), new CardIssueLockRegistry(), new InMemoryIdempotencyStore());

        var error = await Assert.ThrowsAsync<CardIssueError>(() =>
            failingService.ActivateAsync(PersonId, CardClass, issued.SequenceNumber, issued.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("audit_unavailable", error.Code);
        var stillIssued = repository.Get(TenantId, PersonId, CardClass, issued.SequenceNumber);
        Assert.NotNull(stillIssued);
        Assert.Equal(CardIssueStatus.Issued, stillIssued!.Status); // never advanced to Active
        Assert.Equal(issued.Version, stillIssued.Version); // never incremented
    }

    [Fact]
    public async Task A_successful_issuance_still_commits_normally_when_the_audit_sink_works()
    {
        var repository = new InMemoryCardIssueRepository();
        var service = new CardIssueService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(),
            new InMemoryCardIssueAuditSink(), new CardIssueLockRegistry(), new InMemoryIdempotencyStore());

        var issue = await service.IssueAsync(PersonId, CardClass, IssueRequest(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(1, issue.SequenceNumber);
        Assert.Single(repository.List(TenantId, PersonId, CardClass));
    }

    /// <summary>
    /// BIO-CRD-005-QA-01: with sequence assignment (a pure read) and commit split apart so the
    /// audit can run between them, the (tenant, person, card class) lock every mutation
    /// acquires is now the ONLY thing preventing two concurrent issuances from computing the
    /// same next sequence number and racing on <c>Create</c>. This proves that guarantee holds
    /// through the real service, not merely that the repository alone used to retry internally.
    /// </summary>
    [Fact]
    public async Task Concurrent_service_level_issuance_for_the_same_person_and_card_class_never_collides_or_skips_a_sequence_number()
    {
        var repository = new InMemoryCardIssueRepository();
        var service = new CardIssueService(
            new FakeCurrentTenantProvider(TenantId), repository, new FakePersonExistenceGateway(),
            new InMemoryCardIssueAuditSink(), new CardIssueLockRegistry(), new InMemoryIdempotencyStore());
        const int concurrentIssues = 50;

        var tasks = Enumerable.Range(0, concurrentIssues)
            .Select(_ => service.IssueAsync(PersonId, CardClass, IssueRequest(), Operator(), CanonicalId.NewEventId(), CancellationToken.None));
        await Task.WhenAll(tasks);

        var sequences = repository.List(TenantId, PersonId, CardClass).Select(i => i.SequenceNumber).OrderBy(s => s).ToList();
        Assert.Equal(Enumerable.Range(1, concurrentIssues).ToList(), sequences);
    }
}
