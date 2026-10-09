// Developer tests for BIO-CRD-005. SYNTHETIC fixtures only.
using Biolog.Features.CardIssuance.Audit;
using Biolog.Features.CardIssuance.Domain;
using Xunit;

namespace Biolog.Features.CardIssuance.Tests;

public sealed class CardIssueRepositoryTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string OtherTenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string OtherPersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5c";
    private const string StaffClass = "staff";
    private const string VisitorClass = "visitor";

    private static CardIssueProvenance Provenance(string suffix = "1") => new()
    {
        TemplateRef = $"template-{suffix}",
        PhotoRef = $"photo-{suffix}",
        LogoRef = $"logo-{suffix}",
        SignatureRef = $"signature-{suffix}",
        Operator = new AuditActor(ActorType.User, "operator-1"),
        PrinterRef = $"printer-{suffix}",
        IssuedAt = "2026-09-23T00:00:00.000Z",
    };

    /// <summary>Test-only convenience mirroring exactly what <see cref="CardIssueService.IssueAsync"/> now does (BIO-CRD-005-QA-01): compute the next sequence number, then commit - two separate repository calls, never one combined step, so a caller can audit in between.</summary>
    private static CardIssue Append(InMemoryCardIssueRepository repo, string tenantId, string personId, string cardClass, CardIssueProvenance provenance, string now)
    {
        var sequenceNumber = repo.NextSequenceNumber(tenantId, personId, cardClass);
        var issue = new CardIssue(tenantId, personId, cardClass, sequenceNumber)
        {
            Status = CardIssueStatus.Issued,
            Version = 1,
            Provenance = provenance,
            CreatedAt = now,
            UpdatedAt = now,
        };
        return repo.Create(issue);
    }

    [Fact]
    public void AC01_the_first_card_issued_for_a_person_and_card_class_is_sequence_1()
    {
        var repo = new InMemoryCardIssueRepository();
        var issue = Append(repo, TenantId, PersonId, StaffClass, Provenance(), "2026-09-23T00:00:00.000Z");
        Assert.Equal(1, issue.SequenceNumber);
        Assert.Equal(CardIssueStatus.Issued, issue.Status);
        Assert.Equal(1, issue.Version);
    }

    [Fact]
    public void AC01_each_append_increments_the_sequence_and_AC02_never_reuses_or_resets_it()
    {
        var repo = new InMemoryCardIssueRepository();
        var i1 = Append(repo, TenantId, PersonId, StaffClass, Provenance("1"), "2026-09-23T00:00:00.000Z");
        var i2 = Append(repo, TenantId, PersonId, StaffClass, Provenance("2"), "2026-09-23T00:00:01.000Z");
        var i3 = Append(repo, TenantId, PersonId, StaffClass, Provenance("3"), "2026-09-23T00:00:02.000Z");

        Assert.Equal([1, 2, 3], new[] { i1.SequenceNumber, i2.SequenceNumber, i3.SequenceNumber });

        // The original sequence-1 row is still readable exactly as it was - never overwritten.
        var reread = repo.Get(TenantId, PersonId, StaffClass, 1);
        Assert.NotNull(reread);
        Assert.Equal("template-1", reread!.Provenance.TemplateRef);
    }

    [Fact]
    public void AC01_the_sequence_is_scoped_per_card_class_not_shared_across_classes_for_the_same_person()
    {
        var repo = new InMemoryCardIssueRepository();
        var staff1 = Append(repo, TenantId, PersonId, StaffClass, Provenance(), "2026-09-23T00:00:00.000Z");
        var visitor1 = Append(repo, TenantId, PersonId, VisitorClass, Provenance(), "2026-09-23T00:00:00.000Z");

        Assert.Equal(1, staff1.SequenceNumber);
        Assert.Equal(1, visitor1.SequenceNumber); // independent sequence per card class, not a shared counter
    }

    [Fact]
    public void A_card_issue_in_one_tenant_is_invisible_to_another_tenant()
    {
        var repo = new InMemoryCardIssueRepository();
        Append(repo, TenantId, PersonId, StaffClass, Provenance(), "2026-09-23T00:00:00.000Z");

        Assert.Null(repo.Get(OtherTenantId, PersonId, StaffClass, 1));
        Assert.Empty(repo.List(OtherTenantId, PersonId, StaffClass));
    }

    /// <summary>
    /// BIO-CRD-005-QA-01 (Chioma's acceptance review round 1): concurrency safety for issuance
    /// now lives at the SERVICE layer (<c>CardIssueLockRegistry</c> serialising
    /// <c>NextSequenceNumber</c> and <c>Create</c> across the audit write in between), not
    /// inside the repository alone - splitting sequence assignment from commit so the audit can
    /// run between them (fixing the audit-before-commit violation) necessarily gives up the
    /// repository's own former internal optimistic-retry loop. This is proved at the service
    /// level instead: see
    /// <c>CardIssueAuditAtomicityTests.Concurrent_service_level_issuance_for_the_same_person_and_card_class_never_collides_or_skips_a_sequence_number</c>.
    /// </summary>
    [Fact]
    public void NextSequenceNumber_and_Create_are_two_separate_calls_a_caller_must_itself_serialise()
    {
        var repo = new InMemoryCardIssueRepository();
        var sequenceNumber = repo.NextSequenceNumber(TenantId, PersonId, StaffClass);
        Assert.Equal(1, sequenceNumber);

        var issue = new CardIssue(TenantId, PersonId, StaffClass, sequenceNumber)
        {
            Status = CardIssueStatus.Issued,
            Version = 1,
            Provenance = Provenance(),
            CreatedAt = "2026-09-23T00:00:00.000Z",
            UpdatedAt = "2026-09-23T00:00:00.000Z",
        };
        var created = repo.Create(issue);
        Assert.Equal(1, created.SequenceNumber);
    }

    [Fact]
    public void Create_throws_on_a_collision_at_an_already_occupied_key_rather_than_silently_overwriting()
    {
        var repo = new InMemoryCardIssueRepository();
        Append(repo, TenantId, PersonId, StaffClass, Provenance("1"), "2026-09-23T00:00:00.000Z"); // occupies sequence 1

        var colliding = new CardIssue(TenantId, PersonId, StaffClass, 1)
        {
            Status = CardIssueStatus.Issued,
            Version = 1,
            Provenance = Provenance("2"),
            CreatedAt = "2026-09-23T00:00:01.000Z",
            UpdatedAt = "2026-09-23T00:00:01.000Z",
        };

        var error = Assert.Throws<CardIssueError>(() => repo.Create(colliding));
        Assert.Equal("card_issue_collision", error.Code);
    }

    [Fact]
    public void AC04_get_active_returns_null_when_no_card_is_active()
    {
        var repo = new InMemoryCardIssueRepository();
        Append(repo, TenantId, PersonId, StaffClass, Provenance(), "2026-09-23T00:00:00.000Z");
        Assert.Null(repo.GetActive(TenantId, PersonId, StaffClass));
    }

    [Fact]
    public void AC04_get_active_finds_the_single_card_currently_marked_active()
    {
        var repo = new InMemoryCardIssueRepository();
        var issue = Append(repo, TenantId, PersonId, StaffClass, Provenance(), "2026-09-23T00:00:00.000Z");
        var activated = issue with { Status = CardIssueStatus.Active, ActivatedAt = "2026-09-23T00:01:00.000Z", Version = 2 };
        repo.Replace(issue, activated);

        var active = repo.GetActive(TenantId, PersonId, StaffClass);
        Assert.NotNull(active);
        Assert.Equal(1, active!.SequenceNumber);
    }

    [Fact]
    public void Replace_succeeds_when_the_expected_row_matches_exactly()
    {
        var repo = new InMemoryCardIssueRepository();
        var issue = Append(repo, TenantId, PersonId, StaffClass, Provenance(), "2026-09-23T00:00:00.000Z");
        var updated = issue with { EncodedAt = "2026-09-23T00:01:00.000Z", Version = 2 };

        var result = repo.Replace(issue, updated);
        Assert.Equal(2, result.Version);
        Assert.Equal("2026-09-23T00:01:00.000Z", result.EncodedAt);
    }

    [Fact]
    public void Replace_throws_concurrency_conflict_when_the_stored_row_no_longer_matches_expected()
    {
        var repo = new InMemoryCardIssueRepository();
        var issue = Append(repo, TenantId, PersonId, StaffClass, Provenance(), "2026-09-23T00:00:00.000Z");

        // Someone else updates first.
        repo.Replace(issue, issue with { EncodedAt = "2026-09-23T00:01:00.000Z", Version = 2 });

        // The original caller's stale `issue` (still version 1) then tries to replace.
        var error = Assert.Throws<CardIssueError>(() => repo.Replace(issue, issue with { PrintedAt = "2026-09-23T00:02:00.000Z", Version = 2 }));
        Assert.Equal("concurrency_conflict", error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public void Replace_throws_not_found_for_a_row_that_was_never_created()
    {
        var repo = new InMemoryCardIssueRepository();
        var phantom = new CardIssue(TenantId, PersonId, StaffClass, 1)
        {
            Status = CardIssueStatus.Issued,
            Version = 1,
            Provenance = Provenance(),
            CreatedAt = "2026-09-23T00:00:00.000Z",
            UpdatedAt = "2026-09-23T00:00:00.000Z",
        };

        var error = Assert.Throws<CardIssueError>(() => repo.Replace(phantom, phantom with { Version = 2 }));
        Assert.Equal("card_issue_not_found", error.Code);
    }
}
