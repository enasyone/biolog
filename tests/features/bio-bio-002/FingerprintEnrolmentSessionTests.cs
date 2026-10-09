using Biolog.Features.FingerprintEnrolment.Domain;
using Xunit;

namespace Biolog.Features.FingerprintEnrolment.Tests;

/// <summary>Domain-level tests for <see cref="FingerprintEnrolmentSession"/>: AC01 (modes),
/// AC03 (recapture/exception invariants), and concurrency (CAS).</summary>
public sealed class FingerprintEnrolmentSessionTests
{
    [Fact]
    public void AC01_TenPrint_mode_targets_exactly_the_ten_standard_positions()
    {
        var session = FingerprintEnrolmentSession.Start("s1", "ten_a", "per_a", EnrolmentMode.TenPrint, [], retentionAuthorised: false, "t0");

        Assert.Equal(10, session.TargetPositions.Length);
        Assert.Equal(FingerPositionExtensions.AllTenPrintPositions, session.TargetPositions);
        Assert.All(session.Fingers.Values, f => Assert.Equal(FingerCaptureStatus.Pending, f.Status));
    }

    [Fact]
    public void AC01_SingleFinger_mode_targets_exactly_the_one_requested_position()
    {
        var session = FingerprintEnrolmentSession.Start(
            "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "t0");

        Assert.Single(session.TargetPositions);
        Assert.Equal(FingerPosition.RightIndex, session.TargetPositions[0]);
    }

    [Fact]
    public void AC01_SingleFinger_mode_requires_exactly_one_requested_position()
    {
        Assert.Throws<FingerprintEnrolmentException>(() =>
            FingerprintEnrolmentSession.Start("s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [], retentionAuthorised: false, "t0"));

        Assert.Throws<FingerprintEnrolmentException>(() =>
            FingerprintEnrolmentSession.Start(
                "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger,
                [FingerPosition.RightIndex, FingerPosition.LeftIndex], retentionAuthorised: false, "t0"));
    }

    [Fact]
    public void CaptureFinger_rejects_a_position_outside_the_sessions_target_positions()
    {
        var session = FingerprintEnrolmentSession.Start(
            "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "t0");

        Assert.Throws<FingerprintEnrolmentException>(() =>
            session.CaptureFinger(FingerPosition.LeftThumb, FingerRecord.CreatePending(FingerPosition.LeftThumb), session.Version, "t1"));
    }

    [Fact]
    public void CaptureFinger_bumps_version_and_records_the_finger_as_captured()
    {
        var session = FingerprintEnrolmentSession.Start(
            "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "t0");

        var updated = session.CaptureFinger(
            FingerPosition.RightIndex, FingerRecord.CreatePending(FingerPosition.RightIndex), session.Version, "t1");

        Assert.Equal(session.Version + 1, updated.Version);
        Assert.Equal(FingerCaptureStatus.Captured, updated.Fingers[FingerPosition.RightIndex].Status);
        Assert.Equal(1, updated.Fingers[FingerPosition.RightIndex].CaptureCount);
    }

    // AC03: recapture is simply capturing the same position again.
    [Fact]
    public void AC03_recapturing_an_already_captured_position_increments_capture_count_and_supersedes_it()
    {
        var session = FingerprintEnrolmentSession.Start(
            "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "t0");
        var afterFirst = session.CaptureFinger(FingerPosition.RightIndex, FingerRecord.CreatePending(FingerPosition.RightIndex), session.Version, "t1");

        var afterRecapture = afterFirst.CaptureFinger(
            FingerPosition.RightIndex, FingerRecord.CreatePending(FingerPosition.RightIndex), afterFirst.Version, "t2");

        Assert.Equal(2, afterRecapture.Fingers[FingerPosition.RightIndex].CaptureCount);
    }

    // AC03: missing/amputated exception path.
    [Fact]
    public void AC03_marking_a_finger_missing_amputated_records_the_reason_and_is_terminal()
    {
        var session = FingerprintEnrolmentSession.Start(
            "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "t0");

        var updated = session.MarkMissingOrAmputated(FingerPosition.RightIndex, "surgical amputation", session.Version, "t1");

        Assert.Equal(FingerCaptureStatus.MissingAmputated, updated.Fingers[FingerPosition.RightIndex].Status);
        Assert.Equal("surgical amputation", updated.Fingers[FingerPosition.RightIndex].MissingAmputatedReason);

        Assert.Throws<FingerprintEnrolmentException>(() =>
            updated.CaptureFinger(FingerPosition.RightIndex, FingerRecord.CreatePending(FingerPosition.RightIndex), updated.Version, "t2"));
    }

    [Fact]
    public void AC03_marking_the_same_finger_missing_amputated_twice_is_idempotent()
    {
        var session = FingerprintEnrolmentSession.Start(
            "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "t0");
        var once = session.MarkMissingOrAmputated(FingerPosition.RightIndex, "reason", session.Version, "t1");

        var twice = once.MarkMissingOrAmputated(FingerPosition.RightIndex, "reason", once.Version, "t2");

        Assert.Equal(once.Version, twice.Version);
    }

    [Fact]
    public void Complete_requires_every_target_position_resolved()
    {
        var session = FingerprintEnrolmentSession.Start(
            "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "t0");

        Assert.Throws<FingerprintEnrolmentException>(() => session.Complete(session.Version, "t1"));
    }

    [Fact]
    public void Complete_succeeds_once_every_target_position_is_captured_or_missing_amputated()
    {
        var session = FingerprintEnrolmentSession.Start(
            "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "t0");
        var resolved = session.MarkMissingOrAmputated(FingerPosition.RightIndex, "reason", session.Version, "t1");

        var completed = resolved.Complete(resolved.Version, "t2");

        Assert.Equal(EnrolmentSessionStatus.Completed, completed.Status);
    }

    [Fact]
    public void A_tenprint_session_can_complete_with_a_mix_of_captured_and_missing_amputated_fingers()
    {
        var session = FingerprintEnrolmentSession.Start("s1", "ten_a", "per_a", EnrolmentMode.TenPrint, [], retentionAuthorised: false, "t0");
        var current = session;
        foreach (var position in FingerPositionExtensions.AllTenPrintPositions)
        {
            current = position == FingerPosition.LeftLittle
                ? current.MarkMissingOrAmputated(position, "amputated", current.Version, "t1")
                : current.CaptureFinger(position, FingerRecord.CreatePending(position), current.Version, "t1");
        }

        var completed = current.Complete(current.Version, "t2");

        Assert.Equal(EnrolmentSessionStatus.Completed, completed.Status);
    }

    // Concurrency (ADR-0001 Decision 3): CAS on every mutation.
    [Fact]
    public void A_stale_expected_version_is_rejected_with_a_concurrency_conflict()
    {
        var session = FingerprintEnrolmentSession.Start(
            "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "t0");

        var ex = Assert.Throws<FingerprintEnrolmentException>(() =>
            session.CaptureFinger(FingerPosition.RightIndex, FingerRecord.CreatePending(FingerPosition.RightIndex), session.Version + 1, "t1"));
        Assert.Equal("concurrency_conflict", ex.Code);
    }

    [Fact]
    public void Mutating_a_completed_session_is_rejected()
    {
        var session = FingerprintEnrolmentSession.Start(
            "s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "t0");
        var completed = session.MarkMissingOrAmputated(FingerPosition.RightIndex, "reason", session.Version, "t1").Complete(2, "t2");

        Assert.Throws<FingerprintEnrolmentException>(() => completed.Cancel(completed.Version, "t3"));
    }
}
