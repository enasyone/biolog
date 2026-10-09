using System.Security.Cryptography;

namespace Biolog.Features.Authentication;

public sealed record MfaChallenge(string ChallengeId, string TenantId, string SubjectId, DateTimeOffset ExpiresAt);

public sealed record MfaVerificationRequest(string Code);

/// <summary>
/// BIO-INT-001-AC03: "MFA policy hooks exist" - this interface IS the hook. A tenant's
/// <see cref="AuthProviderConfig.Mfa"/> of <see cref="MfaRequirement.Required"/> makes
/// <see cref="AuthenticationService"/> issue a challenge here and refuse to complete sign-in
/// until <see cref="VerifyAsync"/> returns <c>true</c> - a real SMS/TOTP/push-notification
/// provider is a pluggable future adapter behind this same interface; delivering an actual
/// out-of-band code is explicitly out of this foundation story's scope (no SMS/email gateway
/// exists anywhere in this codebase to send one through).
///
/// **Security-review fix (Zainab, BIO-INT-001-F01, High):** the code is returned ONLY to the
/// direct in-process caller of <see cref="IssueChallengeAsync"/>, alongside the
/// <see cref="MfaChallenge"/> whose <c>ChallengeId</c> IS handed to the (possibly external)
/// caller of the live authenticate endpoint. The code must never be derivable from the
/// challenge id or any other value an external caller can observe - an earlier revision
/// derived it as a fixed hash of the challenge id itself, which meant any caller who received
/// the id (as every legitimate `mfa_required` response necessarily does) could compute the
/// "second factor" themselves, defeating MFA entirely. That derivable-from-public-input shape
/// must never return - a correct implementation's code is independent random state that only
/// this call's return value (never re-derivable, never serialized to any HTTP response) knows.
/// </summary>
public interface IMfaChallengeProvider
{
    /// <summary>Returns the challenge (safe to hand to an external caller) and the code
    /// (never safe to hand to an external caller - see this interface's own remarks). A real
    /// out-of-band adapter would send <c>Code</c> through its own channel here instead of
    /// returning it; this in-memory stand-in has no such channel, so it hands the code back
    /// in-process to whichever trusted server-side caller requested the challenge.</summary>
    Task<(MfaChallenge Challenge, string Code)> IssueChallengeAsync(string tenantId, string subjectId, CancellationToken cancellationToken);

    /// <summary>One-time use: a challenge that has already been verified (successfully or not)
    /// cannot be verified again - proven by <c>Mfa_challenge_is_single_use</c>.</summary>
    Task<bool> VerifyAsync(string challengeId, string code, CancellationToken cancellationToken);
}

/// <summary>
/// In-memory stand-in for a real out-of-band MFA transport: the code is generated with
/// <see cref="RandomNumberGenerator"/> (a six-digit numeric code, the same shape a real
/// SMS/TOTP code would have), independent of the challenge id and every other observable
/// value - there is deliberately no formula anyone, including this class's own callers,
/// could use to reconstruct it from the id alone. Completing a REAL (non-demo) MFA-required
/// sign-in through the live endpoints therefore genuinely requires a real out-of-band
/// transport adapter to be plugged in behind this interface before it can succeed - a
/// disclosed, deliberate limitation of this foundation story, not a hidden gap. This is fine:
/// the live routes remain honestly unable to complete a real MFA-required login until then,
/// rather than being trivially bypassable in the meantime.
/// </summary>
public sealed class InMemoryMfaChallengeProvider : IMfaChallengeProvider
{
    private readonly Dictionary<string, (MfaChallenge Challenge, string Code)> _pending = [];
    private readonly HashSet<string> _consumed = [];
    private readonly Lock _gate = new();

    public Task<(MfaChallenge Challenge, string Code)> IssueChallengeAsync(string tenantId, string subjectId, CancellationToken cancellationToken)
    {
        var challenge = new MfaChallenge(Guid.NewGuid().ToString(), tenantId, subjectId, DateTimeOffset.UtcNow.AddMinutes(5));
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        lock (_gate)
        {
            _pending[challenge.ChallengeId] = (challenge, code);
        }

        return Task.FromResult((challenge, code));
    }

    public Task<bool> VerifyAsync(string challengeId, string code, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_consumed.Contains(challengeId) || !_pending.TryGetValue(challengeId, out var pending))
            {
                return Task.FromResult(false);
            }

            _consumed.Add(challengeId);
            _pending.Remove(challengeId);

            if (pending.Challenge.ExpiresAt < DateTimeOffset.UtcNow)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(pending.Code), System.Text.Encoding.UTF8.GetBytes(code)));
        }
    }
}
