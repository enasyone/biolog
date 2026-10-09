using System.Reflection;
using Biolog.Features.DuplicateReview.Domain;
using Biolog.Features.DuplicateReview.Identifiers;
using Xunit;

namespace Biolog.Features.DuplicateReview.Tests;

/// <summary>
/// Applies BIO-IDN-001-QA-01's lesson from the start, rather than waiting for a review round to
/// find the same class of gap here too: the persistence boundary itself must refuse an identity
/// change, an illegal initial state, and be inaccessible to ordinary production consumers. This
/// test project has <c>InternalsVisibleTo</c> access specifically so these attacks can be
/// driven directly against <see cref="InMemoryDuplicateCandidateRepository"/>.
/// </summary>
public sealed class DuplicateCandidateRepositoryGovernanceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string PersonA = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonB = "per_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14";
    private const string PersonC = "per_0199e0c3-3d46-7297-ae58-6a1b8c0d4f25";

    private static DuplicateCandidate NewPending(string tenantId, string personIdLow, string personIdHigh) =>
        new(tenantId, personIdLow, personIdHigh)
        {
            SignalSources = new HashSet<DuplicateSignalSource> { DuplicateSignalSource.NameAndDateOfBirth },
            Status = DuplicateReviewStatus.PendingReview,
            Version = 1,
            CreatedAt = UtcInstant.Now(),
            UpdatedAt = UtcInstant.Now(),
        };

    [Fact]
    public async Task CreateAsync_rejects_a_candidate_whose_initial_state_is_not_PENDING_REVIEW_version_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryDuplicateCandidateRepository();
        var (low, high) = DuplicateCandidate.OrderPair(PersonA, PersonB);
        var notInitial = NewPending(TenantA, low, high) with { Status = DuplicateReviewStatus.ConfirmedDuplicate };

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => repository.CreateAsync(notInitial, ct));

        Assert.Equal("invalid_initial_state", error.Code);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_moving_a_candidate_to_a_foreign_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryDuplicateCandidateRepository();
        var (low, high) = DuplicateCandidate.OrderPair(PersonA, PersonB);
        var original = await repository.CreateAsync(NewPending(TenantA, low, high), ct);
        // TenantId is get-only, so `original with { TenantId = ... }` does not compile - the
        // attack is only expressible by constructing a genuinely separate instance.
        var forged = new DuplicateCandidate(TenantB, low, high)
        {
            SignalSources = original.SignalSources, Status = DuplicateReviewStatus.ConfirmedDuplicate, Version = 2, CreatedAt = original.CreatedAt, UpdatedAt = UtcInstant.Now(),
        };

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => repository.ReplaceAsync(original, forged, ct));

        Assert.Equal("identity_immutable", error.Code);
        Assert.NotNull(await repository.GetAsync(TenantA, low, high, ct));
        Assert.Null(await repository.GetAsync(TenantB, low, high, ct));
    }

    [Fact]
    public async Task ReplaceAsync_rejects_two_unrelated_candidate_instances()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryDuplicateCandidateRepository();
        var (lowAB, highAB) = DuplicateCandidate.OrderPair(PersonA, PersonB);
        var (lowAC, highAC) = DuplicateCandidate.OrderPair(PersonA, PersonC);
        var original = await repository.CreateAsync(NewPending(TenantA, lowAB, highAB), ct);
        var unrelated = NewPending(TenantA, lowAC, highAC) with { Version = 2 };

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => repository.ReplaceAsync(original, unrelated, ct));

        Assert.Equal("identity_immutable", error.Code);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_a_stale_expected_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryDuplicateCandidateRepository();
        var (low, high) = DuplicateCandidate.OrderPair(PersonA, PersonB);
        var original = await repository.CreateAsync(NewPending(TenantA, low, high), ct);
        await repository.ReplaceAsync(original, original with { Version = 2 }, ct);

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => repository.ReplaceAsync(original, original with { Version = 2 }, ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public void IDuplicateCandidateRepository_and_InMemoryDuplicateCandidateRepository_are_not_public()
    {
        Assert.False(typeof(IDuplicateCandidateRepository).IsPublic);
        Assert.False(typeof(InMemoryDuplicateCandidateRepository).IsPublic);
    }

    [Fact]
    public void DuplicateCandidate_has_no_publicly_accessible_constructor()
    {
        var publicConstructors = typeof(DuplicateCandidate).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void DuplicateReviewService_is_not_public()
    {
        // BIO-IDN-001-QA-01's lesson applied to the service itself, not only the aggregate and
        // repository: only Biolog.Api (DI) and this test assembly may reference it directly.
        Assert.False(typeof(DuplicateReviewService).IsPublic);
    }

    [Fact]
    public void This_project_has_no_assembly_reference_to_BIO_IDN_001s_PersonRegistry_project()
    {
        // BIO-IDN-005-AC03's structural proof: "merge is never automatic" is not merely
        // undocumented here, it is IMPOSSIBLE - this project never even references the
        // assembly a merge would have to mutate through.
        var referenced = typeof(DuplicateReviewService).Assembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.DoesNotContain(referenced, n => n is not null && n.Contains("PersonRegistry", StringComparison.Ordinal));
    }

    [Fact]
    public void No_type_in_this_project_defines_a_method_or_endpoint_named_anything_like_merge()
    {
        // BIO-IDN-005-AC03, structural, adversarial: grep-equivalent over every public and
        // internal method this project declares - mirrors BIO-IDN-003-AC04's identical
        // "no field/method named X anywhere" proof for its own excluded concept.
        var assembly = typeof(DuplicateReviewService).Assembly;
        var suspiciousMethods = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.Contains("merge", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(suspiciousMethods);
    }

    // --- BIO-IDN-005-QA-01: PersonContactPoint governance, applying BIO-IDN-001-QA-01 from the start ---

    [Fact]
    public async Task ContactPoint_ReplaceAsync_rejects_moving_a_contact_point_to_a_foreign_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonContactPointRepository();
        var now = UtcInstant.Now();
        var original = await repository.CreateAsync(
            new PersonContactPoint(TenantA, PersonA) { Email = "a@example.edu.ng", Version = 1, CreatedAt = now, UpdatedAt = now }, ct);
        // TenantId is get-only, so `original with { TenantId = ... }` does not compile - the
        // attack is only expressible by constructing a genuinely separate instance.
        var forged = new PersonContactPoint(TenantB, PersonA) { Email = "a@example.edu.ng", Version = 2, CreatedAt = original.CreatedAt, UpdatedAt = UtcInstant.Now() };

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => repository.ReplaceAsync(original, forged, ct));

        Assert.Equal("identity_immutable", error.Code);
        Assert.NotNull(await repository.GetAsync(TenantA, PersonA, ct));
        Assert.Null(await repository.GetAsync(TenantB, PersonA, ct));
    }

    [Fact]
    public async Task ContactPoint_ReplaceAsync_rejects_a_stale_expected_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonContactPointRepository();
        var now = UtcInstant.Now();
        var original = await repository.CreateAsync(
            new PersonContactPoint(TenantA, PersonA) { Email = "a@example.edu.ng", Version = 1, CreatedAt = now, UpdatedAt = now }, ct);
        await repository.ReplaceAsync(original, original with { Version = 2 }, ct);

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => repository.ReplaceAsync(original, original with { Version = 2 }, ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public void IPersonContactPointRepository_and_InMemoryPersonContactPointRepository_are_not_public()
    {
        Assert.False(typeof(IPersonContactPointRepository).IsPublic);
        Assert.False(typeof(InMemoryPersonContactPointRepository).IsPublic);
    }

    [Fact]
    public void PersonContactPoint_has_no_publicly_accessible_constructor()
    {
        var publicConstructors = typeof(PersonContactPoint).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void PersonContactPointService_is_not_public()
    {
        Assert.False(typeof(PersonContactPointService).IsPublic);
    }
}
