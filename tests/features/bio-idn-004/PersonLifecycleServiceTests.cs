using Biolog.Features.PersonLifecycle.Api;
using Biolog.Features.PersonLifecycle.Audit;
using Biolog.Features.PersonLifecycle.Domain;
using Xunit;

namespace Biolog.Features.PersonLifecycle.Tests;

/// <summary>BIO-IDN-004-AC01 (explicit transitions), AC02 (emergency suspension revocation event), AC03 (reactivation permission/reason), AC04 (archived records remain traceable).</summary>
public sealed class PersonLifecycleServiceTests
{
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");

    private sealed record Fixture(
        PersonLifecycleService Service, FakePersonLifecycleGateway Gateway, InMemoryPersonLifecycleAuditSink AuditSink, InMemoryCredentialAccessRevocationEventSink RevocationSink);

    private static Fixture NewFixture(string tenantId = TenantA)
    {
        var gateway = new FakePersonLifecycleGateway();
        var auditSink = new InMemoryPersonLifecycleAuditSink();
        var revocationSink = new InMemoryCredentialAccessRevocationEventSink();
        var service = new PersonLifecycleService(
            new FakeCurrentTenantProvider(tenantId), gateway.ForTenant(tenantId), auditSink, revocationSink,
            new InMemoryPendingSuspensionReconciliationStore(), new PersonLifecycleLockRegistry());
        return new Fixture(service, gateway, auditSink, revocationSink);
    }

    private static TransitionRequest Request(string targetStatus, int version, string? reason = null) =>
        new() { TargetStatus = targetStatus, Version = version, Reason = reason };

