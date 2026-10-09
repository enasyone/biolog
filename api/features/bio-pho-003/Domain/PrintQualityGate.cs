namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// BIO-PHO-003-AC01's print-quality assessment, deliberately independent of
/// <see cref="BiometricSuitabilityResult"/> - either can pass while the other fails (AC01's
/// own literal requirement). A pure function - no I/O, no persistence - run at submission time
/// (<see cref="PhotoQualityService.SubmitAsync"/>). Unlike BIO-PHO-002's own
/// <c>CaptureQualityGate</c> (whose failing verdict auto-rejects a booth submission before a
/// record even exists), a failing verdict here does NOT auto-reject anything - BIO-PHO-003's
/// own enrolment-staff workflow gives a human operator the final decision (AC03), informed by
/// both this verdict and the biometric one, not an automatic gate. See
/// <see cref="PhotoQualityService"/>'s header for the full reasoning.
///
/// <b>What is measured.</b> Frame size, aspect ratio and DPI are measured directly from the
/// photo's own bytes (<see cref="PhotoQualityRasterInspector"/>) - no face-height/background
/// attestation exists here (that was BIO-PHO-002's own booth-specific concern; this story's
/// print side is purely about whether the image file itself is fit to print).
/// </summary>
public static class PrintQualityGate
{
    private const double AspectRatioTolerance = 0.02; // 2%, to absorb rounding in a real-world crop

    public sealed record Verdict(bool Passed, IReadOnlyList<string> Reasons);

    public static Verdict Evaluate(PhotoQualityRasterInspector.Inspected inspected, PrintQualitySpecification specification)
    {
        var reasons = new List<string>();

        if (inspected.WidthPx < specification.FrameWidthPx || inspected.HeightPx < specification.FrameHeightPx)
        {
            reasons.Add(
                $"photo is {inspected.WidthPx}x{inspected.HeightPx}px, below the required minimum " +
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

        // Rounded to the nearest whole DPI before comparing - see CaptureQualityGate's (BIO-PHO-002)
        // identical comment for why an un-rounded comparison would wrongly reject a genuinely
        // on-target capture (a PNG's pHYs chunk stores pixels-per-metre, not DPI directly).
        if (inspected.NativeDpi is { } nativeDpi && Math.Round(nativeDpi) < specification.MinimumDpi)
        {
            reasons.Add($"embedded DPI {nativeDpi:F0} is below the required minimum {specification.MinimumDpi}");
        }

        return new Verdict(reasons.Count == 0, reasons);
    }
}
