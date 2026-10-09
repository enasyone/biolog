using Biolog.Features.Visitors.Api;
using Biolog.Features.Visitors.Audit;
using Biolog.Features.Visitors.Domain;
using Xunit;

namespace Biolog.Features.Visitors.Tests;

/// <summary>AC01 (preregistration + optional PERSON link), AC02 (host approval, auditable), AC03 (configurable fields enforced at check-in) and AC04 (automatic expiry) against <see cref="VisitorRecordService"/>.</summary>
public class VisitorRecordServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string HostPersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string VisitorPersonId = "per_0199e0c2-1111-7222-8333-444455556666";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-frontdesk-1");

    private static VisitorRecordService MakeService(
        out InMemoryVisitorRecordRepository repository, out InMemoryVisitorAuditSink auditSink, out InMemoryVisitorConfigRepository configRepository,
        IPermissionGateway? permissionGateway = null, IPersonExistenceGateway? personGateway = null)
    {
        repository = new InMemoryVisitorRecordRepository();
        auditSink = new InMemoryVisitorAuditSink();
        configRepository = new InMemoryVisitorConfigRepository();
        return new VisitorRecordService(
            new FakeCurrentTenantProvider(TenantId), repository, configRepository, auditSink,
            new VisitorRecordLockRegistry(), personGateway ?? new FakePersonExistenceGateway(), permissionGateway ?? new FakePermissionGateway());
    }

    private static PreregisterVisitorRequest ValidRequest(string? personId = null) => new()
    {
        PersonId = personId,
        FullName = "Amara Adeleke",
        ContactEmail = "amara@example.com",
        HostPersonId = HostPersonId,
        TemporaryCredentialRequested = false,
    };

    [Fact]
    public async Task AC01_a_a_preregistered_visitor_carries_full_name_and_host_with_no_person_link_required()
    {
        var service = MakeService(out _, out _, out _);
        var record = await service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        Assert.Equal("Amara Adeleke", record.FullName);
        Assert.Equal(HostPersonId, record.HostPersonId);
        Assert.Null(record.PersonId); // AC01: a lead needs no permanent identity
        Assert.Equal(HostApprovalStatus.Pending, record.HostApprovalStatus);
        Assert.Equal(VisitStatus.Preregistered, record.VisitStatus);
    }

    [Fact]
    public async Task AC01_b_a_repeat_visitor_can_be_linked_to_their_existing_person()
    {
        var service = MakeService(out _, out _, out _);
        var record = await service.PreregisterAsync(ValidRequest(VisitorPersonId), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        Assert.Equal(VisitorPersonId, record.PersonId);
    }

    [Fact]
    public async Task AC01_c_an_optional_person_id_that_does_not_exist_is_rejected()
    {
        var service = MakeService(out _, out _, out _, personGateway: new FakePersonExistenceGateway(new HashSet<string> { HostPersonId }));
        var ex = await Assert.ThrowsAsync<VisitorError>(() =>
            service.PreregisterAsync(ValidRequest(VisitorPersonId), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));

        Assert.Equal("person_not_found", ex.Code);
    }

    [Fact]
    public async Task AC01_d_a_host_person_id_that_does_not_exist_is_rejected()
    {
        var service = MakeService(out _, out _, out _, personGateway: new FakePersonExistenceGateway(new HashSet<string>()));
        var ex = await Assert.ThrowsAsync<VisitorError>(() =>
            service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));

        Assert.Equal("person_not_found", ex.Code);
    }

    [Fact]
    public async Task AC02_a_approving_requires_the_decide_permission()
    {
        var denyingGateway = new FakePermissionGateway(new HashSet<(string, string)>());
        var service = MakeService(out var repository, out _, out _, denyingGateway);
        var record = await service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var ex = await Assert.ThrowsAsync<VisitorError>(() =>
            service.ApproveAsync(record.Id, new VersionedVisitorRequest { Version = 1 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26"));
        Assert.Equal("permission_required", ex.Code);
        Assert.Equal(403, ex.Status);
        var reread = await repository.GetAsync(TenantId, record.Id);
        Assert.Equal(HostApprovalStatus.Pending, reread!.HostApprovalStatus); // untouched
    }

    [Fact]
    public async Task AC02_b_approving_persists_the_decision_and_audits_it()
    {
        var service = MakeService(out var repository, out var auditSink, out _);
        var record = await service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var approved = await service.ApproveAsync(record.Id, new VersionedVisitorRequest { Version = 1 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        Assert.Equal(HostApprovalStatus.Approved, approved.HostApprovalStatus);
        Assert.Equal(Actor.Id, approved.ApprovalDecidedBy);
        var reread = await repository.GetAsync(TenantId, record.Id);
        Assert.Equal(HostApprovalStatus.Approved, reread!.HostApprovalStatus);
        Assert.Contains(auditSink.All(), e => e.Action == "visitor.approved");
    }

    [Fact]
    public async Task AC02_c_denying_without_a_reason_is_rejected()
    {
        var service = MakeService(out _, out _, out _);
        var record = await service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var ex = await Assert.ThrowsAsync<VisitorError>(() =>
            service.DenyAsync(record.Id, new DenyVisitorRequest { Version = 1, Reason = null }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26"));
        Assert.Equal("validation_failed", ex.Code);
    }

    [Fact]
    public async Task AC02_d_a_denied_visitor_carries_the_reason_and_is_audited()
    {
        var service = MakeService(out _, out var auditSink, out _);
        var record = await service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var denied = await service.DenyAsync(record.Id, new DenyVisitorRequest { Version = 1, Reason = "no meeting on record" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        Assert.Equal(HostApprovalStatus.Denied, denied.HostApprovalStatus);
        Assert.Equal("no meeting on record", denied.ApprovalReason);
        Assert.Contains(auditSink.All(), e => e.Action == "visitor.denied");
    }

    [Fact]
    public async Task AC02_e_an_already_decided_request_cannot_be_redecided()
    {
        var service = MakeService(out _, out _, out _);
        var record = await service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        await service.ApproveAsync(record.Id, new VersionedVisitorRequest { Version = 1 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        var ex = await Assert.ThrowsAsync<VisitorError>(() =>
            service.DenyAsync(record.Id, new DenyVisitorRequest { Version = 2, Reason = "changed my mind" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27"));
        Assert.Equal("approval_already_decided", ex.Code);
    }

    [Fact]
    public async Task AC02_f_concurrent_approve_attempts_on_the_same_pending_record_only_one_wins()
    {
        var service = MakeService(out var repository, out _, out _);
        var record = await service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            try
            {
                await service.ApproveAsync(record.Id, new VersionedVisitorRequest { Version = 1 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");
                return true;
            }
            catch (VisitorError)
            {
                return false;
            }
        }));

        Assert.Single(results, r => r); // exactly one caller's version-1 request wins the CAS
        var reread = await repository.GetAsync(TenantId, record.Id);
        Assert.Equal(2, reread!.Version);
        Assert.Equal(HostApprovalStatus.Approved, reread.HostApprovalStatus);
    }

    [Fact]
    public async Task AC03_a_check_in_enforces_this_tenants_required_fields()
    {
        var service = MakeService(out var repository, out _, out var configRepository);
        var configService = new VisitorConfigService(new FakeCurrentTenantProvider(TenantId), configRepository, new InMemoryVisitorAuditSink(), new VisitorConfigLockRegistry(), new FakePermissionGateway());
        await configService.SetAsync(new SetVisitorConfigRequest { RequireNda = true, DefaultVisitDurationHours = 8, MaxVisitDurationHours = 24 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var record = await service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");
        await service.ApproveAsync(record.Id, new VersionedVisitorRequest { Version = 1 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27");

        var ex = await Assert.ThrowsAsync<VisitorError>(() =>
            service.CheckInAsync(record.Id, new VersionedVisitorRequest { Version = 2 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f28"));
        Assert.Equal("nda_required", ex.Code);

        await service.RecordNdaAcceptanceAsync(record.Id, new VersionedVisitorRequest { Version = 2 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f29");
        var checkedIn = await service.CheckInAsync(record.Id, new VersionedVisitorRequest { Version = 3 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f2a");
        Assert.Equal(VisitStatus.CheckedIn, checkedIn.VisitStatus);
    }

    [Fact]
    public async Task AC03_b_vehicle_nda_safety_briefing_watchlist_and_temporary_credential_are_all_captured_on_the_record()
    {
        var service = MakeService(out _, out _, out var configRepository);
        var configService = new VisitorConfigService(new FakeCurrentTenantProvider(TenantId), configRepository, new InMemoryVisitorAuditSink(), new VisitorConfigLockRegistry(), new FakePermissionGateway());
        await configService.SetAsync(new SetVisitorConfigRequest { AllowTemporaryCredential = true, DefaultVisitDurationHours = 8, MaxVisitDurationHours = 24 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f24");

        var request = ValidRequest() with { VehiclePlate = "KJA-123-XY", TemporaryCredentialRequested = true };
        var record = await service.PreregisterAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        Assert.Equal("KJA-123-XY", record.VehiclePlate);
        Assert.True(record.TemporaryCredentialRequested);
        Assert.False(record.NdaAccepted);
        Assert.False(record.SafetyBriefingCompleted);
        Assert.Equal(WatchlistScreeningResult.NotScreened, record.WatchlistScreeningResult);

        record = await service.RecordNdaAcceptanceAsync(record.Id, new VersionedVisitorRequest { Version = record.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");
        record = await service.RecordSafetyBriefingAsync(record.Id, new VersionedVisitorRequest { Version = record.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27");
        record = await service.RecordWatchlistResultAsync(record.Id, new RecordWatchlistResultRequest { Version = record.Version, Result = "clear" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f28");

        Assert.True(record.NdaAccepted);
        Assert.True(record.SafetyBriefingCompleted);
        Assert.Equal(WatchlistScreeningResult.Clear, record.WatchlistScreeningResult);

        var approved = await service.ApproveAsync(record.Id, new VersionedVisitorRequest { Version = record.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f29");
        var issued = await service.IssueTemporaryCredentialAsync(approved.Id, new VersionedVisitorRequest { Version = approved.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f2a");
        Assert.True(issued.TemporaryCredentialIssued);
    }

    [Fact]
    public async Task AC03_c_a_flagged_watchlist_result_blocks_check_in_unconditionally_even_with_no_config_requirement()
    {
        var service = MakeService(out _, out _, out _);
        var record = await service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        record = await service.ApproveAsync(record.Id, new VersionedVisitorRequest { Version = record.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");
        record = await service.RecordWatchlistResultAsync(record.Id, new RecordWatchlistResultRequest { Version = record.Version, Result = "flagged" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27");

        var ex = await Assert.ThrowsAsync<VisitorError>(() =>
            service.CheckInAsync(record.Id, new VersionedVisitorRequest { Version = record.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f28"));
        Assert.Equal("watchlist_flagged", ex.Code);
    }

    // AC04_a/b construct the expired record directly through the internal domain factory and
    // insert it straight into the repository, bypassing PreregisterAsync's own (correct, and
    // separately worth keeping) "expires_at must be in the future" validation - a visitor cannot
    // be PREREGISTERED already expired, but an in-flight preregistration CAN legitimately age
    // past its expiry before check-in, which is the scenario these two tests exist to prove.
    private static VisitorRecord MakeExpiredRecord() => VisitorRecord.Preregister(
        TenantId, personId: null, "Amara Adeleke", contactEmail: null, contactPhone: null, HostPersonId,
        vehiclePlate: null, temporaryCredentialRequested: false, expiresAt: "2020-01-01T00:00:00.000Z", now: "2019-12-31T00:00:00.000Z");

    [Fact]
    public async Task AC04_a_expiry_is_computed_at_read_time_and_never_mutates_the_stored_record()
    {
        var service = MakeService(out var repository, out _, out _);
        var record = await repository.CreateAsync(MakeExpiredRecord());

        Assert.Equal(VisitStatus.Preregistered, record.VisitStatus); // stored field is untouched
        var effective = VisitorStatusEvaluator.EffectiveStatus(record, "2026-09-24T08:00:00.000Z");
        Assert.Equal(EffectiveVisitStatus.Expired, effective);

        var reread = await repository.GetAsync(TenantId, record.Id);
        Assert.Equal(VisitStatus.Preregistered, reread!.VisitStatus); // still untouched after a read elsewhere
    }

    [Fact]
    public async Task AC04_b_check_in_is_blocked_once_a_preregistration_has_expired()
    {
        var service = MakeService(out var repository, out _, out _);
        var record = await repository.CreateAsync(MakeExpiredRecord());
        var approved = record.Approve(Actor.Id, "2019-12-31T01:00:00.000Z");
        record = await repository.ReplaceAsync(record, approved);

        var ex = await Assert.ThrowsAsync<VisitorError>(() =>
            service.CheckInAsync(record.Id, new VersionedVisitorRequest { Version = record.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27"));
        Assert.Equal("expired", ex.Code);
    }

    [Fact]
    public async Task check_in_then_check_out_is_a_real_two_step_transition_and_check_in_requires_approval_first()
    {
        var service = MakeService(out _, out _, out _);
        var record = await service.PreregisterAsync(ValidRequest(), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var blocked = await Assert.ThrowsAsync<VisitorError>(() =>
            service.CheckInAsync(record.Id, new VersionedVisitorRequest { Version = 1 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26"));
        Assert.Equal("not_approved", blocked.Code);

        record = await service.ApproveAsync(record.Id, new VersionedVisitorRequest { Version = 1 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f27");
        record = await service.CheckInAsync(record.Id, new VersionedVisitorRequest { Version = record.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f28");
        Assert.Equal(VisitStatus.CheckedIn, record.VisitStatus);

        record = await service.CheckOutAsync(record.Id, new VersionedVisitorRequest { Version = record.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f29");
        Assert.Equal(VisitStatus.CheckedOut, record.VisitStatus);

        var reCheckIn = await Assert.ThrowsAsync<VisitorError>(() =>
            service.CheckInAsync(record.Id, new VersionedVisitorRequest { Version = record.Version }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f2a"));
        Assert.Equal("already_checked_in_or_out", reCheckIn.Code);
    }
}