    [Fact]
    public async Task A_plain_allowed_transition_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Verified);

        var updated = await f.Service.TransitionAsync(PersonId, Request("ACTIVE", 1), assertedPermission: null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(PersonStatus.Active, updated.Status);
        Assert.Equal(2, updated.Version);
    }

    [Fact]
    public async Task Assign_without_a_resolved_tenant_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        var service = new PersonLifecycleService(
            new FakeCurrentTenantProvider(null), f.Gateway.ForTenant(TenantA), f.AuditSink, f.RevocationSink,
            new InMemoryPendingSuspensionReconciliationStore(), new PersonLifecycleLockRegistry());

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => service.TransitionAsync(PersonId, Request("ACTIVE", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("tenant_context_missing", error.Code);
    }

    [Fact]
    public async Task A_malformed_person_id_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => f.Service.TransitionAsync("not-a-person-id", Request("ACTIVE", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task An_unregistered_person_is_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => f.Service.TransitionAsync(PersonId, Request("ACTIVE", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("person_not_found", error.Code);
    }

    // --- AC01: allowed identity transitions are explicit ---

    [Theory]
    [InlineData(PersonStatus.PreEnrolled, PersonStatus.Verified)]
    [InlineData(PersonStatus.PreEnrolled, PersonStatus.Archived)]
    [InlineData(PersonStatus.Verified, PersonStatus.Active)]
    [InlineData(PersonStatus.Verified, PersonStatus.Archived)]
    [InlineData(PersonStatus.Active, PersonStatus.Suspended)]
    [InlineData(PersonStatus.Active, PersonStatus.Expired)]
    [InlineData(PersonStatus.Active, PersonStatus.FormerAlumni)]
    [InlineData(PersonStatus.Active, PersonStatus.Revoked)]
    [InlineData(PersonStatus.Active, PersonStatus.Archived)]
    [InlineData(PersonStatus.Suspended, PersonStatus.Active)]
    [InlineData(PersonStatus.Suspended, PersonStatus.Revoked)]
    [InlineData(PersonStatus.Suspended, PersonStatus.Archived)]
    [InlineData(PersonStatus.Expired, PersonStatus.Active)]
    [InlineData(PersonStatus.Expired, PersonStatus.Archived)]
    [InlineData(PersonStatus.FormerAlumni, PersonStatus.Active)]
    [InlineData(PersonStatus.FormerAlumni, PersonStatus.Archived)]
    [InlineData(PersonStatus.Revoked, PersonStatus.Archived)]
    public void AC01_every_allowed_pair_in_the_explicit_whitelist_is_allowed(PersonStatus from, PersonStatus to)
    {
        Assert.True(LifecycleTransitionPolicy.IsAllowed(from, to));
    }

    [Theory]
    [InlineData(PersonStatus.PreEnrolled, PersonStatus.Active)]
    [InlineData(PersonStatus.PreEnrolled, PersonStatus.Suspended)]
    [InlineData(PersonStatus.Verified, PersonStatus.Suspended)]
    [InlineData(PersonStatus.Verified, PersonStatus.PreEnrolled)]
    [InlineData(PersonStatus.Active, PersonStatus.PreEnrolled)]
    [InlineData(PersonStatus.Active, PersonStatus.Verified)]
    [InlineData(PersonStatus.Archived, PersonStatus.Active)]
    [InlineData(PersonStatus.Archived, PersonStatus.PreEnrolled)]
    [InlineData(PersonStatus.Revoked, PersonStatus.Active)]
    [InlineData(PersonStatus.Active, PersonStatus.Active)]
    public void AC01_every_other_pair_is_rejected(PersonStatus from, PersonStatus to)
    {
        Assert.False(LifecycleTransitionPolicy.IsAllowed(from, to));
    }

    [Fact]
    public async Task AC01_a_transition_outside_the_whitelist_is_rejected_before_the_gateway_is_ever_called()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.PreEnrolled);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => f.Service.TransitionAsync(PersonId, Request("ACTIVE", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("transition_not_allowed", error.Code);
        Assert.Equal(PersonStatus.PreEnrolled, f.Gateway[PersonId]!.Status); // untouched
        Assert.Empty(f.AuditSink.Events);
    }

    [Fact]
    public async Task AC01_archived_is_terminal_no_further_transition_is_ever_allowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Archived, version: 5);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => f.Service.TransitionAsync(PersonId, Request("ACTIVE", 5), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("transition_not_allowed", error.Code);
    }

    // --- AC02: emergency suspension emits a credential/access revocation event ---

    [Fact]
    public async Task AC02_emergency_suspension_publishes_a_credential_access_revocation_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Active);

        await f.Service.TransitionAsync(PersonId, Request("SUSPENDED", 1, reason: "reported stolen badge"), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var evt = Assert.Single(f.RevocationSink.Events);
        Assert.Equal(PersonId, evt.PersonId);
        Assert.Equal(TenantA, evt.TenantId);
        Assert.Equal("reported stolen badge", evt.Reason);
        Assert.Equal("person_emergency_suspended", evt.ReasonCode);
    }

    [Fact]
    public async Task AC02_emergency_suspension_does_not_require_a_reason()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Active);

        var updated = await f.Service.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(PersonStatus.Suspended, updated.Status);
        Assert.Single(f.RevocationSink.Events);
    }

    [Fact]
    public async Task AC02_a_non_suspension_transition_never_publishes_a_revocation_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Verified);

        await f.Service.TransitionAsync(PersonId, Request("ACTIVE", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Empty(f.RevocationSink.Events);
    }

    [Fact]
    public async Task AC02_the_ordinary_lifecycle_audit_event_is_also_written_for_a_suspension()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Active);

        await f.Service.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var evt = Assert.Single(f.AuditSink.Events);
        Assert.Equal("person_lifecycle.emergency_suspend", evt.Action);
        Assert.Equal("ACTIVE", evt.Detail["from_status"]);
        Assert.Equal("SUSPENDED", evt.Detail["to_status"]);
    }

    // --- AC03: reactivation requires permission and audit reason ---

    [Fact]
    public async Task AC03_reactivation_without_the_permission_header_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Suspended);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(() => f.Service.TransitionAsync(
            PersonId, Request("ACTIVE", 1, reason: "cleared by security"), assertedPermission: null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("permission_required", error.Code);
        Assert.Equal(403, error.Status);
    }

    [Fact]
    public async Task AC03_reactivation_with_the_wrong_permission_string_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Suspended);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(() => f.Service.TransitionAsync(
            PersonId, Request("ACTIVE", 1, reason: "cleared"), assertedPermission: "person.suspend", Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task AC03_reactivation_without_a_reason_is_rejected_even_with_permission()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Suspended);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(() => f.Service.TransitionAsync(
            PersonId, Request("ACTIVE", 1), assertedPermission: "person.reactivate", Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("reason_required", error.Code);
    }

    [Fact]
    public async Task AC03_reactivation_with_permission_and_reason_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Suspended);

        var updated = await f.Service.TransitionAsync(
            PersonId, Request("ACTIVE", 1, reason: "cleared by security review"), assertedPermission: "person.reactivate", Actor,
            "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(PersonStatus.Active, updated.Status);
        var evt = f.AuditSink.Events.Single(e => e.Action == "person_lifecycle.reactivate");
        Assert.Equal("cleared by security review", evt.Detail["reason"]);
    }

    [Fact]
    public async Task AC03_the_initial_activation_from_VERIFIED_needs_neither_permission_nor_reason()
    {
        // Distinguishes "reactivation" (SUSPENDED -> ACTIVE) from ordinary initial activation
        // (VERIFIED -> ACTIVE) - only the former is gated by AC03.
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Verified);

        var updated = await f.Service.TransitionAsync(PersonId, Request("ACTIVE", 1), assertedPermission: null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal(PersonStatus.Active, updated.Status);
    }

    // --- AC04: archived records remain traceable ---

    [Fact]
    public async Task AC04_an_archived_person_s_snapshot_is_still_readable()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Active);
        await f.Service.TransitionAsync(PersonId, Request("ARCHIVED", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var snapshot = await f.Service.GetSnapshotAsync(PersonId, ct);

        Assert.NotNull(snapshot);
        Assert.Equal(PersonStatus.Archived, snapshot!.Status);
    }

    [Fact]
    public async Task AC04_the_full_lifecycle_event_history_survives_archival()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Verified);
        await f.Service.TransitionAsync(PersonId, Request("ACTIVE", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        await f.Service.TransitionAsync(PersonId, Request("SUSPENDED", 2), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);
        await f.Service.TransitionAsync(
            PersonId, Request("ACTIVE", 3, reason: "cleared"), "person.reactivate", Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);
        await f.Service.TransitionAsync(PersonId, Request("ARCHIVED", 4), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a82", ct);

        var events = await f.Service.ListEventsAsync(PersonId, ct);

        Assert.Equal(4, events.Count);
        Assert.Equal(["person_lifecycle.transition", "person_lifecycle.emergency_suspend", "person_lifecycle.reactivate", "person_lifecycle.transition"],
            events.Select(e => e.Action));
    }

    [Fact]
    public async Task AC04_a_further_transition_attempt_on_an_archived_person_is_rejected_but_reads_still_work()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Active);
        await f.Service.TransitionAsync(PersonId, Request("ARCHIVED", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => f.Service.TransitionAsync(PersonId, Request("ACTIVE", 2), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));
        Assert.Equal("transition_not_allowed", error.Code);

        var events = await f.Service.ListEventsAsync(PersonId, ct);
        Assert.Single(events); // the original archive event, still there
    }

    // --- Tenant scoping and concurrency ---

    [Fact]
    public async Task A_person_registered_in_a_different_tenant_is_not_found_not_forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture(tenantId: TenantB);
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Active);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => f.Service.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("person_not_found", error.Code);
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task A_stale_version_is_a_concurrency_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Active, version: 3);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => f.Service.TransitionAsync(PersonId, Request("SUSPENDED", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task A_missing_version_is_a_validation_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Active);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(() => f.Service.TransitionAsync(
            PersonId, new TransitionRequest { TargetStatus = "SUSPENDED" }, null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task An_unrecognised_target_status_is_a_validation_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Active);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(
            () => f.Service.TransitionAsync(PersonId, Request("NOT_A_STATUS", 1), null, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task A_reason_over_the_length_bound_is_a_validation_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = NewFixture();
        f.Gateway.Seed(PersonId, TenantA, PersonStatus.Suspended);

        var error = await Assert.ThrowsAsync<PersonLifecycleError>(() => f.Service.TransitionAsync(
            PersonId, Request("ACTIVE", 1, reason: new string('x', 501)), "person.reactivate", Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }
}
