namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// BIO-ACC-001-AC01's "required assurance" dimension: how strongly the presented credential's
/// origin was itself verified, independent of whether the credential class/location/schedule/
/// status predicates match. Three ordered levels, declared in ascending order deliberately -
/// <see cref="AssuranceLevelExtensions.Satisfies"/> compares the underlying enum ordinal, so a
/// caller presenting <see cref="High"/> satisfies a rule requiring <see cref="Basic"/> or
/// <see cref="Elevated"/>, never the reverse.
/// </summary>
public enum AssuranceLevel
{
    /// <summary>A credential was presented and matched; no additional factor verified.</summary>
    Basic,

    /// <summary>A second factor accompanied the credential (e.g. a PIN or a biometric match alongside the card).</summary>
    Elevated,

    /// <summary>Multiple independent factors were verified, or a live biometric match against a trusted store.</summary>
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

    public static bool TryParse(string? value, out AssuranceLevel level)
    {
        switch (value)
        {
            case "BASIC": level = AssuranceLevel.Basic; return true;
            case "ELEVATED": level = AssuranceLevel.Elevated; return true;
            case "HIGH": level = AssuranceLevel.High; return true;
            default: level = default; return false;
        }
    }

    /// <summary>True when <paramref name="presented"/> is at least as strong as <paramref name="required"/>.</summary>
    public static bool Satisfies(this AssuranceLevel presented, AssuranceLevel required) => presented >= required;
}
