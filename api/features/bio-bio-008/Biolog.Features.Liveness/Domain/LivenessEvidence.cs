namespace Biolog.Features.Liveness.Domain;

/// <summary>
/// BIO-BIO-008-AC01: "Liveness result has provider/method/version/confidence/status." The
/// richer, provider-neutral evidence type this story adds - distinct from BIO-BIO-003's own
/// internal enrolment-only <c>LivenessResult</c> (which carries only IsLive/Score/ProviderId,
/// no Method/ProviderVersion/Status), and never confusable with a face-match outcome (AC02 -
/// see <see cref="LivenessStatus"/>'s own remarks).
/// </summary>
internal sealed record LivenessEvidence
{
    /// <summary>Which provider implementation produced this evidence (never a vendor name baked
    /// into calling code - mirrors BIO-BIO-001's own ProviderId convention).</summary>
    public required string ProviderId { get; init; }

    /// <summary>The liveness detection technique used (e.g. "passive", "challenge-response") -
    /// AC01's own "method" field, distinct from which provider/vendor answered.</summary>
    public required string Method { get; init; }

    public required string ProviderVersion { get; init; }

    /// <summary>0.0-1.0 confidence in <see cref="Status"/>, never a real accuracy claim (no real
    /// anti-spoofing model exists anywhere in this repo - see <see cref="SyntheticLivenessProvider"/>).</summary>
    public required double Confidence { get; init; }

    public required LivenessStatus Status { get; init; }

    /// <summary>Populated only when <see cref="Status"/> is <see cref="LivenessStatus.SpoofDetected"/>
    /// or <see cref="LivenessStatus.Inconclusive"/> - names which adversarial pattern or ambiguity
    /// was recognised, never populated for <see cref="LivenessStatus.Live"/>.</summary>
    public string? Reason { get; init; }
}
