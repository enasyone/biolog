using Biolog.Features.DocumentVerify.Model;
using Xunit;

namespace Biolog.Features.DocumentVerify.Tests;

/// <summary>Developer tests for BIO-DOC-001-AC05 ("manual review state and conflict evidence
/// are supported").</summary>
public sealed class DocumentConflictTests
{
    private const string TenantId = "ten_test-tenant";
    private const string SessionId = "dcs_1";
    private const string RaisedAt = "2026-09-21T12:00:04.000Z";
    private const string Later = "2026-09-21T12:10:00.000Z";

    [Fact]
    public void AC05_raising_a_conflict_starts_pending_review_with_the_disagreeing_sequence_numbers_recorded()
    {
        var conflict = DocumentConflict.Raise(TenantId, SessionId, "date_of_birth", new[] { 2, 5 }, RaisedAt);

        Assert.Equal(ManualReviewState.PendingReview, conflict.ReviewState);
        Assert.Equal(new[] { 2, 5 }, conflict.ConflictingFieldEvidenceSequenceNumbers);
    }

    [Fact]
    public void AC05_a_conflict_of_fewer_than_two_disagreeing_records_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => DocumentConflict.Raise(TenantId, SessionId, "surname", new[] { 1 }, RaisedAt));
    }

    [Fact]
    public void AC05_review_state_moves_pending_to_in_review_to_resolved()
    {
        var conflict = DocumentConflict.Raise(TenantId, SessionId, "surname", new[] { 1, 3 }, RaisedAt);

        conflict.BeginReview(conflict.Version, Later);
        Assert.Equal(ManualReviewState.InReview, conflict.ReviewState);

        conflict.Resolve("Okafor", "reviewer_amina", Later, conflict.Version);
        Assert.Equal(ManualReviewState.Resolved, conflict.ReviewState);
        Assert.Equal("Okafor", conflict.ResolvedValue);
        Assert.Equal("reviewer_amina", conflict.ResolvedBy);
        Assert.Equal(Later, conflict.ResolvedAt);
    }

    [Fact]
    public void AC05_resolve_is_valid_directly_from_pending_review_a_reviewer_need_not_call_begin_review_first()
    {
        var conflict = DocumentConflict.Raise(TenantId, SessionId, "surname", new[] { 1, 3 }, RaisedAt);

        conflict.Resolve("Okafor", "reviewer_amina", Later, conflict.Version);

        Assert.Equal(ManualReviewState.Resolved, conflict.ReviewState);
    }

    [Fact]
    public void an_already_resolved_conflict_cannot_be_resolved_again()
    {
        var conflict = DocumentConflict.Raise(TenantId, SessionId, "surname", new[] { 1, 3 }, RaisedAt);
        conflict.Resolve("Okafor", "reviewer_amina", Later, conflict.Version);

        Assert.Throws<InvalidOperationException>(() => conflict.Resolve("Okafor", "reviewer_femi", Later, conflict.Version));
    }

    [Fact]
    public void resolve_requires_a_non_empty_reviewer_there_is_no_anonymous_resolution()
    {
        var conflict = DocumentConflict.Raise(TenantId, SessionId, "surname", new[] { 1, 3 }, RaisedAt);

        Assert.Throws<ArgumentException>(() => conflict.Resolve("Okafor", "", Later, conflict.Version));
    }

    [Fact]
    public void stale_expected_version_on_resolve_is_rejected()
    {
        var conflict = DocumentConflict.Raise(TenantId, SessionId, "surname", new[] { 1, 3 }, RaisedAt);
        var staleVersion = conflict.Version;
        conflict.BeginReview(staleVersion, Later);

        Assert.Throws<ConcurrencyException>(() => conflict.Resolve("Okafor", "reviewer_amina", Later, staleVersion));
    }
}
