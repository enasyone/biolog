using System.Text.Json.Serialization;

namespace Biolog.Features.Authentication;

/// <summary>Selectable identity providers per the BIO-INT-001 user story.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuthProviderType>))]
public enum AuthProviderType
{
    Password,
    EntraId,
    Saml2,
    Oidc,
    Google,
    Ldap,
}

[JsonConverter(typeof(JsonStringEnumConverter<MfaRequirement>))]
public enum MfaRequirement
{
    None,
    Required,
}

/// <summary>AC04: never a bare exception message - a closed, auditable reason code.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuthenticationFailureReason>))]
public enum AuthenticationFailureReason
{
    ProviderDisabled,
    InvalidCredentials,
    InvalidAssertion,
    ProviderUnavailable,
    MfaFailed,
    MfaChallengeExpired,
}

[JsonConverter(typeof(JsonStringEnumConverter<AuthAuditAction>))]
public enum AuthAuditAction
{
    LoginSucceeded,
    LoginFailed,
    MfaChallengeIssued,
    MfaVerified,
    MfaFailed,
    AccountLinked,
}
