using Biolog.Features.PersonProfileRegistry.Api;
using Biolog.Features.PersonProfileRegistry.Audit;
using Biolog.Features.PersonProfileRegistry.Domain;
using Xunit;

namespace Biolog.Features.PersonProfileRegistry.Tests;

/// <summary>BIO-IDN-002-AC01 (multiple simultaneous profiles), AC02 (hierarchy targeting), AC03 (independent activation/expiry), AC04 (no duplicate PERSON).</summary>
public sealed class PersonProfileServiceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");

    private static PersonProfileService NewService(string? tenantId = TenantA) => new(
        new FakeCurrentTenantProvider(tenantId), new InMemoryPersonProfileRepository(), new InMemoryPersonProfileAuditSink(), new PersonProfileLockRegistry(),
        new FakePersonExistenceGateway());

    private static AssignProfileRequest ValidRequest(string type = "STUDENT") => new() { Type = type };

    [Fact]
    public async Task Assign_creates_an_active_profile_in_the_resolved_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var profile = await service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(PersonId, profile.PersonId);
        Assert.Equal(TenantA, profile.TenantId);
        Assert.Equal(ProfileType.Student, profile.Type);
        Assert.Equal(ProfileStatus.Active, profile.Status);
        Assert.Equal(1, profile.Version);
    }

    [Fact]
    public async Task Assign_without_a_resolved_tenant_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(tenantId: null);

        var error = await Assert.ThrowsAsync<PersonProfileError>(
            () => service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("tenant_context_missing", error.Code);
    }

    [Fact]
    public async Task Assign_rejects_a_malformed_person_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<PersonProfileError>(
            () => service.AssignAsync("not-a-person-id", ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task AC01_a_person_can_hold_two_different_profile_types_at_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        await service.AssignAsync(PersonId, ValidRequest("STUDENT"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        await service.AssignAsync(PersonId, ValidRequest("EMPLOYEE"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var all = await service.ListAsync(PersonId, ct);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, p => p.Type == ProfileType.Student);
        Assert.Contains(all, p => p.Type == ProfileType.Employee);
    }

    [Fact]
    public async Task AC04_assigning_a_second_profile_type_never_creates_a_second_person()
    {
        // There is no "create person" operation anywhere on this service - AssignAsync
        // always attaches to the SAME PersonId it was given.
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var first = await service.AssignAsync(PersonId, ValidRequest("STUDENT"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var second = await service.AssignAsync(PersonId, ValidRequest("EMPLOYEE"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        Assert.Equal(first.PersonId, second.PersonId);
    }

    [Fact]
    public async Task AC04_assigning_the_same_profile_type_twice_is_rejected_as_a_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        await service.AssignAsync(PersonId, ValidRequest("STUDENT"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<PersonProfileError>(
            () => service.AssignAsync(PersonId, ValidRequest("STUDENT"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("duplicate_profile_type", error.Code);
    }

    [Fact]
    public void No_person_creation_method_exists_anywhere_on_this_service()
    {
        // BIO-IDN-002-AC04, adversarial by reflection.
        var methods = typeof(PersonProfileService).GetMethods().Select(m => m.Name.ToLowerInvariant());
        Assert.DoesNotContain(methods, name => name.Contains("createperson") || name.Contains("registerperson"));
    }

    [Theory]
    [InlineData("TENANT_DEFINED", "Coach")]
    public async Task AC01_a_tenant_defined_profile_requires_a_custom_label(string type, string label)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var profile = await service.AssignAsync(
            PersonId, new AssignProfileRequest { Type = type, CustomLabel = label }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(label, profile.CustomLabel);
    }

    [Fact]
    public async Task Assign_rejects_TENANT_DEFINED_without_a_custom_label()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => service.AssignAsync(
            PersonId, new AssignProfileRequest { Type = "TENANT_DEFINED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Assign_rejects_a_custom_label_on_a_fixed_profile_type()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => service.AssignAsync(
            PersonId, new AssignProfileRequest { Type = "STUDENT", CustomLabel = "Whatever" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task AC02_a_profile_can_target_organisation_campus_and_branch()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var profile = await service.AssignAsync(
            PersonId,
            new AssignProfileRequest { Type = "STUDENT", OrganisationId = "org-main", CampusId = "campus-north", BranchId = "branch-3" },
            Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal("org-main", profile.OrganisationId);
        Assert.Equal("campus-north", profile.CampusId);
        Assert.Equal("branch-3", profile.BranchId);
    }

    [Fact]
    public async Task AC02_hierarchy_fields_are_optional()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var profile = await service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Null(profile.OrganisationId);
        Assert.Null(profile.CampusId);
        Assert.Null(profile.BranchId);
    }

    [Fact]
    public async Task AC03_expires_at_must_be_a_real_UTC_instant()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => service.AssignAsync(
            PersonId, new AssignProfileRequest { Type = "VISITOR", ExpiresAt = "2026-09-20" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task AC03_a_profile_with_a_future_expiry_is_currently_in_effect()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var profile = await service.AssignAsync(
            PersonId, new AssignProfileRequest { Type = "VISITOR", ExpiresAt = "2999-01-01T00:00:00.000Z" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.True(profile.IsCurrentlyInEffect(Identifiers.UtcInstant.Now()));
    }

    [Fact]
    public async Task AC03_a_profile_with_a_past_expiry_is_not_currently_in_effect_even_while_status_is_still_ACTIVE()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var profile = await service.AssignAsync(
            PersonId, new AssignProfileRequest { Type = "VISITOR", ExpiresAt = "2000-01-01T00:00:00.000Z" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(ProfileStatus.Active, profile.Status); // status itself never auto-flips - see ProfileStatus header
        Assert.False(profile.IsCurrentlyInEffect(Identifiers.UtcInstant.Now()));
    }

    [Fact]
    public async Task AC03_two_profiles_for_the_same_person_have_fully_independent_activation_and_expiry()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var student = await service.AssignAsync(
            PersonId, new AssignProfileRequest { Type = "STUDENT", ExpiresAt = "2999-01-01T00:00:00.000Z" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        await service.RevokeAsync(PersonId, ProfileType.Student, new RevokeProfileRequest { Version = student.Version }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);
        var employee = await service.AssignAsync(PersonId, ValidRequest("EMPLOYEE"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        var revokedStudent = await service.GetAsync(PersonId, ProfileType.Student, ct);
        Assert.Equal(ProfileStatus.Revoked, revokedStudent!.Status);
        Assert.Equal(ProfileStatus.Active, employee.Status); // revoking one never touches the other
    }

    [Fact]
    public async Task Revoke_is_the_only_transition_and_is_terminal()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var profile = await service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var revoked = await service.RevokeAsync(
            PersonId, ProfileType.Student, new RevokeProfileRequest { Version = profile.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        Assert.Equal(ProfileStatus.Revoked, revoked.Status);
    }

    [Fact]
    public async Task Revoking_an_already_revoked_profile_is_a_no_op()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var profile = await service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        var revoked = await service.RevokeAsync(
            PersonId, ProfileType.Student, new RevokeProfileRequest { Version = profile.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var again = await service.RevokeAsync(
            PersonId, ProfileType.Student, new RevokeProfileRequest { Version = revoked.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        Assert.Equal(revoked.Version, again.Version); // no-op: version does not move again
    }

    [Fact]
    public async Task A_stale_version_on_revoke_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var profile = await service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => service.RevokeAsync(
            PersonId, ProfileType.Student, new RevokeProfileRequest { Version = profile.Version + 1 }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task Get_returns_null_for_a_profile_registered_in_a_different_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var serviceA = new PersonProfileService(new FakeCurrentTenantProvider(TenantA), repository, new InMemoryPersonProfileAuditSink(), new PersonProfileLockRegistry(), new FakePersonExistenceGateway());
        var serviceB = new PersonProfileService(new FakeCurrentTenantProvider(TenantB), repository, new InMemoryPersonProfileAuditSink(), new PersonProfileLockRegistry(), new FakePersonExistenceGateway());
        await serviceA.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var crossTenant = await serviceB.GetAsync(PersonId, ProfileType.Student, ct);

        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task RevokeAsync_on_another_tenant_s_profile_is_not_found_not_forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var serviceA = new PersonProfileService(new FakeCurrentTenantProvider(TenantA), repository, new InMemoryPersonProfileAuditSink(), new PersonProfileLockRegistry(), new FakePersonExistenceGateway());
        var serviceB = new PersonProfileService(new FakeCurrentTenantProvider(TenantB), repository, new InMemoryPersonProfileAuditSink(), new PersonProfileLockRegistry(), new FakePersonExistenceGateway());
        var profile = await serviceA.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => serviceB.RevokeAsync(
            PersonId, ProfileType.Student, new RevokeProfileRequest { Version = profile.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("person_profile_not_found", error.Code);
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task QA01_assigning_a_profile_for_a_person_that_does_not_exist_in_the_resolved_tenant_is_rejected()
    {
        // BIO-IDN-002-QA-01 (Seyi round 1): a syntactically well-formed but entirely
        // unregistered person id must never succeed.
        var ct = TestContext.Current.CancellationToken;
        var service = new PersonProfileService(
            new FakeCurrentTenantProvider(TenantA), new InMemoryPersonProfileRepository(), new InMemoryPersonProfileAuditSink(),
            new PersonProfileLockRegistry(), new FakePersonExistenceGateway(existingPersonIds: new HashSet<string>()));

        var error = await Assert.ThrowsAsync<PersonProfileError>(
            () => service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("person_not_found", error.Code);
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task QA01_the_existence_check_fails_closed_no_audit_event_and_nothing_is_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var sink = new InMemoryPersonProfileAuditSink();
        var service = new PersonProfileService(
            new FakeCurrentTenantProvider(TenantA), repository, sink, new PersonProfileLockRegistry(),
            new FakePersonExistenceGateway(existingPersonIds: new HashSet<string>()));

        await Assert.ThrowsAsync<PersonProfileError>(
            () => service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Empty(sink.Events);
        Assert.Empty(await repository.ListByPersonAsync(TenantA, PersonId, ct));
    }

    // A tenant-B caller referencing a tenant-A person is deliberately NOT re-proven here with
    // a second fake-gateway shape: FakePersonExistenceGateway matches by person id alone and
    // has no tenant concept of its own (that scoping belongs to the real production adapter,
    // which delegates to BIO-IDN-001's own tenant-scoped PersonService.GetAsync - see that
    // interface's header). The genuinely tenant-aware version of this exact scenario is
    // PersonProfileHostChannelTests.Assigning_a_profile_for_a_tenant_A_person_through_tenant_B_s_host_is_rejected_identically_to_unknown,
    // through the real packaged host - a unit-level double asserting the same thing here would
    // only be testing the double's own configuration, not this service's logic.
}
