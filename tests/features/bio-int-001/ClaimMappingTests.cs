using Biolog.Features.Authentication;
using Xunit;

namespace Biolog.Features.Authentication.Tests;

/// <summary>BIO-INT-001-AC02: no provider-specific claim becomes a role without an explicit mapping rule.</summary>
public sealed class ClaimMappingTests
{
    [Fact]
    public void An_unmapped_claim_contributes_no_role()
    {
        var rules = new List<ClaimMappingRule> { new("group", "registrars", "RegistrarHr") };
        var claims = new List<ProviderClaim> { new("group", "some-other-group") };

        var roles = ClaimMappingService.MapClaims(rules, claims);

        Assert.Empty(roles);
    }

    [Fact]
    public void A_matching_claim_type_and_value_contributes_its_mapped_role()
    {
        var rules = new List<ClaimMappingRule> { new("group", "registrars", "RegistrarHr") };
        var claims = new List<ProviderClaim> { new("group", "registrars") };

        var roles = ClaimMappingService.MapClaims(rules, claims);

        Assert.Equal(["RegistrarHr"], roles);
    }

    [Fact]
    public void A_matching_type_with_a_different_value_does_not_match()
    {
        var rules = new List<ClaimMappingRule> { new("group", "registrars", "RegistrarHr") };
        var claims = new List<ProviderClaim> { new("group", "REGISTRARS") }; // case-sensitive, deliberately different

        var roles = ClaimMappingService.MapClaims(rules, claims);

        Assert.Empty(roles);
    }

    [Fact]
    public void Multiple_distinct_claims_can_map_to_multiple_distinct_roles()
    {
        var rules = new List<ClaimMappingRule>
        {
            new("group", "registrars", "RegistrarHr"),
            new("group", "gate-staff", "Gate"),
        };
        var claims = new List<ProviderClaim> { new("group", "registrars"), new("group", "gate-staff") };

        var roles = ClaimMappingService.MapClaims(rules, claims);

        Assert.Equal(new HashSet<string> { "RegistrarHr", "Gate" }, roles);
    }

    [Fact]
    public void A_hostile_claim_type_that_looks_like_a_privileged_group_never_leaks_a_role_without_a_rule()
    {
        var rules = new List<ClaimMappingRule> { new("group", "registrars", "RegistrarHr") };
        var claims = new List<ProviderClaim> { new("group", "super-admin"), new("role", "SuperAdmin"), new("SuperAdmin", "true") };

        var roles = ClaimMappingService.MapClaims(rules, claims);

        Assert.Empty(roles);
    }

    [Fact]
    public void Publishing_no_rules_at_all_maps_every_claim_to_nothing()
    {
        var claims = new List<ProviderClaim> { new("group", "registrars") };

        var roles = ClaimMappingService.MapClaims([], claims);

        Assert.Empty(roles);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("TenantAdmin")]
    [InlineData("RegistrarHr")]
    [InlineData("Enrolment")]
    [InlineData("Biometric")]
    [InlineData("CardProduction")]
    [InlineData("Security")]
    [InlineData("Gate")]
    [InlineData("Auditor")]
    [InlineData("Helpdesk")]
    [InlineData("ReadOnly")]
    public void KnownRoles_recognizes_every_one_of_BIO_IDN_006s_eleven_wire_strings(string role)
    {
        Assert.True(KnownRoles.IsKnown(role));
    }

    [Fact]
    public void KnownRoles_rejects_an_invented_role_name()
    {
        Assert.False(KnownRoles.IsKnown("GlobalOwner"));
    }
}
