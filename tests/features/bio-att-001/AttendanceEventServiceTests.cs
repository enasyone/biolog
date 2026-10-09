using Biolog.Features.Attendance.Api;
using Biolog.Features.Attendance.Audit;
using Biolog.Features.Attendance.Domain;
using Xunit;

namespace Biolog.Features.Attendance.Tests;

/// <summary>AC01 (event shape) and AC03 (duplicate/idempotent capture) against <see cref="AttendanceEventService"/>.</summary>
public class AttendanceEventServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string DeviceId = "dev_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03";
    private static readonly AuditActor Actor = new(ActorType.Device, DeviceId);

    private static AttendanceEventService MakeService(out InMemoryAttendanceEventRepository repository, out InMemoryAttendanceAuditSink auditSink)
    {
        repository = new InMemoryAttendanceEventRepository();
        auditSink = new InMemoryAttendanceAuditSink();
        return new AttendanceEventService(
            new FakeCurrentTenantProvider(TenantId), repository, auditSink, new AttendanceEventLockRegistry(), new FakePersonExistenceGateway());
    }

    private static CaptureAttendanceEventRequest ValidRequest(string idempotencyKey, string occurredAt = "2026-09-24T08:00:00.000Z") => new()
    {
        PersonId = PersonId,
        Source = "device",
        DeviceId = DeviceId,
        Location = "Main Gate",
        Mode = "check_in",
        Evidence = new VerificationEvidenceRequest { Method = "biometric", Verified = true, Confidence = 0.97 },
        OccurredAt = occurredAt,
        IdempotencyKey = idempotencyKey,
    };

    [Fact]
    public async Task AC01_a_a_captured_event_carries_person_source_device_location_time_mode_and_evidence()
    {
        var service = MakeService(out _, out _);
        var captured = await service.CaptureAsync(ValidRequest("idem-1"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        Assert.Equal(PersonId, captured.PersonId);
        Assert.Equal(AttendanceSource.Device, captured.Source);
        Assert.Equal(DeviceId, captured.DeviceId);
        Assert.Equal("Main Gate", captured.Location);
        Assert.Equal("2026-09-24T08:00:00.000Z", captured.OccurredAt);
        Assert.Equal(AttendanceMode.CheckIn, captured.Mode);
        Assert.Equal(VerificationMethod.Biometric, captured.Evidence.Method);
        Assert.True(captured.Evidence.Verified);
        Assert.Equal(0.97, captured.Evidence.Confidence);
    }

    [Fact]
    public async Task AC01_b_device_source_requires_a_device_id()
    {
        var service = MakeService(out _, out _);
        var request = ValidRequest("idem-2") with { DeviceId = null };

        var ex = await Assert.ThrowsAsync<AttendanceError>(() => service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("validation_failed", ex.Code);
    }

    [Fact]
    public async Task AC01_c_a_manual_source_forbids_a_device_id()
    {
        var service = MakeService(out _, out _);
        var request = ValidRequest("idem-3") with { Source = "manual" };

        var ex = await Assert.ThrowsAsync<AttendanceError>(() => service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("validation_failed", ex.Code);
    }

    [Fact]
    public async Task AC01_d_a_confidence_score_outside_zero_to_one_is_rejected()
    {
        var service = MakeService(out _, out _);
        var request = ValidRequest("idem-4") with { Evidence = new VerificationEvidenceRequest { Method = "biometric", Verified = true, Confidence = 1.5 } };

        var ex = await Assert.ThrowsAsync<AttendanceError>(() => service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("validation_failed", ex.Code);
    }

    [Fact]
    public async Task AC01_e_a_captured_event_referencing_a_person_who_does_not_exist_is_rejected()
    {
        var repository = new InMemoryAttendanceEventRepository();
        var auditSink = new InMemoryAttendanceAuditSink();
        var service = new AttendanceEventService(
            new FakeCurrentTenantProvider(TenantId), repository, auditSink, new AttendanceEventLockRegistry(),
            new FakePersonExistenceGateway(new HashSet<string>())); // nobody exists

        var ex = await Assert.ThrowsAsync<AttendanceError>(() => service.CaptureAsync(ValidRequest("idem-5"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("person_not_found", ex.Code);
    }

    [Fact]
    public async Task AC03_a_identical_redelivery_of_the_same_idempotency_key_returns_the_same_event_and_does_not_duplicate()
    {
        var service = MakeService(out var repository, out var auditSink);
        var request = ValidRequest("idem-replay");

        var first = await service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        var second = await service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        Assert.Equal(first.EventId, second.EventId);
        var stored = await repository.ListForTenantAsync(TenantId);
        Assert.Single(stored);
        Assert.Single(auditSink.All()); // no second audit entry for a pure replay
    }

    [Fact]
    public async Task AC03_b_reusing_an_idempotency_key_with_different_content_is_a_conflict_not_a_silent_overwrite()
    {
        var service = MakeService(out var repository, out _);
        var first = ValidRequest("idem-conflict");
        var second = ValidRequest("idem-conflict") with { Location = "Side Door" }; // same key, different content

        await service.CaptureAsync(first, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        var ex = await Assert.ThrowsAsync<AttendanceError>(() => service.CaptureAsync(second, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26"));

        Assert.Equal("idempotency_conflict", ex.Code);
        Assert.Equal(409, ex.Status);
        var stored = await repository.ListForTenantAsync(TenantId);
        Assert.Single(stored);
        Assert.Equal("Main Gate", stored[0].Location); // the original event was never overwritten
    }

    [Fact]
    public async Task AC03_c_concurrent_captures_with_the_same_idempotency_key_serialize_and_only_one_event_is_ever_created()
    {
        var service = MakeService(out var repository, out _);
        var request = ValidRequest("idem-race");

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            service.CaptureAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25")));

        var stored = await repository.ListForTenantAsync(TenantId);
        Assert.Single(stored);
        Assert.True(results.All(r => r.EventId == results[0].EventId));
    }

    [Fact]
    public async Task AC02_a_two_different_captures_for_the_same_person_produce_two_independent_raw_events_neither_carrying_a_status_field()
    {
        var service = MakeService(out var repository, out _);
        await service.CaptureAsync(ValidRequest("idem-a", "2026-09-24T08:00:00.000Z"), Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        await service.CaptureAsync(ValidRequest("idem-b", "2026-09-24T17:00:00.000Z") with { Mode = "check_out" }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        var stored = await repository.ListForTenantAsync(TenantId);
        Assert.Equal(2, stored.Count);
        // AttendanceEvent has no status/derived-classification property at all - compile-time proof by construction, not a runtime assertion.
    }
}
