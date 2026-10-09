using Biolog.Features.Attendance.Domain;
using Biolog.Features.Attendance.Identifiers;
using Xunit;

namespace Biolog.Features.Attendance.Tests;

/// <summary>AC02 (raw is distinct from derived) and AC04 (tenant policy defines interpretation) - all against the pure <see cref="AttendanceStatusEvaluator"/>, no DI.</summary>
public class AttendanceStatusEvaluatorTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    private static AttendanceEvent MakeEvent(AttendanceMode mode, string occurredAt) =>
        AttendanceEvent.Capture(
            TenantId, PersonId, AttendanceSource.Device, "dev_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03", "Main Gate",
            mode, new VerificationEvidence { Method = VerificationMethod.Credential, Verified = true, Confidence = 0.9 },
            occurredAt, occurredAt, Guid.NewGuid().ToString());

    [Fact]
    public void AC02_a_no_events_for_the_day_produce_absent()
    {
        var policy = AttendancePolicy.Default(TenantId);
        var status = AttendanceStatusEvaluator.Evaluate(TenantId, PersonId, "2026-09-24", [], policy, UtcInstant.Now());

        Assert.Equal(AttendanceStatusValue.Absent, status.Status);
        Assert.Empty(status.BasedOnEventIds);
    }

    [Fact]
    public void AC02_b_a_checkout_only_day_with_no_checkin_or_presence_is_still_absent()
    {
        var policy = AttendancePolicy.Default(TenantId);
        var events = new List<AttendanceEvent> { MakeEvent(AttendanceMode.CheckOut, "2026-09-24T17:00:00.000Z") };
        var status = AttendanceStatusEvaluator.Evaluate(TenantId, PersonId, "2026-09-24", events, policy, UtcInstant.Now());

        Assert.Equal(AttendanceStatusValue.Absent, status.Status);
    }

    [Fact]
    public void AC04_a_default_policy_has_no_expected_start_time_so_any_arrival_is_present()
    {
        var policy = AttendancePolicy.Default(TenantId);
        var events = new List<AttendanceEvent> { MakeEvent(AttendanceMode.CheckIn, "2026-09-24T23:59:00.000Z") };
        var status = AttendanceStatusEvaluator.Evaluate(TenantId, PersonId, "2026-09-24", events, policy, UtcInstant.Now());

        Assert.Equal(AttendanceStatusValue.Present, status.Status);
        Assert.Single(status.BasedOnEventIds, events[0].EventId);
    }

    [Fact]
    public void AC04_b_an_arrival_within_the_grace_window_is_present()
    {
        var policy = AttendancePolicy.Create(TenantId, AttendanceInterpretationMode.Education, "08:00", lateGraceMinutes: 10, "actor-1", UtcInstant.Now());
        var events = new List<AttendanceEvent> { MakeEvent(AttendanceMode.CheckIn, "2026-09-24T08:09:00.000Z") };
        var status = AttendanceStatusEvaluator.Evaluate(TenantId, PersonId, "2026-09-24", events, policy, UtcInstant.Now());

        Assert.Equal(AttendanceStatusValue.Present, status.Status);
    }

    [Fact]
    public void AC04_c_an_arrival_after_the_grace_window_is_late()
    {
        var policy = AttendancePolicy.Create(TenantId, AttendanceInterpretationMode.Education, "08:00", lateGraceMinutes: 10, "actor-1", UtcInstant.Now());
        var events = new List<AttendanceEvent> { MakeEvent(AttendanceMode.CheckIn, "2026-09-24T08:11:00.000Z") };
        var status = AttendanceStatusEvaluator.Evaluate(TenantId, PersonId, "2026-09-24", events, policy, UtcInstant.Now());

        Assert.Equal(AttendanceStatusValue.Late, status.Status);
    }

    [Fact]
    public void AC02_and_AC04_together_the_identical_raw_event_set_classifies_differently_under_two_different_policies()
    {
        var events = new List<AttendanceEvent> { MakeEvent(AttendanceMode.CheckIn, "2026-09-24T08:30:00.000Z") };

        var lenientPolicy = AttendancePolicy.Create(TenantId, AttendanceInterpretationMode.Workforce, "08:00", lateGraceMinutes: 60, "actor-1", UtcInstant.Now());
        var strictPolicy = AttendancePolicy.Create(TenantId, AttendanceInterpretationMode.Workforce, "08:00", lateGraceMinutes: 5, "actor-1", UtcInstant.Now());

        var underLenient = AttendanceStatusEvaluator.Evaluate(TenantId, PersonId, "2026-09-24", events, lenientPolicy, UtcInstant.Now());
        var underStrict = AttendanceStatusEvaluator.Evaluate(TenantId, PersonId, "2026-09-24", events, strictPolicy, UtcInstant.Now());

        Assert.Equal(AttendanceStatusValue.Present, underLenient.Status);
        Assert.Equal(AttendanceStatusValue.Late, underStrict.Status);
        // The raw fact itself never changed between the two evaluations - same event, same EventId, same content.
        Assert.Equal(events[0].EventId, underLenient.BasedOnEventIds[0]);
        Assert.Equal(events[0].EventId, underStrict.BasedOnEventIds[0]);
    }

    [Fact]
    public void AC02_d_the_earliest_arrival_of_the_day_is_the_one_evaluated_when_multiple_exist()
    {
        var policy = AttendancePolicy.Create(TenantId, AttendanceInterpretationMode.Education, "08:00", lateGraceMinutes: 5, "actor-1", UtcInstant.Now());
        var early = MakeEvent(AttendanceMode.CheckIn, "2026-09-24T07:55:00.000Z");
        var later = MakeEvent(AttendanceMode.Presence, "2026-09-24T09:00:00.000Z");
        var events = new List<AttendanceEvent> { later, early }; // deliberately out of order

        var status = AttendanceStatusEvaluator.Evaluate(TenantId, PersonId, "2026-09-24", events, policy, UtcInstant.Now());

        Assert.Equal(AttendanceStatusValue.Present, status.Status);
        Assert.Equal(early.EventId, status.BasedOnEventIds[0]);
    }
}
