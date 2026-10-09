using System.Reflection;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Biolog.Features.InstitutionalIdentifiers.Identifiers;
using Xunit;

namespace Biolog.Features.InstitutionalIdentifiers.Tests;

/// <summary>
/// Applies BIO-IDN-001-QA-01's lesson from the start, rather than waiting for a review round
/// to find the same class of gap here too: the persistence boundary itself must refuse an
/// identity change, an illegal initial state, and be inaccessible to ordinary production
/// consumers. This test project has <c>InternalsVisibleTo</c> access specifically so these
/// attacks can be driven directly against <see cref="InMemoryInstitutionalIdentifierRepository"/>.
/// </summary>
public sealed class InstitutionalIdentifierRepositoryGovernanceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    private static InstitutionalIdentifier NewActive(string tenantId, IdentifierType type = IdentifierType.MatriculationNumber, string value = "MAT/2026/00123") =>
        new(PersonId, tenantId, type, customLabel: null, sequenceNumber: 1, value)
        {
            Status = IdentifierStatus.Active,
            Version = 1,
            CreatedAt = UtcInstant.Now(),
            UpdatedAt = UtcInstant.Now(),
        };

    [Fact]
    public async Task CreateAsync_rejects_an_identifier_whose_initial_state_is_not_ACTIVE_version_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var notInitial = NewActive(TenantA) with { Status = IdentifierStatus.Retired };

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => repository.CreateAsync(notInitial, ct));

        Assert.Equal("invalid_initial_state", error.Code);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_second_ACTIVE_identifier_for_the_same_person_type_and_label()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        await repository.CreateAsync(NewActive(TenantA), ct);
        var second = new InstitutionalIdentifier(PersonId, TenantA, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 2, "MAT/2026/99999")
        {
            Status = IdentifierStatus.Active, Version = 1, CreatedAt = UtcInstant.Now(), UpdatedAt = UtcInstant.Now(),
        };

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => repository.CreateAsync(second, ct));

        Assert.Equal("identifier_already_active", error.Code);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_moving_an_identifier_to_a_foreign_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var original = await repository.CreateAsync(NewActive(TenantA), ct);
        // TenantId is get-only, so `original with { TenantId = ... }` does not compile - the
        // attack is only expressible by constructing a genuinely separate instance.
        var forged = new InstitutionalIdentifier(original.PersonId, TenantB, original.Type, customLabel: null, original.SequenceNumber, original.Value)
        {
            Status = IdentifierStatus.Retired, Version = 2, CreatedAt = original.CreatedAt, UpdatedAt = UtcInstant.Now(),
        };

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => repository.ReplaceAsync(original, forged, ct));

        Assert.Equal("identity_immutable", error.Code);
        Assert.NotNull(await repository.GetAsync(TenantA, PersonId, original.Type, customLabel: null, original.SequenceNumber, ct));
        Assert.Null(await repository.GetAsync(TenantB, PersonId, original.Type, customLabel: null, original.SequenceNumber, ct));
    }

    [Fact]
    public async Task ReplaceAsync_rejects_changing_value_even_via_a_forged_instance()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var original = await repository.CreateAsync(NewActive(TenantA), ct);
        var forged = new InstitutionalIdentifier(original.PersonId, original.TenantId, original.Type, customLabel: null, original.SequenceNumber, "CHANGED-VALUE")
        {
            Status = IdentifierStatus.Active, Version = 2, CreatedAt = original.CreatedAt, UpdatedAt = UtcInstant.Now(),
        };

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => repository.ReplaceAsync(original, forged, ct));

        Assert.Equal("identity_immutable", error.Code);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_two_unrelated_identifier_instances()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var original = await repository.CreateAsync(NewActive(TenantA), ct);
        var unrelated = NewActive(TenantA, IdentifierType.StaffNumber, "STF-00456") with { Status = IdentifierStatus.Retired, Version = 2 };

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => repository.ReplaceAsync(original, unrelated, ct));

        Assert.Equal("identity_immutable", error.Code);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_replacing_an_already_retired_identifier_a_second_time()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var original = await repository.CreateAsync(NewActive(TenantA), ct);
        var retired = await repository.ReplaceAsync(original, original with { Status = IdentifierStatus.Retired, Version = 2 }, ct);
        var secondAttempt = retired with { Version = 3 }; // no-op status change, still illegal at this boundary

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => repository.ReplaceAsync(retired, secondAttempt, ct));

        Assert.Equal("already_retired", error.Code);
    }

    [Fact]
    public void IInstitutionalIdentifierRepository_and_InMemoryInstitutionalIdentifierRepository_are_not_public()
    {
        Assert.False(typeof(IInstitutionalIdentifierRepository).IsPublic);
        Assert.False(typeof(InMemoryInstitutionalIdentifierRepository).IsPublic);
    }

    [Fact]
    public void InstitutionalIdentifier_has_no_publicly_accessible_constructor()
    {
        var publicConstructors = typeof(InstitutionalIdentifier).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void InMemoryInstitutionalIdentifierRepository_has_no_audit_sink_dependency_of_any_kind()
    {
        var constructorParams = typeof(InMemoryInstitutionalIdentifierRepository).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters());
        Assert.DoesNotContain(constructorParams, p => p.ParameterType.Name.Contains("Audit"));
    }
}
