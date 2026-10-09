namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>
/// Mirrors BIO-ACC-001's own <c>Domain.AssuranceLevel</c> wire strings exactly - three ordered
/// levels this feature DERIVES from biometric evidence (<see cref="AccessDecisionCombiner.DeriveAssuranceLevel"/>,
/// AC01) rather than resolves from a gateway, then passes across <see cref="IPolicyDecisionGateway"/>
/// as a plain wire string for BIO-ACC-001's own policy engine to interpret.
/// </summary>
public enum AssuranceLevel
{
    Basic,
    Elevated,
    High,
}

public static class AssuranceLevelExtensions
{
    public static string ToWireString(this AssuranceLevel level) => level switch
    {
        AssuranceLevel.Basic => "BASIC",
        AssuranceLevel.Elevated => "ELEVATED",
        AssuranceLevel.High => "HIGH",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "unreachable: exhaustive switch"),
    };
}
