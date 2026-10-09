namespace Biolog.Features.Authentication;

/// <summary>
/// BIOLOG's own native password provider (the user story's first-listed adapter). Ignores
/// <see cref="ProviderCredentialInput.AssertedClaims"/> entirely - a password provider is not
/// fronted by a third-party claims source, so it only ever returns the claims
/// <see cref="ICredentialStore"/> recorded at registration time (e.g. directory group
/// memberships), never anything the caller of this authenticate call asserts.
/// </summary>
public sealed class PasswordProviderAdapter(ICredentialStore credentialStore) : IAuthProviderAdapter
{
    public AuthProviderType ProviderType => AuthProviderType.Password;

    public async Task<ProviderAuthenticationResult> AuthenticateAsync(
        AuthProviderConfig config, ProviderCredentialInput input, CancellationToken cancellationToken)
    {
        if (!input.Credentials.TryGetValue("username", out var username) ||
            !input.Credentials.TryGetValue("password", out var password) ||
            string.IsNullOrEmpty(username))
        {
            return ProviderAuthenticationResult.Fail(AuthenticationFailureReason.InvalidCredentials);
        }

        var claims = await credentialStore.VerifyAsync(config.TenantId, config.ProviderId, username, password, cancellationToken);
        return claims is null
            ? ProviderAuthenticationResult.Fail(AuthenticationFailureReason.InvalidCredentials)
            : ProviderAuthenticationResult.Ok(username, claims);
    }
}
