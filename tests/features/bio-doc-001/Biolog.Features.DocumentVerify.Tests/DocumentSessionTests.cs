using Biolog.Features.DocumentVerify.Model;
using Xunit;

namespace Biolog.Features.DocumentVerify.Tests;

/// <summary>Developer tests for BIO-DOC-001-AC01 ("DocumentSession supports person-linked or
/// pre-person onboarding") plus the ADR-0001 Decision 3 optimistic-concurrency discipline every
/// mutable aggregate in this story must honour.</summary>
public sealed class DocumentSessionTests
{
    private const string TenantId = "ten_test-tenant";
    private const string Now = "2026-09-21T12:00:00.000Z";
    private const string Later = "2026-09-21T12:05:00.000Z";

    [Fact]
    public void AC01_open_with_no_person_starts_pre_person_onboarding()
    {
        var session = DocumentSession.Open("dcs_1", TenantId, personId: null, Now);

        Assert.Null(session.PersonId);
        Assert.Equal(DocumentSessionStatus.Open, session.Status);
        Assert.Equal(DocumentSession.InitialVersion, session.Version);
    }

    [Fact]
    public void AC01_open_with_a_person_id_is_person_linked_immediately()
    {
        var session = DocumentSession.Open("dcs_2", TenantId, personId: "per_1", Now);

        Assert.Equal("per_1", session.PersonId);
        Assert.Equal(DocumentSessionStatus.PersonLinked, session.Status);
    }

    [Fact]
    public void AC01_link_person_transitions_a_pre_person_session_and_bumps_version()
    {
        var session = DocumentSession.Open("dcs_3", TenantId, personId: null, Now);

        session.LinkPerson("per_9", session.Version, Later);

        Assert.Equal("per_9", session.PersonId);
        Assert.Equal(DocumentSessionStatus.PersonLinked, session.Status);
        Assert.Equal(DocumentSession.InitialVersion + 1, session.Version);
        Assert.Equal(Later, session.UpdatedAt);
    }

    [Fact]
    public void AC01_linking_the_same_person_twice_is_idempotent_and_does_not_bump_version_again()
    {
        var session = DocumentSession.Open("dcs_4", TenantId, personId: null, Now);
        session.LinkPerson("per_9", session.Version, Later);
        var versionAfterFirstLink = session.Version;

        session.LinkPerson("per_9", versionAfterFirstLink, Later);

        Assert.Equal(versionAfterFirstLink, session.Version);
    }

    [Fact]
    public void AC01_linking_a_different_person_than_the_one_already_linked_is_refused()
    {
        var session = DocumentSession.Open("dcs_5", TenantId, personId: "per_1", Now);

        var ex = Assert.Throws<InvalidOperationException>(() => session.LinkPerson("per_2", session.Version, Later));
        Assert.Contains("already_linked_to_different_person", ex.Message);
    }

    [Fact]
    public void ADR0001_D3_mutating_with_a_stale_expected_version_is_rejected_with_current_version_reported()
    {
        var session = DocumentSession.Open("dcs_6", TenantId, personId: null, Now);
        var staleVersion = session.Version;
        session.LinkPerson("per_1", staleVersion, Later);

        var ex = Assert.Throws<ConcurrencyException>(() => session.EnterManualReview(staleVersion, Later));

        Assert.Equal(staleVersion, ex.ExpectedVersion);
        Assert.Equal(session.Version, ex.ActualVersion);
    }

    [Fact]
    public void closed_session_refuses_further_mutation()
    {
        var session = DocumentSession.Open("dcs_7", TenantId, personId: "per_1", Now);
        session.Close(session.Version, Later);

        Assert.Equal(DocumentSessionStatus.Closed, session.Status);
        Assert.Throws<InvalidOperationException>(() => session.EnterManualReview(session.Version, Later));
    }

    [Fact]
    public void manual_review_round_trip_returns_to_the_correct_prior_status()
    {
        var preLinked = DocumentSession.Open("dcs_8", TenantId, personId: null, Now);
        preLinked.EnterManualReview(preLinked.Version, Later);
        Assert.Equal(DocumentSessionStatus.ManualReviewPending, preLinked.Status);
        preLinked.ResolveManualReview(preLinked.Version, Later);
        Assert.Equal(DocumentSessionStatus.Open, preLinked.Status);

        var linked = DocumentSession.Open("dcs_9", TenantId, personId: "per_1", Now);
        linked.EnterManualReview(linked.Version, Later);
        linked.ResolveManualReview(linked.Version, Later);
        Assert.Equal(DocumentSessionStatus.PersonLinked, linked.Status);
    }

    [Theory]
    [InlineData("", TenantId)]
    [InlineData("dcs_1", "")]
    public void open_rejects_empty_id_or_tenant(string id, string tenantId)
    {
        Assert.Throws<ArgumentException>(() => DocumentSession.Open(id, tenantId, personId: null, Now));
    }
}
