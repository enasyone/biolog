namespace Biolog.Features.Authentication;

/// <summary>
/// What a caller presents to a provider adapter. <see cref="Credentials"/> carries
/// provider-specific transport fields (Password: <c>username</c>/<c>password</c>; every
/// federated provider: a <c>simulate</c> marker - see each adapter's own doc comment - since
/// no real Entra ID/SAML2/OIDC/Google/LDAP endpoint exists to call from this sandboxed
/// codebase, the same deterministic-simulation precedent BIO-RPT-001's channel adapters and
/// BIO-DEV-003's vendor SDK simulators already established). <see cref="AssertedClaims"/> is
/// what the (simulated) identity provider asserts about the subject on success - federated
/// adapters pass it straight through unchanged; <see cref="PasswordProviderAdapter"/> ignores
/// it and returns the credential store's own recorded claims instead, since a password
/// provider is not fronted by a third-party claims source.
/// </summary>
public sealed record ProviderCredentialInput(
    IReadOnlyDictionary<string, string> Credentials,
    IReadOnlyList<ProviderClaim> AssertedClaims);

/// <summary>
/// An adapter's own verdict, before any claim mapping happens. <see cref="Ok"/>/<see cref="Fail"/>
/// are the only construction paths - never build this record directly - mirroring
/// <c>ChannelDeliveryResult</c>'s own factory-only shape in BIO-RPT-001.
/// </summary>
public sealed record ProviderAuthenticationResult
{
    public required bool Success { get; init; }
    public string? SubjectId { get; init; }
    public IReadOnlyList<ProviderClaim>? Claims { get; init; }
    public AuthenticationFailureReason? FailureReason { get; init; }

    public static ProviderAuthenticationResult Ok(string subjectId, IReadOnlyList<ProviderClaim> claims) =>
        new() { Success = true, SubjectId = subjectId, Claims = claims };

    public static ProviderAuthenticationResult Fail(AuthenticationFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}

/// <summary>
/// One adapter per <see cref="AuthProviderType"/> (BIO-INT-001's own "selectable ...
/// adapters" - the user story's exact word). <see cref="AuthenticationService"/> dictionary-
/// dispatches by <see cref="ProviderType"/> and never branches on the enum itself, the same
/// separation discipline BIO-RPT-001-AC02 established for its own channel adapters.
/// </summary>
public interface IAuthProviderAdapter
{
    AuthProviderType ProviderType { get; }

    Task<ProviderAuthenticationResult> AuthenticateAsync(
        AuthProviderConfig config, ProviderCredentialInput input, CancellationToken cancellationToken);
}
