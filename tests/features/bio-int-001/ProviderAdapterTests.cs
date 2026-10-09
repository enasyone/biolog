using Biolog.Features.Authentication;
using Xunit;

namespace Biolog.Features.Authentication.Tests;

/// <summary>Covers the five federated adapters (BIO-INT-001's "selectable ... adapters") -
/// PasswordProviderAdapter is covered separately in AuthenticationServiceTests since it needs
/// a live ICredentialStore, not just a config record.</summary>
public sealed class ProviderAdapterTests
{
    public static TheoryData<IAuthProviderAdapter, AuthProviderType> FederatedAdapters() => new()
    {
        { new EntraIdProviderAdapter(), AuthProviderType.EntraId },
        { new Saml2ProviderAdapter(), AuthProviderType.Saml2 },
        { new OidcProviderAdapter(), AuthProviderType.Oidc },
        { new GoogleProviderAdapter(), AuthProviderType.Google },
        { new LdapProviderAdapter(), AuthProviderType.Ldap },
    };

    private static AuthProviderConfig Config(AuthProviderType type) => new(
        "provider-1", "ten_a", type, "Test", true, MfaRequirement.None, [], 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Theory]
    [MemberData(nameof(FederatedAdapters))]
    public void Each_adapter_declares_its_own_provider_type(IAuthProviderAdapter adapter, AuthProviderType expected)
    {
        Assert.Equal(expected, adapter.ProviderType);
    }

    [Theory]
    [MemberData(nameof(FederatedAdapters))]
    public async Task Missing_subject_is_an_invalid_assertion(IAuthProviderAdapter adapter, AuthProviderType type)
    {
        var input = new ProviderCredentialInput(new Dictionary<string, string>(), []);

        var result = await adapter.AuthenticateAsync(Config(type), input, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AuthenticationFailureReason.InvalidAssertion, result.FailureReason);
    }

    [Theory]
    [MemberData(nameof(FederatedAdapters))]
    public async Task Default_simulate_value_succeeds_and_passes_asserted_claims_through_unchanged(IAuthProviderAdapter adapter, AuthProviderType type)
    {
        var claims = new List<ProviderClaim> { new("group", "registrars"), new("department", "hr") };
        var input = new ProviderCredentialInput(new Dictionary<string, string> { ["subject"] = "subject-1" }, claims);

        var result = await adapter.AuthenticateAsync(Config(type), input, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("subject-1", result.SubjectId);
        Assert.Equal(claims, result.Claims);
    }

    [Theory]
    [MemberData(nameof(FederatedAdapters))]
    public async Task Simulate_invalid_assertion_fails_with_the_matching_reason(IAuthProviderAdapter adapter, AuthProviderType type)
    {
        var input = new ProviderCredentialInput(
            new Dictionary<string, string> { ["subject"] = "subject-1", ["simulate"] = "invalid_assertion" }, []);

        var result = await adapter.AuthenticateAsync(Config(type), input, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AuthenticationFailureReason.InvalidAssertion, result.FailureReason);
    }

    [Theory]
    [MemberData(nameof(FederatedAdapters))]
    public async Task Simulate_provider_unavailable_fails_with_the_matching_reason(IAuthProviderAdapter adapter, AuthProviderType type)
    {
        var input = new ProviderCredentialInput(
            new Dictionary<string, string> { ["subject"] = "subject-1", ["simulate"] = "provider_unavailable" }, []);

        var result = await adapter.AuthenticateAsync(Config(type), input, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AuthenticationFailureReason.ProviderUnavailable, result.FailureReason);
    }
}
