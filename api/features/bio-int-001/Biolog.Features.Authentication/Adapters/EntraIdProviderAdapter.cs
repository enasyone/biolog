namespace Biolog.Features.Authentication;

/// <summary>
/// Simulated Microsoft Entra ID / External ID federation. No real Entra ID tenant exists to
/// call from this sandboxed codebase, so this adapter deterministically switches on a
/// caller-supplied <c>simulate</c> credential field - the same precedent BIO-RPT-001's channel
/// adapters and BIO-DEV-003's vendor SDK simulators already established for "no real backend
/// exists yet, so the adapter boundary is proven with a deterministic marker instead of a
/// hardcoded always-success stub". <see cref="ProviderCredentialInput.AssertedClaims"/> is
/// passed straight through unchanged on success - a federation adapter never invents or
/// edits what the (simulated) identity provider asserts; only <see cref="ClaimMappingService"/>
/// decides what any of it means for authorization (AC02).
/// </summary>
public sealed class EntraIdProviderAdapter : IAuthProviderAdapter
{
    public AuthProviderType ProviderType => AuthProviderType.EntraId;

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
