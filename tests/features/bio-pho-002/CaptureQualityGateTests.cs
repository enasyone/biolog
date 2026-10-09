using Biolog.Features.PhotoBooth.Domain;
using Xunit;

namespace Biolog.Features.PhotoBooth.Tests;

public sealed class CaptureQualityGateTests
{
    private static readonly CaptureSpecification Spec = CaptureSpecification.Current;

    [Fact]
    public void AC01_a_capture_meeting_every_rule_passes()
    {
        var inspected = new CaptureRasterInspector.Inspected(CaptureFormat.Png, Spec.FrameWidthPx, Spec.FrameHeightPx, Spec.MinimumDpi);

        var verdict = CaptureQualityGate.Evaluate(inspected, Spec, faceHeightCompliant: true, backgroundCompliant: true);

        Assert.True(verdict.Passed);
        Assert.Empty(verdict.FailureReasons);
    }

    [Fact]
    public void AC04_a_capture_smaller_than_the_required_frame_size_fails()
    {
        var inspected = new CaptureRasterInspector.Inspected(CaptureFormat.Png, Spec.FrameWidthPx / 2, Spec.FrameHeightPx / 2, null);

        var verdict = CaptureQualityGate.Evaluate(inspected, Spec, faceHeightCompliant: true, backgroundCompliant: true);

        Assert.False(verdict.Passed);
        Assert.Contains(verdict.FailureReasons, r => r.Contains("below the required minimum"));
    }

    [Fact]
    public void AC04_a_capture_with_the_wrong_aspect_ratio_fails()
    {
        // Same total resolution, but square instead of the required 7:9 portrait ratio.
        var inspected = new CaptureRasterInspector.Inspected(CaptureFormat.Png, 1200, 1200, null);

        var verdict = CaptureQualityGate.Evaluate(inspected, Spec, faceHeightCompliant: true, backgroundCompliant: true);

        Assert.False(verdict.Passed);
        Assert.Contains(verdict.FailureReasons, r => r.Contains("aspect ratio"));
    }

    [Fact]
    public void AC04_a_capture_with_embedded_dpi_below_the_minimum_fails()
    {
        var inspected = new CaptureRasterInspector.Inspected(CaptureFormat.Png, Spec.FrameWidthPx, Spec.FrameHeightPx, Spec.MinimumDpi - 1);

        var verdict = CaptureQualityGate.Evaluate(inspected, Spec, faceHeightCompliant: true, backgroundCompliant: true);

        Assert.False(verdict.Passed);
        Assert.Contains(verdict.FailureReasons, r => r.Contains("embedded DPI"));
    }

    [Fact]
    public void AC01_a_capture_with_no_embedded_dpi_at_all_still_passes_when_the_pixel_dimensions_alone_meet_the_minimum()
    {
        // Many real cameras never embed density metadata - the pixel-dimension check alone is
        // the real minimum-DPI enforcement; a missing NativeDpi is not itself a failure.
        var inspected = new CaptureRasterInspector.Inspected(CaptureFormat.Jpeg, Spec.FrameWidthPx, Spec.FrameHeightPx, null);

        var verdict = CaptureQualityGate.Evaluate(inspected, Spec, faceHeightCompliant: true, backgroundCompliant: true);

        Assert.True(verdict.Passed);
    }

    [Fact]
    public void AC04_a_capture_the_booth_did_not_attest_face_height_compliance_for_fails()
    {
        var inspected = new CaptureRasterInspector.Inspected(CaptureFormat.Png, Spec.FrameWidthPx, Spec.FrameHeightPx, Spec.MinimumDpi);

        var verdict = CaptureQualityGate.Evaluate(inspected, Spec, faceHeightCompliant: false, backgroundCompliant: true);

        Assert.False(verdict.Passed);
        Assert.Contains(verdict.FailureReasons, r => r.Contains("face-height"));
    }

    [Fact]
    public void AC04_a_capture_the_booth_did_not_attest_background_compliance_for_fails()
    {
        var inspected = new CaptureRasterInspector.Inspected(CaptureFormat.Png, Spec.FrameWidthPx, Spec.FrameHeightPx, Spec.MinimumDpi);

        var verdict = CaptureQualityGate.Evaluate(inspected, Spec, faceHeightCompliant: true, backgroundCompliant: false);

        Assert.False(verdict.Passed);
        Assert.Contains(verdict.FailureReasons, r => r.Contains("background"));
    }

    [Fact]
    public void AC04_multiple_simultaneous_failures_are_all_reported_together()
    {
        var inspected = new CaptureRasterInspector.Inspected(CaptureFormat.Png, 10, 10, 50);

        var verdict = CaptureQualityGate.Evaluate(inspected, Spec, faceHeightCompliant: false, backgroundCompliant: false);

        Assert.False(verdict.Passed);
        Assert.True(verdict.FailureReasons.Count >= 3);
    }
}
