namespace Biolog.Features.Authentication;

/// <summary>
/// AC02 core: "no provider-specific claims leak into authorization without mapping" - a raw
/// claim never becomes a role by itself. Only a claim that exactly matches a rule the tenant's
/// own <see cref="AuthProviderConfig"/> declares contributes a role; every other raw claim
/// (unrecognized type, unrecognized value, or simply absent from the rule set) contributes
/// nothing - there is no default/deny-by-role-absence distinction to get wrong, because there
/// is no default at all.
/// </summary>
public static class ClaimMappingService
{
    public static IReadOnlySet<string> MapClaims(
        IReadOnlyList<ClaimMappingRule> rules,
        IReadOnlyList<ProviderClaim> rawClaims)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in rawClaims)
        {
            foreach (var rule in rules)
            {
                if (rule.Matches(claim))
                {
                    roles.Add(rule.MappedRole);
                }
            }
        }

        return roles;
    }
}
