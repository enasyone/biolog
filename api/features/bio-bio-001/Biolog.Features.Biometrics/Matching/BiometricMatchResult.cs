namespace Biolog.Features.Biometrics.Matching;

/// <summary>Outcome of comparing two <see cref="Domain.BiometricTemplate"/> instances (1:1
/// verify). <see cref="Score"/> is matcher-defined (higher is closer) and not comparable across
/// different <see cref="MatcherId"/> implementations.</summary>
public sealed record BiometricMatchResult
{
    public required bool IsMatch { get; init; }
    public required double Score { get; init; }
    public required string MatcherId { get; init; }
    public required string MatchedTemplateId { get; init; }
}
