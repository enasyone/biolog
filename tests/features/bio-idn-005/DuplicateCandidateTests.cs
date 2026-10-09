using Biolog.Features.DuplicateReview.Domain;
using Biolog.Features.DuplicateReview.Identifiers;
using Xunit;

namespace Biolog.Features.DuplicateReview.Tests;

/// <summary>Domain-level proof of <see cref="DuplicateCandidate"/>'s own invariants, independent of the service/repository.</summary>
public sealed class DuplicateCandidateTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonA = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonB = "per_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14";

    // The constructor throws before any object initializer would run, but the `required`
    // members must still be supplied syntactically - the compiler enforces this statically,
    // independent of the constructor's own runtime behaviour.
    private static DuplicateCandidate New(string tenantId, string personIdLow, string personIdHigh) =>
        new(tenantId, personIdLow, personIdHigh)
        {
            SignalSources = new HashSet<DuplicateSignalSource>(),
            Status = DuplicateReviewStatus.PendingReview,
            Version = 1,
            CreatedAt = UtcInstant.Now(),
            UpdatedAt = UtcInstant.Now(),
        };

    [Fact]
    public void OrderPair_produces_the_same_result_regardless_of_input_order()
    {
        var forward = DuplicateCandidate.OrderPair(PersonA, PersonB);
        var reverse = DuplicateCandidate.OrderPair(PersonB, PersonA);

        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void Constructing_a_candidate_with_a_malformed_tenant_id_throws()
    {
        var (low, high) = DuplicateCandidate.OrderPair(PersonA, PersonB);
        Assert.Throws<ArgumentException>(() => New("not-a-tenant-id", low, high));
    }

    [Fact]
    public void Constructing_a_candidate_with_a_malformed_person_id_throws()
    {
        var (low, high) = DuplicateCandidate.OrderPair(PersonA, PersonB);
        Assert.Throws<ArgumentException>(() => New(TenantA, "not-a-person-id", high));
        Assert.Throws<ArgumentException>(() => New(TenantA, low, "not-a-person-id"));
    }

    [Fact]
    public void Constructing_a_self_paired_candidate_throws()
    {
        Assert.Throws<ArgumentException>(() => New(TenantA, PersonA, PersonA));
    }

    [Fact]
    public void Constructing_a_candidate_with_the_pair_in_the_wrong_order_throws()
    {
        var (low, high) = DuplicateCandidate.OrderPair(PersonA, PersonB);
        Assert.Throws<ArgumentException>(() => New(TenantA, high, low));
    }
}
