using System.Reflection;
using Biolog.Features.PersonRegistry.Domain;
using Biolog.Features.PersonRegistry.Identifiers;
using Xunit;

namespace Biolog.Features.PersonRegistry.Tests;

/// <summary>
/// BIO-IDN-001-QA-01 (Seyi round 1, High): the persistence boundary itself must refuse a
/// tenant move, an altered identifier and an illegal lifecycle jump - independently of
/// whether the caller reached it through <see cref="PersonService"/> or (as an independent
/// probe proved) directly. This test project has <c>InternalsVisibleTo</c> access
/// specifically so these attacks can be driven directly against
/// <see cref="InMemoryPersonRepository"/>, not merely asserted by reading the source.
/// </summary>
public sealed class PersonRepositoryGovernanceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";

    private static Person NewPreEnrolled(string tenantId) => new(CanonicalId.NewPersonId(), tenantId)
    {
        FirstName = "Synthetic",
        LastName = "Person",
        Status = PersonStatus.PreEnrolled,
        Version = 1,
        CreatedAt = UtcInstant.Now(),
        UpdatedAt = UtcInstant.Now(),
    };

    [Fact]
    public async Task CreateAsync_rejects_a_person_whose_initial_state_is_not_PRE_ENROLLED_version_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var notInitial = NewPreEnrolled(TenantA) with { Status = PersonStatus.Active };

        var error = await Assert.ThrowsAsync<PersonError>(() => repository.CreateAsync(notInitial, ct));

        Assert.Equal("invalid_initial_state", error.Code);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_person_created_directly_at_a_version_other_than_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var notInitial = NewPreEnrolled(TenantA) with { Version = 2 };

        var error = await Assert.ThrowsAsync<PersonError>(() => repository.CreateAsync(notInitial, ct));

        Assert.Equal("invalid_initial_state", error.Code);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_moving_a_person_to_a_foreign_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var original = await repository.CreateAsync(NewPreEnrolled(TenantA), ct);
        // TenantId has no init/set accessor (get-only), so `original with { TenantId = ... }`
        // does not even compile any more - this is the exact BIO-IDN-001-QA-01 reproduction
        // shape, now expressible only by constructing a genuinely separate instance sharing
        // the same Id, which the repository must still reject.
        var forged = new Person(original.Id, TenantB)
        {
            FirstName = original.FirstName, LastName = original.LastName, Status = PersonStatus.Archived, Version = 2,
            CreatedAt = original.CreatedAt, UpdatedAt = UtcInstant.Now(),
        };

        var error = await Assert.ThrowsAsync<PersonError>(() => repository.ReplaceAsync(original, forged, ct));

        Assert.Equal("identity_or_tenant_immutable", error.Code);
        // The exact BIO-IDN-001-QA-01 reproduction: the record must never actually move.
        Assert.NotNull(await repository.GetAsync(TenantA, original.Id, ct));
        Assert.Null(await repository.GetAsync(TenantB, original.Id, ct));
    }

    [Fact]
    public void Construction_rejects_an_empty_or_malformed_tenant_id_outright()
    {
        // Person's own constructor rejects an empty/malformed tenant id at construction
        // (defense in depth), so the only way to reach the repository's OWN foreign-tenant
        // check with an "empty" tenant is a value that is well-formed but simply different -
        // covered by ReplaceAsync_rejects_moving_a_person_to_a_foreign_tenant above. This
        // test proves construction itself fails closed for the exact empty case the review
        // named explicitly.
        Assert.Throws<ArgumentException>(() => new Person(CanonicalId.NewPersonId(), "")
        {
            FirstName = "A", LastName = "B", Status = PersonStatus.PreEnrolled, Version = 1, CreatedAt = "x", UpdatedAt = "x",
        });
    }

    [Fact]
    public async Task ReplaceAsync_rejects_two_unrelated_person_instances_with_different_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var original = await repository.CreateAsync(NewPreEnrolled(TenantA), ct);
        var unrelated = NewPreEnrolled(TenantA) with { Status = PersonStatus.Archived, Version = 2 }; // a DIFFERENT person entirely

        var error = await Assert.ThrowsAsync<PersonError>(() => repository.ReplaceAsync(original, unrelated, ct));

        Assert.Equal("identity_or_tenant_immutable", error.Code);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_an_illegal_lifecycle_jump_even_with_identity_and_tenant_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var original = await repository.CreateAsync(NewPreEnrolled(TenantA), ct);
        var illegalJump = original with { Status = PersonStatus.Active, Version = 2 }; // PRE_ENROLLED -> ACTIVE skips VERIFIED

        var error = await Assert.ThrowsAsync<PersonError>(() => repository.ReplaceAsync(original, illegalJump, ct));

        Assert.Equal("invalid_status_transition", error.Code);
    }

    [Fact]
    public void InMemoryPersonRepository_has_no_audit_sink_dependency_of_any_kind()
    {
        // BIO-IDN-001-QA-01's "a mutation that has no audit dependency" case: a direct
        // repository call CANNOT be audited, structurally - there is no audit sink for it to
        // even call. This is why access to this type must itself be restricted (below),
        // rather than relying on an audit call the repository has no way to make.
        var constructorParams = typeof(InMemoryPersonRepository).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters());
        Assert.DoesNotContain(constructorParams, p => p.ParameterType.Name.Contains("Audit"));
    }

    [Fact]
    public void IPersonRepository_and_InMemoryPersonRepository_are_not_public()
    {
        // BIO-IDN-001-QA-01's required closure: "make the unaudited persistence mutation
        // surface inaccessible to ordinary production consumers." Proven adversarially by
        // reflection, not just by reading the source - an ordinary consumer is any assembly
        // without this project's InternalsVisibleTo grant, and IsPublic reflects exactly that
        // boundary. Person itself stays a public TYPE (PersonResponse.From(Person) and
        // PersonService's own return types need to name it) - only its CONSTRUCTION is
        // restricted, the same shape BIO-TEN-002-QA-03's TenantContext fix used.
        Assert.False(typeof(IPersonRepository).IsPublic);
        Assert.False(typeof(InMemoryPersonRepository).IsPublic);
    }

    [Fact]
    public void Person_has_no_publicly_accessible_constructor()
    {
        // "Public aggregate construction must not let callers manufacture a record that
        // merely has the expected shape" - proven directly: the only constructor reflection
        // can find at all is the one internal constructor, not a public parameterless one an
        // object initializer could use.
        var allConstructors = typeof(Person).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var publicConstructors = typeof(Person).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(allConstructors); // the internal constructor is found via non-public reflection
        Assert.Empty(publicConstructors); // but none of them are public
    }
}
