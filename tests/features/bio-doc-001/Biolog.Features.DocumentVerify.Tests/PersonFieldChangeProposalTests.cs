using Biolog.Features.DocumentVerify.Model;
using Xunit;

namespace Biolog.Features.DocumentVerify.Tests;

/// <summary>Developer tests for BIO-DOC-001-AC04 ("document results never silently overwrite
/// PERSON fields"), proven structurally rather than merely asserted - see
/// <see cref="PersonFieldChangeProposal"/>'s own remarks.</summary>
public sealed class PersonFieldChangeProposalTests
{
    private const string TenantId = "ten_test-tenant";
    private const string SessionId = "dcs_1";
    private const string PersonId = "per_1";
    private const string ProposedAt = "2026-09-21T12:00:03.000Z";
    private const string AcceptedAt = "2026-09-21T12:05:00.000Z";

    private static PersonFieldChangeProposal SampleProposal(bool requiresManualReview = false) =>
        PersonFieldChangeProposal.Propose(
            TenantId, SessionId, PersonId, "date_of_birth", "2001-03-14",
            sourceFieldEvidenceSequenceNumber: 1, requiresManualReview, ProposedAt);

    [Fact]
    public void AC04_a_freshly_proposed_change_is_never_applied_by_construction()
    {
        var proposal = SampleProposal();

        Assert.Null(proposal.AppliedAt);
        Assert.Null(proposal.AppliedBy);
    }

    [Fact]
    public void AC04_accept_requires_a_non_empty_actor_there_is_no_anonymous_or_implicit_acceptance()
    {
        var proposal = SampleProposal();

        Assert.Throws<ArgumentException>(() => proposal.Accept("", AcceptedAt));
    }

    [Fact]
    public void AC04_accept_returns_a_new_instance_the_original_proposal_stays_unapplied()
    {
        var proposal = SampleProposal();

        var accepted = proposal.Accept("reviewer_amina", AcceptedAt);

        Assert.Null(proposal.AppliedAt);
        Assert.Null(proposal.AppliedBy);
        Assert.Equal(AcceptedAt, accepted.AppliedAt);
        Assert.Equal("reviewer_amina", accepted.AppliedBy);
        Assert.NotSame(proposal, accepted);
    }

    [Fact]
    public void AC04_AC05_a_proposal_flagged_as_requiring_manual_review_cannot_be_accepted_until_the_conflict_clears()
    {
        var proposal = SampleProposal(requiresManualReview: true);

        var ex = Assert.Throws<InvalidOperationException>(() => proposal.Accept("reviewer_amina", AcceptedAt));
        Assert.Contains("requires_manual_review", ex.Message);
    }

    [Fact]
    public void an_already_accepted_proposal_cannot_be_accepted_again()
    {
        var accepted = SampleProposal().Accept("reviewer_amina", AcceptedAt);

        Assert.Throws<InvalidOperationException>(() => accepted.Accept("reviewer_femi", AcceptedAt));
    }

    [Fact]
    public void carries_provenance_back_to_the_field_evidence_it_was_derived_from()
    {
        var proposal = SampleProposal();

        Assert.Equal(1, proposal.SourceFieldEvidenceSequenceNumber);
        Assert.Equal(SessionId, proposal.DocumentSessionId);
        Assert.Equal(PersonId, proposal.PersonId);
    }
}
