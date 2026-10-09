using Biolog.Features.PersonRegistry.Api;
using Biolog.Features.PersonRegistry.Audit;
using Biolog.Features.PersonRegistry.Domain;
using Xunit;

namespace Biolog.Features.PersonRegistry.Tests;

/// <summary>BIO-IDN-001-AC01 (independent of roles), AC02 (tenant boundary), AC03 (lifecycle states), AC04 (governed deletion).</summary>
public sealed class PersonServiceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");

    private static PersonService NewService(string? tenantId = TenantA) => new(
        new FakeCurrentTenantProvider(tenantId), new InMemoryPersonRepository(), new InMemoryPersonAuditSink(), new PersonLockRegistry());

    private static RegisterPersonRequest ValidRequest(string firstName = "Ngozi", string lastName = "Okafor") => new()
    {
        FirstName = firstName,
        LastName = lastName,
    };

    [Fact]
    public async Task Register_creates_a_person_pre_enrolled_in_the_resolved_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var person = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.StartsWith("per_", person.Id);
        Assert.Equal(TenantA, person.TenantId);
        Assert.Equal(PersonStatus.PreEnrolled, person.Status);
        Assert.Equal(1, person.Version);
    }

    [Fact]
    public async Task Register_without_a_resolved_tenant_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(tenantId: null);

        var error = await Assert.ThrowsAsync<PersonError>(
            () => service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("tenant_context_missing", error.Code);
        Assert.Equal(400, error.Status);
    }

    [Theory]
    [InlineData(null, "Okafor")]
    [InlineData("", "Okafor")]
    [InlineData("   ", "Okafor")]
    [InlineData("Ngozi", null)]
    [InlineData("Ngozi", "")]
    public async Task Register_requires_first_and_last_name(string? firstName, string? lastName)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<PersonError>(() => service.RegisterAsync(
            new RegisterPersonRequest { FirstName = firstName, LastName = lastName }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Theory]
    [InlineData("2004-11-02")]
    [InlineData("2000-02-29")] // leap year
    [InlineData("1000-01-01")] // lower bound
    [InlineData("9999-12-31")] // upper bound
    public async Task Register_accepts_a_real_calendar_date_for_born_on(string bornOn)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var person = await service.RegisterAsync(
            new RegisterPersonRequest { FirstName = "Ngozi", LastName = "Okafor", BornOn = bornOn }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(bornOn, person.BornOn);
    }

    [Theory]
    [InlineData("2026-02-30")] // no such day
    [InlineData("2026-02-29")] // not a leap year
    [InlineData("1900-02-29")] // century non-leap
    [InlineData("0000-01-01")] // year out of range
    [InlineData("2026-13-01")] // no such month
    [InlineData("2026/11/02")] // wrong format
    [InlineData("not-a-date")]
    public async Task Register_rejects_an_impossible_or_malformed_born_on(string bornOn)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<PersonError>(() => service.RegisterAsync(
            new RegisterPersonRequest { FirstName = "Ngozi", LastName = "Okafor", BornOn = bornOn }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Get_returns_null_for_a_person_registered_in_a_different_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var serviceA = new PersonService(new FakeCurrentTenantProvider(TenantA), repository, new InMemoryPersonAuditSink(), new PersonLockRegistry());
        var serviceB = new PersonService(new FakeCurrentTenantProvider(TenantB), repository, new InMemoryPersonAuditSink(), new PersonLockRegistry());
        var person = await serviceA.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var crossTenant = await serviceB.GetAsync(person.Id, ct);

        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task List_never_returns_another_tenant_s_person()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var serviceA = new PersonService(new FakeCurrentTenantProvider(TenantA), repository, new InMemoryPersonAuditSink(), new PersonLockRegistry());
        var serviceB = new PersonService(new FakeCurrentTenantProvider(TenantB), repository, new InMemoryPersonAuditSink(), new PersonLockRegistry());
        await serviceA.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        await serviceB.RegisterAsync(ValidRequest("Chidi", "Eze"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var tenantAList = await serviceA.ListAsync(ct);

        var single = Assert.Single(tenantAList);
        Assert.Equal("Ngozi", single.FirstName);
    }

    [Fact]
    public async Task No_role_or_profile_field_exists_on_the_registration_request_or_the_aggregate()
    {
        // BIO-IDN-001-AC01, adversarial by reflection: a caller cannot smuggle a role/profile
        // type in, because the shape simply has no such field.
        var forbidden = new[] { "role", "profile", "profile_type", "student", "staff", "employee", "visitor" };
        var requestProps = typeof(RegisterPersonRequest).GetProperties().Select(p => p.Name.ToLowerInvariant());
        var personProps = typeof(Person).GetProperties().Select(p => p.Name.ToLowerInvariant());

        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(name, requestProps);
            Assert.DoesNotContain(name, personProps);
        }
    }

    [Fact]
    public void No_delete_method_exists_anywhere_on_the_repository_or_the_service()
    {
        // BIO-IDN-001-AC04, adversarial by reflection: the only way a person's active life
        // ends is the governed ARCHIVED transition (see UpdateStatus tests below) - there is
        // no separate delete/remove verb for a future caller to accidentally reach for.
        var repositoryMethods = typeof(IPersonRepository).GetMethods().Select(m => m.Name.ToLowerInvariant());
        var serviceMethods = typeof(PersonService).GetMethods().Select(m => m.Name.ToLowerInvariant());

        foreach (var forbidden in new[] { "delete", "remove", "purge", "erase" })
        {
            Assert.DoesNotContain(repositoryMethods, name => name.Contains(forbidden));
            Assert.DoesNotContain(serviceMethods, name => name.Contains(forbidden));
        }
    }

    private static async Task<Person> RegisteredAndVerified(PersonService service, CancellationToken ct)
    {
        var person = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        return await service.UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = "VERIFIED" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);
    }

    [Theory]
    [InlineData("PRE_ENROLLED", "VERIFIED")]
    [InlineData("VERIFIED", "ACTIVE")]
    [InlineData("VERIFIED", "ARCHIVED")]
    [InlineData("ACTIVE", "SUSPENDED")]
    [InlineData("ACTIVE", "EXPIRED")]
    [InlineData("ACTIVE", "FORMER_ALUMNI")]
    [InlineData("ACTIVE", "REVOKED")]
    [InlineData("SUSPENDED", "ACTIVE")]
    [InlineData("EXPIRED", "ACTIVE")]
    [InlineData("FORMER_ALUMNI", "ACTIVE")]
    [InlineData("REVOKED", "ARCHIVED")]
    public async Task A_baseline_lifecycle_transition_is_allowed(string from, string to)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var person = await DriveTo(service, from, ct);

        var updated = await service.UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = to }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a90", ct);

        Assert.Equal(to, updated.Status.ToWireString());
        Assert.Equal(person.Version + 1, updated.Version);
    }

    [Theory]
    [InlineData("PRE_ENROLLED", "ACTIVE")] // must pass through VERIFIED
    [InlineData("VERIFIED", "SUSPENDED")] // must pass through ACTIVE
    [InlineData("SUSPENDED", "EXPIRED")] // suspended must return to ACTIVE first
    [InlineData("REVOKED", "ACTIVE")] // revoked is not reversible except to ARCHIVED
    public async Task An_illegal_lifecycle_transition_is_rejected(string from, string to)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var person = await DriveTo(service, from, ct);

        var error = await Assert.ThrowsAsync<PersonError>(() => service.UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = to }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a90", ct));

        Assert.Equal("invalid_status_transition", error.Code);
    }

    [Fact]
    public async Task Archived_is_terminal_no_transition_is_ever_allowed_out_of_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var person = await DriveTo(service, "ARCHIVED", ct);

        foreach (var attempt in new[] { "PRE_ENROLLED", "VERIFIED", "ACTIVE", "SUSPENDED", "EXPIRED", "FORMER_ALUMNI", "REVOKED" })
        {
            var error = await Assert.ThrowsAsync<PersonError>(() => service.UpdateStatusAsync(
                person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = attempt }, Actor,
                "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a90", ct));
            Assert.Equal("invalid_status_transition", error.Code);
        }
    }

    [Fact]
    public async Task A_stale_version_is_rejected_before_the_status_is_even_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var person = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<PersonError>(() => service.UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version + 1, Status = "VERIFIED" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a90", ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task UpdateStatus_on_an_unknown_person_is_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<PersonError>(() => service.UpdateStatusAsync(
            "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99", new UpdatePersonStatusRequest { Version = 1, Status = "VERIFIED" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a90", ct));

        Assert.Equal("person_not_found", error.Code);
    }

    [Fact]
    public async Task UpdateStatus_on_another_tenant_s_person_is_not_found_not_forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var serviceA = new PersonService(new FakeCurrentTenantProvider(TenantA), repository, new InMemoryPersonAuditSink(), new PersonLockRegistry());
        var serviceB = new PersonService(new FakeCurrentTenantProvider(TenantB), repository, new InMemoryPersonAuditSink(), new PersonLockRegistry());
        var person = await serviceA.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<PersonError>(() => serviceB.UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = "VERIFIED" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a90", ct));

        Assert.Equal("person_not_found", error.Code);
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task Repeating_the_same_status_is_a_no_op_version_does_not_move()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var person = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var result = await service.UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = "PRE_ENROLLED" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a90", ct);

        Assert.Equal(person.Version, result.Version);
    }

    private static async Task<Person> DriveTo(PersonService service, string status, CancellationToken ct)
    {
        var person = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        var path = status switch
        {
            "PRE_ENROLLED" => Array.Empty<string>(),
            "VERIFIED" => ["VERIFIED"],
            "ACTIVE" => ["VERIFIED", "ACTIVE"],
            "SUSPENDED" => ["VERIFIED", "ACTIVE", "SUSPENDED"],
            "EXPIRED" => ["VERIFIED", "ACTIVE", "EXPIRED"],
            "FORMER_ALUMNI" => ["VERIFIED", "ACTIVE", "FORMER_ALUMNI"],
            "REVOKED" => ["VERIFIED", "ACTIVE", "REVOKED"],
            "ARCHIVED" => ["VERIFIED", "ACTIVE", "ARCHIVED"],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

        var current = person;
        var eventSuffix = 0;
        foreach (var next in path)
        {
            current = await service.UpdateStatusAsync(
                current.Id, new UpdatePersonStatusRequest { Version = current.Version, Status = next }, Actor,
                $"evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a{eventSuffix:D2}", ct);
            eventSuffix++;
        }

        return current;
    }
}
