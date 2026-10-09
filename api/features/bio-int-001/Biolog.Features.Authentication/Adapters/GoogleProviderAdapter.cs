namespace Biolog.Features.Authentication;

/// <summary>Simulated optional Google Workspace federation - see
/// <see cref="EntraIdProviderAdapter"/>'s own doc comment for why simulation, not a real Google
/// OAuth endpoint, is this adapter's boundary.</summary>
public sealed class GoogleProviderAdapter : IAuthProviderAdapter
{
    public AuthProviderType ProviderType => AuthProviderType.Google;

    public Task<ProviderAuthenticationResult> AuthenticateAsync(
        AuthProviderConfig config, ProviderCredentialInput input, CancellationToken cancellationToken)
    {
        if (!input.Credentials.TryGetValue("subject", out var subject) || string.IsNullOrEmpty(subject))
        {
            return Task.FromResult(ProviderAuthenticationResult.Fail(AuthenticationFailureReason.InvalidAssertion));
        }

        var simulate = input.Credentials.GetValueOrDefault("simulate");
        var result = simulate switch
        {
            "invalid_assertion" => ProviderAuthenticationResult.Fail(AuthenticationFailureReason.InvalidAssertion),
            "provider_unavailable" => ProviderAuthenticationResult.Fail(AuthenticationFailureReason.ProviderUnavailable),
            _ => ProviderAuthenticationResult.Ok(subject, input.AssertedClaims),
        };

        return Task.FromResult(result);
    }
}
