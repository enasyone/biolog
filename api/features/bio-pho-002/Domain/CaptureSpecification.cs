namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// BIO-PHO-002-AC01: "Capture specification includes frame size, aspect ratio, minimum DPI,
/// face-height/background rules." No master-spec section or ADR names concrete values for any
/// of these at the time of writing, so <see cref="Current"/> is this story's own, deliberately
/// named baseline - one place to revise, not a magic number scattered through the feature, the
/// same resolution BIO-CRD-001 used for its own unspecified DPI floor and BIO-PHO-001 used for
/// its own unspecified derivative target sizes. Chosen to match a conventional passport/ID-photo
/// specification (a 35mm x 45mm frame at 300 DPI, ICAO-style face-height guidance), since the
/// epic (Photography & Photo Booth, feeding card production and biometric matching) is exactly
/// that use case.
///
/// <b>Versioned, per AC01's "versioned API contract."</b> <see cref="SpecificationVersion"/> is
/// the whole contract's version (not a per-record ADR-0001 aggregate version) - a booth fetches
/// it once and should reject captures itself if it does not recognise the version it receives,
/// though this story cannot enforce that on an external device; BIOLOG's own enforcement is
/// <see cref="CaptureQualityGate"/> checking every submission against <see cref="Current"/>
/// regardless of what the booth believed it was targeting.
///
/// <b>Face-height/background rules are declared here but not independently verifiable from a
/// pixel-level check</b> - no computer-vision library exists anywhere in this host (the same
/// disclosed boundary <see cref="CaptureRasterInspector"/>'s header names). A submission
/// self-attests <see cref="Api.SubmitCaptureRequest.FaceHeightCompliant"/> and
/// <see cref="Api.SubmitCaptureRequest.BackgroundCompliant"/>; <see cref="CaptureQualityGate"/>
/// treats a `false` (or omitted true) attestation as a quality failure exactly like a measured
/// DPI/dimension failure - see that type's header for the full reasoning.
/// </summary>
public sealed record CaptureSpecification
{
    public required int SpecificationVersion { get; init; }
    public required int FrameWidthPx { get; init; }
    public required int FrameHeightPx { get; init; }
    public required int AspectRatioWidth { get; init; }
    public required int AspectRatioHeight { get; init; }
    public required int MinimumDpi { get; init; }
    public required double MinFaceHeightRatio { get; init; }
    public required double MaxFaceHeightRatio { get; init; }
    public required string BackgroundRequirement { get; init; }

    /// <summary>The one, fixed baseline this story ships - not tenant-configurable (no AC asks for that), the same "fixed baseline catalogue" shape BIO-IDN-006's RoleType uses.</summary>
    public static readonly CaptureSpecification Current = new()
    {
        SpecificationVersion = 1,
        FrameWidthPx = 1050, // 35mm at 300dpi (rounded up from 413.4)
        FrameHeightPx = 1350, // 45mm at 300dpi (rounded up from 531.5)
        AspectRatioWidth = 7,
        AspectRatioHeight = 9, // 35:45 reduces to 7:9
        MinimumDpi = 300,
        MinFaceHeightRatio = 0.50,
        MaxFaceHeightRatio = 0.69,
        BackgroundRequirement = "plain, uniform, light-coloured background with no shadows or patterns",
    };
}
