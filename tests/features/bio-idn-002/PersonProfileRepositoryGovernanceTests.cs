using System.Reflection;
using Biolog.Features.PersonProfileRegistry.Domain;
using Biolog.Features.PersonProfileRegistry.Identifiers;
using Xunit;

namespace Biolog.Features.PersonProfileRegistry.Tests;

/// <summary>
/// Applies BIO-IDN-001-QA-01's lesson from the start, rather than waiting for a review round
/// to find the same class of gap here too: the persistence boundary itself must refuse an
/// identity change, an illegal initial state, and be inaccessible to ordinary production
/// consumers. This test project has <c>InternalsVisibleTo</c> access specifically so these
/// attacks can be driven directly against <see cref="InMemoryPersonProfileRepository"/>.
/// </summary>
public sealed class PersonProfileRepositoryGovernanceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    private static PersonProfile NewActive(string tenantId, ProfileType type = ProfileType.Student) => new(PersonId, tenantId, type, customLabel: null)
    {
        Status = ProfileStatus.Active,
        ActivatedAt = UtcInstant.Now(),
        Version = 1,
        CreatedAt = UtcInstant.Now(),
        UpdatedAt = UtcInstant.Now(),
    };

    [Fact]
    public async Task CreateAsync_rejects_a_profile_whose_initial_state_is_not_ACTIVE_version_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var notInitial = NewActive(TenantA) with { Status = ProfileStatus.Revoked };

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => repository.CreateAsync(notInitial, ct));

        Assert.Equal("invalid_initial_state", error.Code);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_moving_a_profile_to_a_foreign_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var original = await repository.CreateAsync(NewActive(TenantA), ct);
        // TenantId is get-only, so `original with { TenantId = ... }` does not compile - the
        // attack is only expressible by constructing a genuinely separate instance.
        var forged = new PersonProfile(original.PersonId, TenantB, original.Type, customLabel: null)
        {
            Status = ProfileStatus.Revoked, ActivatedAt = original.ActivatedAt, Version = 2, CreatedAt = original.CreatedAt, UpdatedAt = UtcInstant.Now(),
        };

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => repository.ReplaceAsync(original, forged, ct));

        Assert.Equal("identity_immutable", error.Code);
        Assert.NotNull(await repository.GetAsync(TenantA, PersonId, original.Type, ct));
        Assert.Null(await repository.GetAsync(TenantB, PersonId, original.Type, ct));
    }

    [Fact]
    public async Task ReplaceAsync_rejects_two_unrelated_profile_instances()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var original = await repository.CreateAsync(NewActive(TenantA), ct);
        var unrelated = NewActive(TenantA, ProfileType.Employee) with { Status = ProfileStatus.Revoked, Version = 2 };

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => repository.ReplaceAsync(original, unrelated, ct));

        Assert.Equal("identity_immutable", error.Code);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_replacing_an_already_revoked_profile_a_second_time()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var original = await repository.CreateAsync(NewActive(TenantA), ct);
        var revoked = await repository.ReplaceAsync(original, original with { Status = ProfileStatus.Revoked, Version = 2 }, ct);
        var secondAttempt = revoked with { Version = 3 }; // no-op status change, still illegal at this boundary

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => repository.ReplaceAsync(revoked, secondAttempt, ct));

        Assert.Equal("already_revoked", error.Code);
    }

    [Fact]
    public void IPersonProfileRepository_and_InMemoryPersonProfileRepository_are_not_public()
    {
        Assert.False(typeof(IPersonProfileRepository).IsPublic);
        Assert.False(typeof(InMemoryPersonProfileRepository).IsPublic);
    }

    [Fact]
    public void PersonProfile_has_no_publicly_accessible_constructor()
    {
        var publicConstructors = typeof(PersonProfile).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void InMemoryPersonProfileRepository_has_no_audit_sink_dependency_of_any_kind()
    {
        var constructorParams = typeof(InMemoryPersonProfileRepository).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters());
        Assert.DoesNotContain(constructorParams, p => p.ParameterType.Name.Contains("Audit"));
    }
}
