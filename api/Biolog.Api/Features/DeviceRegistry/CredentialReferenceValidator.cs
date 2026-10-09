using System.Text.RegularExpressions;

namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>
/// BIO-DEV-001-QA-04 (Chioma, acceptance): naming a property "CredentialReference" is not an
/// enforceable boundary - the acceptance probe demonstrated that an ordinary plaintext string
/// (<c>synthetic-plaintext-password</c>) passed straight through <see cref="InMemoryDeviceRepository.CreateAsync"/>
/// unchanged, because nothing validated its shape. <see cref="Device.CredentialReference"/>
/// (BIO-DEV-001-AC02) must be an opaque pointer into a supported secret store, never
/// credential material itself and never an arbitrary URL that could carry embedded userinfo
/// credentials (a scheme, then a username, a colon, a password, and finally
/// <c>@host/...</c>).
///
/// Enforced at both boundaries the finding named: the request boundary
/// (<see cref="DeviceService.RegisterAsync"/>, before a tenant-scoped write is even attempted)
/// and the repository boundary (<see cref="InMemoryDeviceRepository.CreateAsync"/>, so a
/// caller that reaches the repository directly - as the acceptance probe does, bypassing the
/// service entirely - cannot slip plaintext in either). This mirrors the belt-and-braces
/// posture <see cref="InMemoryDeviceRepository"/> already takes for tenant isolation: the
/// property name is documentation, the validation at both call sites is the actual boundary.
/// </summary>
public static partial class CredentialReferenceValidator
{
    // "kv" (key-vault-style) is the scheme this story's own developer tests already used
    // before this validator existed; kept as supported rather than churning every fixture.
    private static readonly string[] SupportedSchemes = ["vault", "kv", "kms", "secretref"];

    [GeneratedRegex(@"^(vault|kv|kms|secretref)://[A-Za-z0-9][A-Za-z0-9._/-]{0,255}$")]
    private static partial Regex SupportedReferencePattern();

    /// <summary>
    /// Throws <see cref="InvalidCredentialReferenceException"/> unless
    /// <paramref name="credentialReference"/> is either absent (AC02 leaves it optional - a
    /// device can be registered before its credential is provisioned) or a well-formed
    /// reference under one of <see cref="SupportedSchemes"/>.
    /// </summary>
    public static void Validate(string? credentialReference)
    {
        if (credentialReference is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(credentialReference))
        {
            throw new InvalidCredentialReferenceException(
                "credential_reference must not be empty or whitespace-only");
        }

        if (credentialReference.Any(char.IsControl))
        {
            throw new InvalidCredentialReferenceException(
                "credential_reference must not contain control characters");
        }

        if (credentialReference.Contains('@'))
        {
            // The one character no supported reference scheme ever needs, and the one
            // character every "userinfo@host" credential-bearing URL requires - rejecting it
            // outright closes that entire shape without having to parse and inspect a URI.
            throw new InvalidCredentialReferenceException(
                "credential_reference must not embed userinfo - a credential-bearing URL is not a supported reference");
        }

        if (!SupportedReferencePattern().IsMatch(credentialReference))
        {
            var schemes = string.Join(", ", SupportedSchemes.Select(scheme => $"{scheme}://"));
            throw new InvalidCredentialReferenceException(
                $"credential_reference must use a supported scheme ({schemes}) followed by an opaque " +
                "identifier, not plaintext or an arbitrary URL");
        }
    }
}
