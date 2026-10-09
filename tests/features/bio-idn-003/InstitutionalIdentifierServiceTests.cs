using Biolog.Features.InstitutionalIdentifiers.Api;
using Biolog.Features.InstitutionalIdentifiers.Audit;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Xunit;

namespace Biolog.Features.InstitutionalIdentifiers.Tests;

/// <summary>BIO-IDN-003-AC01 (typed and tenant-scoped), AC02 (configurable uniqueness), AC03 (history/status preserved), AC04 (never a credential id).</summary>
public sealed class InstitutionalIdentifierServiceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonId2 = "per_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14";
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");

    private static InstitutionalIdentifierService NewService(string? tenantId = TenantA) => new(
        new FakeCurrentTenantProvider(tenantId), new InMemoryInstitutionalIdentifierRepository(), new InMemoryIdentifierUniquenessRuleRepository(),
        new InMemoryInstitutionalIdentifierAuditSink(), new InstitutionalIdentifierLockRegistry(), new IdentifierUniquenessRuleLockRegistry(),
        new FakePersonExistenceGateway());

    private static (InstitutionalIdentifierService Identifiers, IdentifierUniquenessRuleService Rules) NewServices(string? tenantId = TenantA)
    {
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var ruleRepository = new InMemoryIdentifierUniquenessRuleRepository();
        var sink = new InMemoryInstitutionalIdentifierAuditSink();
        var tenant = new FakeCurrentTenantProvider(tenantId);
        // Shared, matching production wiring (BioIdn003Module registers this as a single
        // Singleton both services resolve) - BIO-IDN-003-QA-01 depends on both services using
        // the SAME lock table for the same (tenant, type, custom_label) key.
        var uniquenessLockRegistry = new IdentifierUniquenessRuleLockRegistry();
        var identifiers = new InstitutionalIdentifierService(
            tenant, repository, ruleRepository, sink, new InstitutionalIdentifierLockRegistry(), uniquenessLockRegistry, new FakePersonExistenceGateway());
        var rules = new IdentifierUniquenessRuleService(tenant, ruleRepository, repository, sink, uniquenessLockRegistry);
        return (identifiers, rules);
    }

    private static AssignIdentifierRequest ValidRequest(string type = "MATRICULATION_NUMBER", string value = "MAT/2026/00123") => new() { Type = type, Value = value };

    [Fact]
    public async Task Assign_creates_an_active_identifier_in_the_resolved_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var identifier = await service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(PersonId, identifier.PersonId);
        Assert.Equal(TenantA, identifier.TenantId);
        Assert.Equal(IdentifierType.MatriculationNumber, identifier.Type);
        Assert.Equal(IdentifierStatus.Active, identifier.Status);
        Assert.Equal(1, identifier.SequenceNumber);
        Assert.Equal(1, identifier.Version);
    }

    [Fact]
    public async Task Assign_without_a_resolved_tenant_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(tenantId: null);

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(
            () => service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("tenant_context_missing", error.Code);
    }

    [Fact]
    public async Task Assign_rejects_a_malformed_person_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(
            () => service.AssignAsync("not-a-person-id", ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    // --- AC01: typed and tenant-scoped ---

    [Fact]
    public async Task AC01_an_identifier_carries_its_type_and_is_only_visible_in_its_own_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var serviceA = new InstitutionalIdentifierService(
            new FakeCurrentTenantProvider(TenantA), repository, new InMemoryIdentifierUniquenessRuleRepository(), new InMemoryInstitutionalIdentifierAuditSink(),
            new InstitutionalIdentifierLockRegistry(), new IdentifierUniquenessRuleLockRegistry(), new FakePersonExistenceGateway());
        var serviceB = new InstitutionalIdentifierService(
            new FakeCurrentTenantProvider(TenantB), repository, new InMemoryIdentifierUniquenessRuleRepository(), new InMemoryInstitutionalIdentifierAuditSink(),
            new InstitutionalIdentifierLockRegistry(), new IdentifierUniquenessRuleLockRegistry(), new FakePersonExistenceGateway());
        await serviceA.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var crossTenant = await serviceB.GetActiveAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, ct);

        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task AC01_a_person_can_hold_a_matriculation_number_and_a_staff_number_at_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        await service.AssignAsync(PersonId, ValidRequest("MATRICULATION_NUMBER", "MAT/2026/00123"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        await service.AssignAsync(PersonId, ValidRequest("STAFF_NUMBER", "STF-00456"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var all = await service.ListAsync(PersonId, ct);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, i => i.Type == IdentifierType.MatriculationNumber);
        Assert.Contains(all, i => i.Type == IdentifierType.StaffNumber);
    }

    [Theory]
    [InlineData("TENANT_DEFINED", "Library Card Number")]
    public async Task AC01_a_tenant_defined_identifier_requires_a_custom_label(string type, string label)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var identifier = await service.AssignAsync(
            PersonId, new AssignIdentifierRequest { Type = type, CustomLabel = label, Value = "LIB-000001" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(label, identifier.CustomLabel);
    }

    [Fact]
    public async Task Assign_rejects_TENANT_DEFINED_without_a_custom_label()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => service.AssignAsync(
            PersonId, new AssignIdentifierRequest { Type = "TENANT_DEFINED", Value = "X" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Assign_rejects_a_custom_label_on_a_fixed_type()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => service.AssignAsync(
            PersonId, new AssignIdentifierRequest { Type = "MATRICULATION_NUMBER", CustomLabel = "Whatever", Value = "X" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Assign_rejects_a_blank_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => service.AssignAsync(
            PersonId, new AssignIdentifierRequest { Type = "STAFF_NUMBER", Value = "   " }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task A_second_identifier_of_the_same_type_while_one_is_still_active_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        await service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(
            () => service.AssignAsync(PersonId, ValidRequest(value: "MAT/2026/99999"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("identifier_already_active", error.Code);
    }

    // --- AC02: uniqueness rules are configurable per type ---

    [Fact]
    public async Task AC02_by_default_the_same_value_cannot_be_assigned_to_two_different_people_for_the_same_type()
    {
        var ct = TestContext.Current.CancellationToken;
        var (identifiers, _) = NewServices();
        await identifiers.AssignAsync(PersonId, ValidRequest(value: "MAT/2026/00123"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => identifiers.AssignAsync(
            PersonId2, ValidRequest(value: "MAT/2026/00123"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("duplicate_identifier_value", error.Code);
    }

    [Fact]
    public async Task AC02_disabling_enforcement_for_a_type_allows_the_same_value_for_two_people()
    {
        var ct = TestContext.Current.CancellationToken;
        var (identifiers, rules) = NewServices();
        await rules.SetRuleAsync(IdentifierType.TenantDefined, "Notes Reference", enforced: false, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78", ct);

        await identifiers.AssignAsync(
            PersonId, new AssignIdentifierRequest { Type = "TENANT_DEFINED", CustomLabel = "Notes Reference", Value = "SAME" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        var second = await identifiers.AssignAsync(
            PersonId2, new AssignIdentifierRequest { Type = "TENANT_DEFINED", CustomLabel = "Notes Reference", Value = "SAME" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        Assert.Equal("SAME", second.Value);
    }

    [Fact]
    public async Task AC02_uniqueness_is_scoped_per_type_a_matric_number_and_a_staff_number_may_share_a_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var (identifiers, _) = NewServices();
        await identifiers.AssignAsync(PersonId, new AssignIdentifierRequest { Type = "MATRICULATION_NUMBER", Value = "0001" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var second = await identifiers.AssignAsync(PersonId2, new AssignIdentifierRequest { Type = "STAFF_NUMBER", Value = "0001" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        Assert.Equal("0001", second.Value);
    }

    [Fact]
    public async Task AC02_a_retired_value_is_never_silently_recycled_when_enforcement_is_on()
    {
        var ct = TestContext.Current.CancellationToken;
        var (identifiers, _) = NewServices();
        var first = await identifiers.AssignAsync(PersonId, ValidRequest(value: "MAT/2026/00123"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        await identifiers.RetireAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = first.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => identifiers.AssignAsync(
            PersonId2, ValidRequest(value: "MAT/2026/00123"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct));

        Assert.Equal("duplicate_identifier_value", error.Code);
    }

    [Fact]
    public async Task AC02_GetRule_returns_null_and_the_response_default_when_nothing_was_ever_set()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, rules) = NewServices();

        var rule = await rules.GetRuleAsync(IdentifierType.MatriculationNumber, customLabel: null, ct);

        Assert.Null(rule);
    }

    [Fact]
    public async Task AC02_SetRule_persists_and_GetRule_reflects_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, rules) = NewServices();

        await rules.SetRuleAsync(IdentifierType.StaffNumber, customLabel: null, enforced: false, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        var rule = await rules.GetRuleAsync(IdentifierType.StaffNumber, customLabel: null, ct);

        Assert.NotNull(rule);
        Assert.False(rule!.Enforced);
        Assert.Equal(1, rule.Version);
    }

    [Fact]
    public async Task QA01_enabling_enforcement_over_an_already_duplicated_scope_is_rejected()
    {
        // BIO-IDN-003-QA-01 (Seyi round 1, closure item 2): the only way two identical values
        // can exist in one scope is while enforcement was OFF - turning it back ON must never
        // silently record a rule the existing data already violates.
        var ct = TestContext.Current.CancellationToken;
        var (identifiers, rules) = NewServices();
        await rules.SetRuleAsync(IdentifierType.TenantDefined, "Notes Reference", enforced: false, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78", ct);
        await identifiers.AssignAsync(
            PersonId, new AssignIdentifierRequest { Type = "TENANT_DEFINED", CustomLabel = "Notes Reference", Value = "SAME" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        await identifiers.AssignAsync(
            PersonId2, new AssignIdentifierRequest { Type = "TENANT_DEFINED", CustomLabel = "Notes Reference", Value = "SAME" }, Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => rules.SetRuleAsync(
            IdentifierType.TenantDefined, "Notes Reference", enforced: true, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct));

        Assert.Equal("uniqueness_already_violated", error.Code);
        var rule = await rules.GetRuleAsync(IdentifierType.TenantDefined, "Notes Reference", ct);
        Assert.False(rule!.Enforced); // rejected, not silently recorded
    }

    [Fact]
    public async Task QA01_enabling_enforcement_over_a_clean_scope_still_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var (identifiers, rules) = NewServices();
        await identifiers.AssignAsync(
            PersonId, new AssignIdentifierRequest { Type = "STAFF_NUMBER", Value = "STF-0001" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        await identifiers.AssignAsync(
            PersonId2, new AssignIdentifierRequest { Type = "STAFF_NUMBER", Value = "STF-0002" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var rule = await rules.SetRuleAsync(IdentifierType.StaffNumber, customLabel: null, enforced: true, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        Assert.True(rule.Enforced);
    }

    // --- AC03: history/status is preserved ---

    [Fact]
    public async Task AC03_retiring_an_identifier_preserves_the_record_and_marks_it_retired()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var identifier = await service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var retired = await service.RetireAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = identifier.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        Assert.Equal(IdentifierStatus.Retired, retired.Status);
        Assert.NotNull(retired.RetiredAt);
        Assert.Equal(identifier.Value, retired.Value); // the historical record's value is never edited
    }

    [Fact]
    public async Task AC03_a_reissued_identifier_is_a_new_record_at_the_next_sequence_number_and_the_old_one_survives_as_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var first = await service.AssignAsync(PersonId, ValidRequest(value: "MAT/2026/00123"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        await service.RetireAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = first.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var second = await service.AssignAsync(PersonId, ValidRequest(value: "MAT/2026/00999"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        Assert.Equal(2, second.SequenceNumber);
        var history = await service.ListHistoryAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, ct);
        Assert.Equal(2, history.Count);
        Assert.Equal(IdentifierStatus.Retired, history[0].Status);
        Assert.Equal("MAT/2026/00123", history[0].Value);
        Assert.Equal(IdentifierStatus.Active, history[1].Status);
        Assert.Equal("MAT/2026/00999", history[1].Value);
    }

    [Fact]
    public async Task Retire_is_the_only_transition_and_is_terminal_idempotent_no_op()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var identifier = await service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        var retired = await service.RetireAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = identifier.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var again = await service.RetireAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = retired.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        Assert.Equal(retired.Version, again.Version); // no-op: version does not move again
    }

    [Fact]
    public async Task A_stale_version_on_retire_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var identifier = await service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => service.RetireAsync(
            PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = identifier.Version + 1 }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task RetireAsync_on_another_tenant_s_identifier_is_not_found_not_forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var serviceA = new InstitutionalIdentifierService(
            new FakeCurrentTenantProvider(TenantA), repository, new InMemoryIdentifierUniquenessRuleRepository(), new InMemoryInstitutionalIdentifierAuditSink(),
            new InstitutionalIdentifierLockRegistry(), new IdentifierUniquenessRuleLockRegistry(), new FakePersonExistenceGateway());
        var serviceB = new InstitutionalIdentifierService(
            new FakeCurrentTenantProvider(TenantB), repository, new InMemoryIdentifierUniquenessRuleRepository(), new InMemoryInstitutionalIdentifierAuditSink(),
            new InstitutionalIdentifierLockRegistry(), new IdentifierUniquenessRuleLockRegistry(), new FakePersonExistenceGateway());
        var identifier = await serviceA.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => serviceB.RetireAsync(
            PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = identifier.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("institutional_identifier_not_found", error.Code);
        Assert.Equal(404, error.Status);
    }

    // --- AC04: never a credential id ---

    [Fact]
    public void No_field_or_method_anywhere_on_this_service_or_the_aggregate_names_a_credential_id()
    {
        // BIO-IDN-003-AC04, adversarial by reflection: this feature's whole vocabulary is
        // "identifier", never "credential" - the opaque electronic credential id lives in
        // BIO-CRD-001's own crd_ aggregate, never here.
        var serviceMembers = typeof(InstitutionalIdentifierService).GetMethods().Select(m => m.Name.ToLowerInvariant());
        var aggregateMembers = typeof(InstitutionalIdentifier).GetProperties().Select(p => p.Name.ToLowerInvariant());
        Assert.DoesNotContain(serviceMembers, name => name.Contains("credential"));
        Assert.DoesNotContain(aggregateMembers, name => name.Contains("credential"));
    }

    [Fact]
    public void No_authentication_or_verification_method_exists_anywhere_on_this_service()
    {
        // AC04's other half: this service can never be used to authenticate a person - it has
        // no login/verify/authenticate operation of any kind.
        var methods = typeof(InstitutionalIdentifierService).GetMethods().Select(m => m.Name.ToLowerInvariant());
        Assert.DoesNotContain(methods, name => name.Contains("authenticate") || name.Contains("login") || name.Contains("verify"));
    }

    // --- Applying BIO-IDN-002-QA-01 from the start ---

    [Fact]
    public async Task Assigning_an_identifier_for_a_person_that_does_not_exist_in_the_resolved_tenant_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new InstitutionalIdentifierService(
            new FakeCurrentTenantProvider(TenantA), new InMemoryInstitutionalIdentifierRepository(), new InMemoryIdentifierUniquenessRuleRepository(),
            new InMemoryInstitutionalIdentifierAuditSink(), new InstitutionalIdentifierLockRegistry(), new IdentifierUniquenessRuleLockRegistry(),
            new FakePersonExistenceGateway(existingPersonIds: new HashSet<string>()));

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(
            () => service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("person_not_found", error.Code);
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task The_existence_check_fails_closed_no_audit_event_and_nothing_is_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var sink = new InMemoryInstitutionalIdentifierAuditSink();
        var service = new InstitutionalIdentifierService(
            new FakeCurrentTenantProvider(TenantA), repository, new InMemoryIdentifierUniquenessRuleRepository(), sink, new InstitutionalIdentifierLockRegistry(), new IdentifierUniquenessRuleLockRegistry(),
            new FakePersonExistenceGateway(existingPersonIds: new HashSet<string>()));

        await Assert.ThrowsAsync<InstitutionalIdentifierError>(
            () => service.AssignAsync(PersonId, ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Empty(sink.Events);
        Assert.Empty(await repository.ListByPersonAsync(TenantA, PersonId, ct));
    }

    // A tenant-B caller referencing a tenant-A person is deliberately NOT re-proven here with a
    // second fake-gateway shape - see PersonProfileServiceTests' identical note. The genuinely
    // tenant-aware version of this exact scenario is
    // InstitutionalIdentifierHostChannelTests.BIOIDN003_a_tenant_B_caller_cannot_attach_an_identifier_to_a_real_tenant_A_person,
    // through the real packaged host.
}
