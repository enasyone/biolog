using Biolog.Features.Attendance.Api;
using Biolog.Features.Attendance.Audit;
using Biolog.Features.Attendance.Domain;
using Xunit;

namespace Biolog.Features.Attendance.Tests;

/// <summary>AC02/AC04 read-time composition (events + policy -> derived status), through the actual service, not the pure evaluator directly.</summary>
public class AttendanceStatusServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string DeviceId = "dev_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03";
    private static readonly AuditActor Actor = new(ActorType.Device, DeviceId);

    [Fact]
    public async Task setting_a_stricter_policy_after_capture_changes_the_derived_status_without_touching_the_raw_event()
    {
        var eventRepository = new InMemoryAttendanceEventRepository();
        var policyRepository = new InMemoryAttendancePolicyRepository();
        var auditSink = new InMemoryAttendanceAuditSink();
        var tenantProvider = new FakeCurrentTenantProvider(TenantId);

        var eventService = new AttendanceEventService(tenantProvider, eventRepository, auditSink, new AttendanceEventLockRegistry(), new FakePersonExistenceGateway());
        var policyService = new AttendancePolicyService(tenantProvider, policyRepository, auditSink, new AttendancePolicyLockRegistry(), new FakePermissionGateway());
        var statusService = new AttendanceStatusService(tenantProvider, eventRepository, policyRepository);

        var captureRequest = new CaptureAttendanceEventRequest
        {
            PersonId = PersonId,
            Source = "device",
            DeviceId = DeviceId,
            Mode = "check_in",
            Evidence = new VerificationEvidenceRequest { Method = "biometric", Verified = true },
            OccurredAt = "2026-09-24T08:20:00.000Z",
            IdempotencyKey = "status-service-test",
        };
        var captured = await eventService.CaptureAsync(captureRequest, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        var beforePolicy = await statusService.GetDailyStatusAsync(PersonId, "2026-09-24");
        Assert.Equal(AttendanceStatusValue.Present, beforePolicy.Status); // default policy - no expected start time

        await policyService.SetAsync(
            new SetAttendancePolicyRequest { InterpretationMode = "education", ExpectedStartTime = "08:00", LateGraceMinutes = 5 },
            new AuditActor(ActorType.User, "usr-admin-1"), "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        var afterPolicy = await statusService.GetDailyStatusAsync(PersonId, "2026-09-24");
        Assert.Equal(AttendanceStatusValue.Late, afterPolicy.Status); // 08:20 arrival, 08:05 cutoff

        // The raw event itself was never touched by either status computation.
        var stillRaw = await eventRepository.ListForPersonOnDateAsync(TenantId, PersonId, "2026-09-24");
        Assert.Single(stillRaw);
        Assert.Equal(captured.EventId, stillRaw[0].EventId);
        Assert.Equal(captured.OccurredAt, stillRaw[0].OccurredAt);
    }
}
