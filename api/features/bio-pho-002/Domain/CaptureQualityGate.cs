namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// BIO-PHO-002-AC04's real enforcement point: "unapproved/low-quality capture is not silently
/// promoted to master." <see cref="Evaluate"/> is a pure function - no I/O, no persistence -
/// run at submission time (<see cref="CaptureService.SubmitAsync"/>), BEFORE a
/// <see cref="BoothCapture"/> record is ever created. A submission that fails is recorded
/// already <see cref="CaptureStatus.Rejected"/>, never <see cref="CaptureStatus.Pending"/> -
/// there is no "silent" path, because rejection happens deterministically at the one place
/// every submission passes through, not by a human remembering to check.
///
/// <b>What is measured vs. what is attested.</b> Frame size, aspect ratio and DPI are measured
/// directly from the file's own bytes (<see cref="CaptureRasterInspector"/>) - a booth cannot
/// simply claim compliance for these. Face-height and background compliance cannot be measured
/// this way (no computer-vision library exists anywhere in this host - the same disclosed
/// boundary <see cref="CaptureRasterInspector"/>'s header names), so they are booth-supplied
/// attestations; a `false` (or omitted-as-false) attestation is treated as a quality failure
/// with exactly the same weight as a measured one - this gate does not distinguish "the booth
/// says it failed" from "we measured that it failed" in its outcome, only in the reason text,
/// so a booth cannot game promotion by omitting an attestation rather than answering `false`.
/// </summary>
public static class CaptureQualityGate
{
    private const double AspectRatioTolerance = 0.02; // 2%, to absorb rounding in a booth's own frame cropping

    public sealed record Verdict(bool Passed, IReadOnlyList<string> FailureReasons);

    public static Verdict Evaluate(
        CaptureRasterInspector.Inspected inspected, CaptureSpecification specification, bool faceHeightCompliant, bool backgroundCompliant)
    {
        var reasons = new List<string>();

        if (inspected.WidthPx < specification.FrameWidthPx || inspected.HeightPx < specification.FrameHeightPx)
        {
            reasons.Add(
                $"capture is {inspected.WidthPx}x{inspected.HeightPx}px, below the required minimum " +
                $"{specification.FrameWidthPx}x{specification.FrameHeightPx}px");
        }

        var requiredRatio = (double)specification.AspectRatioWidth / specification.AspectRatioHeight;
        var capturedRatio = (double)inspected.WidthPx / inspected.HeightPx;
        if (Math.Abs(capturedRatio - requiredRatio) / requiredRatio > AspectRatioTolerance)
        {
            reasons.Add(
                $"aspect ratio {inspected.WidthPx}:{inspected.HeightPx} does not match the required " +
                $"{specification.AspectRatioWidth}:{specification.AspectRatioHeight} within tolerance");
        }

        // Rounded to the nearest whole DPI before comparing: a PNG's own pHYs chunk stores
        // pixels-per-metre, not DPI directly, so converting a whole-number target DPI (e.g.
        // 300) through that unit and back can legitimately land a fraction of a unit below it
        // (11811 px/m round-trips to 299.9994 dpi, not 300) - a real device's own encoder hits
        // the identical rounding, so an un-rounded strict comparison would wrongly reject a
        // capture that is genuinely at the target resolution.
        if (inspected.NativeDpi is { } nativeDpi && Math.Round(nativeDpi) < specification.MinimumDpi)
        {
            reasons.Add($"embedded DPI {nativeDpi:F0} is below the required minimum {specification.MinimumDpi}");
        }

        if (!faceHeightCompliant)
        {
            reasons.Add("booth did not attest face-height compliance");
        }

        if (!backgroundCompliant)
        {
            reasons.Add("booth did not attest background compliance");
        }

        return new Verdict(reasons.Count == 0, reasons);
    }
}
