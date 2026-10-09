using System.Security.Cryptography;
using System.Text;
using Biolog.Features.Authentication;
using Xunit;

namespace Biolog.Features.Authentication.Tests;

/// <summary>BIO-INT-001-AC03: MFA policy hooks exist - proven against the deterministic
/// in-memory stand-in directly (the orchestrator-level proof lives in
/// AuthenticationServiceTests). Also covers BIO-INT-001-F01 (Zainab, High, fixed): the code
/// must never be derivable from the challenge id.</summary>
public sealed class MfaChallengeProviderTests
{
    [Fact]
    public async Task The_code_returned_at_issuance_verifies_successfully()
    {
        var provider = new InMemoryMfaChallengeProvider();
        var (challenge, code) = await provider.IssueChallengeAsync("ten_a", "subject-1", CancellationToken.None);

        var verified = await provider.VerifyAsync(challenge.ChallengeId, code, CancellationToken.None);

        Assert.True(verified);
    }

    [Fact]
    public async Task A_wrong_code_fails_verification()
    {
        var provider = new InMemoryMfaChallengeProvider();
        var (challenge, _) = await provider.IssueChallengeAsync("ten_a", "subject-1", CancellationToken.None);

        var verified = await provider.VerifyAsync(challenge.ChallengeId, "000000", CancellationToken.None);

        Assert.False(verified);
    }

    [Fact]
    public async Task A_challenge_is_single_use_even_when_the_first_attempt_was_correct()
    {
        var provider = new InMemoryMfaChallengeProvider();
        var (challenge, code) = await provider.IssueChallengeAsync("ten_a", "subject-1", CancellationToken.None);

        var first = await provider.VerifyAsync(challenge.ChallengeId, code, CancellationToken.None);
        var second = await provider.VerifyAsync(challenge.ChallengeId, code, CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task A_challenge_is_single_use_even_when_the_first_attempt_was_wrong()
    {
        var provider = new InMemoryMfaChallengeProvider();
        var (challenge, code) = await provider.IssueChallengeAsync("ten_a", "subject-1", CancellationToken.None);

        var first = await provider.VerifyAsync(challenge.ChallengeId, "000000", CancellationToken.None);
        var second = await provider.VerifyAsync(challenge.ChallengeId, code, CancellationToken.None);

        Assert.False(first);
        Assert.False(second); // the correct code no longer works either - the challenge was already consumed
    }

    [Fact]
    public async Task An_unknown_challenge_id_fails_verification()
    {
        var provider = new InMemoryMfaChallengeProvider();

        var verified = await provider.VerifyAsync(Guid.NewGuid().ToString(), "000000", CancellationToken.None);

        Assert.False(verified);
    }

    /// <summary>BIO-INT-001-F01 (Zainab, High): regression test for the exact vulnerability -
    /// the previous revision derived the code as a fixed SHA-256 prefix of the challenge id,
    /// so any caller who received the id (every legitimate mfa_required response) could
    /// compute the code without ever seeing it delivered. Proves that formula no longer
    /// reconstructs the real code, and that guessing with it fails verification.</summary>
    [Fact]
    public async Task An_attacker_who_knows_only_the_challenge_id_cannot_derive_the_code()
    {
        var provider = new InMemoryMfaChallengeProvider();
        var (challenge, actualCode) = await provider.IssueChallengeAsync("ten_a", "subject-1", CancellationToken.None);

        var oldVulnerableFormula = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(challenge.ChallengeId)))[..6];
        Assert.NotEqual(actualCode, oldVulnerableFormula, StringComparer.OrdinalIgnoreCase);

        var guessedWithOldFormula = await provider.VerifyAsync(challenge.ChallengeId, oldVulnerableFormula, CancellationToken.None);
        Assert.False(guessedWithOldFormula);
    }
}
