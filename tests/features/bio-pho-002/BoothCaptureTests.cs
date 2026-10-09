using Biolog.Features.PhotoBooth.Domain;
using Xunit;

namespace Biolog.Features.PhotoBooth.Tests;

/// <summary>Applying BIO-PHO-001-QA-01's lesson from the start - see <see cref="BoothCapture"/>'s own header.</summary>
public sealed class BoothCaptureTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";

    private static BoothCapture NewPending(byte[] content) => new(
        PersonId, TenantId, DeviceId, sequenceNumber: 1, CaptureFormat.Png, 1050, 1350, nativeDpi: 300,
        faceHeightCompliant: true, backgroundCompliant: true, content, CaptureStatus.Pending,
        rejectionReasons: [], promotedMasterVersion: null, version: 1, submittedAt: "2026-09-22T00:00:00.000Z", decidedAt: null);

    [Fact]
    public void QA01_mutating_the_array_the_caller_originally_passed_in_never_changes_the_stored_content()
    {
        var content = SyntheticImages.Png(10, 10);
        var originalBytes = (byte[])content.Clone();
        var capture = NewPending(content);

        content[0] = 0x00;

        Assert.Equal(originalBytes, capture.Content);
    }

    [Fact]
    public void QA01_mutating_a_previously_read_copy_never_changes_what_a_later_read_returns()
    {
        var content = SyntheticImages.Png(10, 10);
        var originalBytes = (byte[])content.Clone();
        var capture = NewPending(content);

        var firstRead = capture.Content;
        firstRead[0] = 0xFF;

        Assert.Equal(originalBytes, capture.Content);
        Assert.NotSame(firstRead, capture.Content);
    }

    [Fact]
    public void A_pending_capture_may_not_carry_rejection_reasons()
    {
        Assert.Throws<ArgumentException>(() => new BoothCapture(
            PersonId, TenantId, DeviceId, 1, CaptureFormat.Png, 10, 10, null, true, true, SyntheticImages.Png(10, 10),
            CaptureStatus.Pending, rejectionReasons: ["should not be here"], promotedMasterVersion: null, version: 1,
            submittedAt: "2026-09-22T00:00:00.000Z", decidedAt: null));
    }

    [Fact]
    public void A_rejected_capture_must_carry_at_least_one_rejection_reason()
    {
        Assert.Throws<ArgumentException>(() => new BoothCapture(
            PersonId, TenantId, DeviceId, 1, CaptureFormat.Png, 10, 10, null, true, true, SyntheticImages.Png(10, 10),
            CaptureStatus.Rejected, rejectionReasons: [], promotedMasterVersion: null, version: 1,
            submittedAt: "2026-09-22T00:00:00.000Z", decidedAt: "2026-09-22T00:00:01.000Z"));
    }

    [Fact]
    public void A_promoted_capture_must_carry_the_promoted_master_version()
    {
        Assert.Throws<ArgumentException>(() => new BoothCapture(
            PersonId, TenantId, DeviceId, 1, CaptureFormat.Png, 10, 10, null, true, true, SyntheticImages.Png(10, 10),
            CaptureStatus.Promoted, rejectionReasons: [], promotedMasterVersion: null, version: 1,
            submittedAt: "2026-09-22T00:00:00.000Z", decidedAt: "2026-09-22T00:00:01.000Z"));
    }

    [Fact]
    public void Only_a_promoted_capture_may_carry_a_promoted_master_version()
    {
        Assert.Throws<ArgumentException>(() => new BoothCapture(
            PersonId, TenantId, DeviceId, 1, CaptureFormat.Png, 10, 10, null, true, true, SyntheticImages.Png(10, 10),
            CaptureStatus.Pending, rejectionReasons: [], promotedMasterVersion: 1, version: 1,
            submittedAt: "2026-09-22T00:00:00.000Z", decidedAt: null));
    }
}
