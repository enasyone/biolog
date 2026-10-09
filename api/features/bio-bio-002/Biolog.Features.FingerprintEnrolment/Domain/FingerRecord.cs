using Biolog.Features.Biometrics.Domain;

namespace Biolog.Features.FingerprintEnrolment.Domain;

/// <summary>
/// BIO-BIO-002-AC02: one finger's identity, quality and resolution state within a session.
///
/// <b>AC04 - raw images are discarded by default.</b> <see cref="RetainedRawImage"/> is null on
/// EVERY capture unless the owning <see cref="FingerprintEnrolmentSession.RetentionAuthorised"/>
/// was true when the session was started - <see cref="Service.FingerprintEnrolmentService.CaptureFingerAsync"/>
/// never populates it otherwise, so there is no code path where a raw image survives past the
/// method call that derived <see cref="Template"/> from it unless a caller explicitly authorised
/// retention up front. <see cref="Template"/> itself is BIO-BIO-001's own
/// <see cref="BiometricTemplate"/> - reused directly, not re-derived - so this feature never
/// re-implements AC02's provider-isolation guarantee, only builds a guided session on top of it.
///
/// <b>AC03 - recapture and exceptions.</b> <see cref="CaptureCount"/> increments on every
/// successful capture of the SAME position (a recapture supersedes the previous template/
/// quality/raw-image, it does not accumulate a history); <see cref="Status"/> becomes
/// <see cref="FingerCaptureStatus.MissingAmputated"/> only through
/// <see cref="Service.FingerprintEnrolmentService.MarkMissingOrAmputatedAsync"/>, which is
/// mutually exclusive with capture (see that method's remarks).
/// </summary>
public sealed record FingerRecord
{
    public required FingerPosition Position { get; init; }
    public required FingerCaptureStatus Status { get; init; }
    public int? QualityScore { get; init; }
    public BiometricTemplate? Template { get; init; }
    public int CaptureCount { get; init; }
    public string? MissingAmputatedReason { get; init; }
    public string UpdatedAtUtc { get; init; } = string.Empty;

    private readonly byte[]? _retainedRawImage;

    /// <summary>Only non-null when the session's <c>RetentionAuthorised</c> flag was set at
    /// capture time (AC04). Clones on write/read like BIO-BIO-001's payload types (BIO-BIO-001
    /// -QA-01) - no aliasing between a caller's array and this record's own state.</summary>
    public byte[]? RetainedRawImage
    {
        get => _retainedRawImage is null ? null : (byte[])_retainedRawImage.Clone();
        init => _retainedRawImage = value is null ? null : (byte[])value.Clone();
    }

    public static FingerRecord CreatePending(FingerPosition position) => new()
    {
        Position = position,
        Status = FingerCaptureStatus.Pending,
        UpdatedAtUtc = string.Empty,
    };
}
