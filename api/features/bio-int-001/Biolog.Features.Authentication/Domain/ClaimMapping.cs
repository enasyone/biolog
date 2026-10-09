namespace Biolog.Features.Authentication;

/// <summary>A single claim a provider asserts about an authenticated subject.</summary>
public sealed record ProviderClaim(string Type, string Value);

/// <summary>
/// AC02: the ONLY mechanism by which a provider claim can ever become authorization-relevant.
/// An exact (ClaimType, ClaimValue) match is required to contribute <see cref="MappedRole"/>;
/// there is no wildcard/regex form, and no default/fallback role for an unmatched claim.
/// <see cref="MappedRole"/> must be one of <see cref="KnownRoles.WireFormats"/>.
/// </summary>
public sealed record ClaimMappingRule(string ClaimType, string ClaimValue, string MappedRole)
{
    public bool Matches(ProviderClaim claim) =>
        string.Equals(ClaimType, claim.Type, StringComparison.Ordinal) &&
        string.Equals(ClaimValue, claim.Value, StringComparison.Ordinal);
}
