namespace Biolog.Features.FaceEnrolment.Domain;

/// <summary>One named check's verdict (AC02). <see cref="Guidance"/> is required exactly when
/// <see cref="Passed"/> is false (AC04: "poor capture yields actionable recapture guidance") - a
/// failed check with no guidance would tell an operator WHAT is wrong but not what to DO about
/// it.</summary>
public sealed record FaceQualityCheckResult
{
    public required FaceQualityCheck Check { get; init; }
    public required bool Passed { get; init; }
    public string? Guidance { get; init; }

    public static FaceQualityCheckResult Pass(FaceQualityCheck check) => new() { Check = check, Passed = true };

    public static FaceQualityCheckResult Fail(FaceQualityCheck check, string guidance)
    {
        if (string.IsNullOrWhiteSpace(guidance))
        {
            throw new ArgumentException("a failed check must carry actionable guidance (AC04)", nameof(guidance));
        }
        return new FaceQualityCheckResult { Check = check, Passed = false, Guidance = guidance };
    }
}
