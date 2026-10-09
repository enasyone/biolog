namespace Biolog.Features.Authentication;

public enum AuthenticationOutcomeKind
{
    Success,
    MfaRequired,
    Failure,
}

/// <summary>Result of <see cref="AuthenticationService.AuthenticateAsync"/> or
/// <see cref="AuthenticationService.CompleteMfaAsync"/> - exactly one of the three factory
/// methods produces a valid instance, matching <c>ProviderAuthenticationResult</c>'s own
/// factory-only discipline.</summary>
public sealed record AuthenticationOutcome
{
    public required AuthenticationOutcomeKind Kind { get; init; }
    public AuthenticatedPrincipal? Principal { get; init; }
    public MfaChallenge? Challenge { get; init; }
    public AuthenticationFailureReason? FailureReason { get; init; }

    /// <summary>NEVER safe to put in an HTTP response - see <see cref="IMfaChallengeProvider"/>'s
    /// own remarks (BIO-INT-001-F01). <c>internal</c> so only this assembly and
    /// <c>Biolog.Api</c> (via <c>InternalsVisibleTo</c>) can even see the property exists;
    /// System.Text.Json also does not serialize non-public members by default, and
    /// <c>BioInt001Module</c>'s own HTTP mapping explicitly whitelists response fields rather
    /// than serializing this record directly - three independent reasons this can never reach
    /// an external caller, not one. Populated only on the <see cref="AuthenticationOutcomeKind.MfaRequired"/>
    /// branch, solely so a fully server-side, same-request demonstration flow (this story's own
    /// AC05 endpoint) can complete a challenge it just issued without a real out-of-band
    /// transport existing yet - it is never read back FROM an external caller, and no
    /// production sign-in can complete a real MFA-required login through the live endpoints
    /// until a real transport adapter is plugged in (a disclosed limitation, not a gap).</summary>
    internal string? DemoOnlyMfaCode { get; init; }

    public static AuthenticationOutcome Success(AuthenticatedPrincipal principal) =>
        new() { Kind = AuthenticationOutcomeKind.Success, Principal = principal };

    public static AuthenticationOutcome MfaRequired(MfaChallenge challenge, string demoOnlyCode) =>
        new() { Kind = AuthenticationOutcomeKind.MfaRequired, Challenge = challenge, DemoOnlyMfaCode = demoOnlyCode };

    public static AuthenticationOutcome Failure(AuthenticationFailureReason reason) =>
        new() { Kind = AuthenticationOutcomeKind.Failure, FailureReason = reason };
}
