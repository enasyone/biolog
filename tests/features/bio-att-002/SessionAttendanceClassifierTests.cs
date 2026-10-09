using Biolog.Features.AttendanceSessions.Domain;
using Xunit;

namespace Biolog.Features.AttendanceSessions.Tests;

public sealed class SessionAttendanceClassifierTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    private static Session BuildSession(string scheduledStartTime = "09:00") =>
        Session.Create(TenantId, SessionType.Lecture, "Intro to Biology", "2026-10-01", scheduledStartTime, [PersonId], "usr_teacher_1", "2026-09-30T00:00:00.000Z");

    private static AttendanceEvidenceRecord Arrival(string occurredAt, string mode = "check_in") => new()
    {
        EventId = "evt_0199e0c1-1111-7222-8333-000000000000",
        OccurredAt = occurredAt,
        Source = "device",
        Mode = mode,
        EvidenceMethod = "credential",
        EvidenceVerified = true,
        EvidenceConfidence = 0.95,
    };

    [Fact]
    public void AC02_no_evidence_at_all_is_absent_with_no_grounding_evidence()
    {
        var session = BuildSession();
        var policy = SessionAttendancePolicy.Default(TenantId, SessionType.Lecture);

        var result = SessionAttendanceClassifier.Evaluate(TenantId, session, PersonId, [], policy, "2026-10-01T10:00:00.000Z");

        Assert.Equal(SessionAttendanceStatusValue.Absent, result.Status);
        Assert.Empty(result.GroundingEvidence);
    }

    [Fact]
    public void AC02_an_arrival_within_the_late_grace_window_is_present()
    {
        var session = BuildSession("09:00");
        var policy = SessionAttendancePolicy.Create(TenantId, SessionType.Lecture, lateGraceMinutes: 15, absentAfterMinutes: 30, "usr_admin", "2026-09-30T00:00:00.000Z");

        var result = SessionAttendanceClassifier.Evaluate(TenantId, session, PersonId, [Arrival("2026-10-01T09:10:00.000Z")], policy, "2026-10-01T10:00:00.000Z");

        Assert.Equal(SessionAttendanceStatusValue.Present, result.Status);
        Assert.Single(result.GroundingEvidence);
    }

    [Fact]
    public void AC02_an_arrival_between_the_grace_window_and_the_absent_threshold_is_late()
    {
        var session = BuildSession("09:00");
        var policy = SessionAttendancePolicy.Create(TenantId, SessionType.Lecture, lateGraceMinutes: 15, absentAfterMinutes: 30, "usr_admin", "2026-09-30T00:00:00.000Z");

        var result = SessionAttendanceClassifier.Evaluate(TenantId, session, PersonId, [Arrival("2026-10-01T09:20:00.000Z")], policy, "2026-10-01T10:00:00.000Z");

        Assert.Equal(SessionAttendanceStatusValue.Late, result.Status);
    }

    [Fact]
    public void AC02_an_arrival_after_the_absent_threshold_is_absent_even_though_a_real_event_exists()
    {
        var session = BuildSession("09:00");
        var policy = SessionAttendancePolicy.Create(TenantId, SessionType.Lecture, lateGraceMinutes: 15, absentAfterMinutes: 30, "usr_admin", "2026-09-30T00:00:00.000Z");

        var result = SessionAttendanceClassifier.Evaluate(TenantId, session, PersonId, [Arrival("2026-10-01T09:45:00.000Z")], policy, "2026-10-01T10:00:00.000Z");

        Assert.Equal(SessionAttendanceStatusValue.Absent, result.Status);
    }

    [Fact]
    public void AC02_the_SAME_evidence_classifies_differently_under_two_different_session_type_policies()
    {
        var session = BuildSession("09:00");
        var evidence = new[] { Arrival("2026-10-01T09:20:00.000Z") };

        var strictPolicy = SessionAttendancePolicy.Create(TenantId, SessionType.Exam, lateGraceMinutes: 5, absentAfterMinutes: 10, "usr_admin", "2026-09-30T00:00:00.000Z");
        var lenientPolicy = SessionAttendancePolicy.Create(TenantId, SessionType.Lecture, lateGraceMinutes: 30, absentAfterMinutes: 60, "usr_admin", "2026-09-30T00:00:00.000Z");

        var strictResult = SessionAttendanceClassifier.Evaluate(TenantId, session, PersonId, evidence, strictPolicy, "2026-10-01T10:00:00.000Z");
        var lenientResult = SessionAttendanceClassifier.Evaluate(TenantId, session, PersonId, evidence, lenientPolicy, "2026-10-01T10:00:00.000Z");

        Assert.Equal(SessionAttendanceStatusValue.Absent, strictResult.Status);
        Assert.Equal(SessionAttendanceStatusValue.Present, lenientResult.Status);
    }

    [Fact]
    public void AC03_the_grounding_evidence_carries_the_original_event_id_and_verification_details_unmodified()
    {
        var session = BuildSession("09:00");
        var policy = SessionAttendancePolicy.Default(TenantId, SessionType.Lecture);
        var evidence = new AttendanceEvidenceRecord
        {
            EventId = "evt_0199e0c1-2222-7333-8444-000000000000",
            OccurredAt = "2026-10-01T09:05:00.000Z",
            Source = "device",
            Mode = "check_in",
            EvidenceMethod = "biometric",
            EvidenceVerified = true,
            EvidenceConfidence = 0.88,
        };

        var result = SessionAttendanceClassifier.Evaluate(TenantId, session, PersonId, [evidence], policy, "2026-10-01T10:00:00.000Z");

        var grounding = Assert.Single(result.GroundingEvidence);
        Assert.Equal(evidence.EventId, grounding.EventId);
        Assert.Equal(evidence.EvidenceMethod, grounding.EvidenceMethod);
        Assert.Equal(evidence.EvidenceConfidence, grounding.EvidenceConfidence);
    }

    [Fact]
    public void AC02_a_check_out_only_event_never_counts_as_an_arrival()
    {
        var session = BuildSession("09:00");
        var policy = SessionAttendancePolicy.Default(TenantId, SessionType.Lecture);

        var result = SessionAttendanceClassifier.Evaluate(TenantId, session, PersonId, [Arrival("2026-10-01T09:05:00.000Z", mode: "check_out")], policy, "2026-10-01T10:00:00.000Z");

        Assert.Equal(SessionAttendanceStatusValue.Absent, result.Status);
    }
}
