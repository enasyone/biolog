using Biolog.Features.ApiSecurity;
using Xunit;

namespace Biolog.Features.ApiSecurity.Tests;

public sealed class ApiAccessTokenCodecTests
{
    private const string SecretA = "test-access-token-secret-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SecretB = "test-access-token-secret-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void A_freshly_issued_token_validates_and_round_trips_its_fields()
    {
        var codec = new HmacApiAccessTokenCodec(SecretA);
        var scopes = new HashSet<string> { "ReadOnly", "Gate" };
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);

        var token = codec.Issue("ten_a", "client-1", scopes, expiresAt);
        var validated = codec.TryValidate(token, out var accessToken);

        Assert.True(validated);
        Assert.Equal("ten_a", accessToken!.TenantId);
        Assert.Equal("client-1", accessToken.ClientId);
        Assert.Equal(scopes, accessToken.Scopes);
    }

    [Fact]
    public void A_token_signed_with_a_different_secret_does_not_validate()
    {
        var issuer = new HmacApiAccessTokenCodec(SecretA);
        var verifier = new HmacApiAccessTokenCodec(SecretB);

        var token = issuer.Issue("ten_a", "client-1", new HashSet<string>(), DateTimeOffset.UtcNow.AddMinutes(15));
        var validated = verifier.TryValidate(token, out var accessToken);

        Assert.False(validated);
        Assert.Null(accessToken);
    }

    [Fact]
    public void A_tampered_payload_does_not_validate()
    {
        var codec = new HmacApiAccessTokenCodec(SecretA);
        var token = codec.Issue("ten_a", "client-1", new HashSet<string> { "ReadOnly" }, DateTimeOffset.UtcNow.AddMinutes(15));

        var parts = token.Split('.', 2);
        var payloadBytes = Convert.FromBase64String(parts[0]);
        var tamperedPayload = System.Text.Encoding.UTF8.GetString(payloadBytes).Replace("ReadOnly", "SuperAdmin");
        var tamperedToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tamperedPayload)) + "." + parts[1];

        var validated = codec.TryValidate(tamperedToken, out var accessToken);

        Assert.False(validated);
        Assert.Null(accessToken);
    }

    [Fact]
    public void An_expired_token_does_not_validate()
    {
        var codec = new HmacApiAccessTokenCodec(SecretA);
        var token = codec.Issue("ten_a", "client-1", new HashSet<string>(), DateTimeOffset.UtcNow.AddMinutes(-1));

        var validated = codec.TryValidate(token, out var accessToken);

        Assert.False(validated);
        Assert.Null(accessToken);
    }

    // --- Security-review fix (Zainab, ZB-INT003-01, Critical): fail CLOSED, no dummy key ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void An_unconfigured_or_too_short_secret_refuses_to_issue_a_token(string? configuredSecret)
    {
        var codec = new HmacApiAccessTokenCodec(configuredSecret);

        Assert.Throws<InvalidOperationException>(() =>
            codec.Issue("ten_a", "client-1", new HashSet<string>(), DateTimeOffset.UtcNow.AddMinutes(15)));
    }

    [Fact]
    public void Two_independently_unconfigured_codecs_can_no_longer_forge_or_accept_each_others_tokens()
    {
        // This is the exact exploit Zainab's review proved: before this fix, two unconfigured
        // instances shared the same public dummy key and accepted each other's tokens -
        // anyone who read this source could forge a token for any tenant/client/scope. Now
        // neither instance can even ISSUE a token, let alone validate one from a sibling
        // unconfigured instance.
        var unconfiguredA = new HmacApiAccessTokenCodec(null);
        var unconfiguredB = new HmacApiAccessTokenCodec("");

        Assert.Throws<InvalidOperationException>(() =>
            unconfiguredA.Issue("ten_a", "client-1", new HashSet<string>(), DateTimeOffset.UtcNow.AddMinutes(15)));

        // Even a token minted by a DIFFERENTLY-configured codec (simulating a forged guess)
        // must never validate against an unconfigured instance.
        var configured = new HmacApiAccessTokenCodec(SecretA);
        var forgedAttempt = configured.Issue("ten_a", "client-1", new HashSet<string>(), DateTimeOffset.UtcNow.AddMinutes(15));
        Assert.False(unconfiguredB.TryValidate(forgedAttempt, out var accessToken));
        Assert.Null(accessToken);
    }

    [Fact]
    public void A_configured_codec_never_validates_anything_once_unconfigured()
    {
        var configured = new HmacApiAccessTokenCodec(SecretA);
        var token = configured.Issue("ten_a", "client-1", new HashSet<string>(), DateTimeOffset.UtcNow.AddMinutes(15));

        var unconfigured = new HmacApiAccessTokenCodec(null);
        Assert.False(unconfigured.TryValidate(token, out var accessToken));
        Assert.Null(accessToken);
    }
}

public sealed class ApiSecretHasherTests
{
    private const string PepperA = "test-secret-hash-pepper-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PepperB = "test-secret-hash-pepper-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void The_same_secret_verifies_against_its_own_hash()
    {
        var hasher = new HmacApiSecretHasher(PepperA);
        var hash = hasher.Hash("plaintext-secret-value");

        Assert.True(hasher.Verify("plaintext-secret-value", hash));
    }

    [Fact]
    public void A_different_secret_does_not_verify()
    {
        var hasher = new HmacApiSecretHasher(PepperA);
        var hash = hasher.Hash("plaintext-secret-value");

        Assert.False(hasher.Verify("a-different-secret", hash));
    }

    [Fact]
    public void The_stored_hash_never_equals_the_plaintext_bytes()
    {
        // BIO-INT-003-AC04: the secret must never be stored verbatim, directly or indirectly -
        // the hash output must not simply be the plaintext's own UTF-8 bytes under a different name.
        var hasher = new HmacApiSecretHasher(PepperA);
        const string plaintext = "plaintext-secret-value";
        var hash = hasher.Hash(plaintext);

        Assert.NotEqual(System.Text.Encoding.UTF8.GetBytes(plaintext), hash);
    }

    [Fact]
    public void Two_different_peppers_hash_the_same_secret_differently()
    {
        var hasherA = new HmacApiSecretHasher(PepperA);
        var hasherB = new HmacApiSecretHasher(PepperB);

        var hashA = hasherA.Hash("plaintext-secret-value");
        Assert.False(hasherB.Verify("plaintext-secret-value", hashA));
    }

    // --- Security-review fix (Zainab, ZB-INT003-01, Critical): fail CLOSED, no dummy pepper ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void An_unconfigured_or_too_short_pepper_refuses_to_hash_a_secret(string? configuredPepper)
    {
        var hasher = new HmacApiSecretHasher(configuredPepper);

        Assert.Throws<InvalidOperationException>(() => hasher.Hash("plaintext-secret-value"));
    }

    [Fact]
    public void An_unconfigured_pepper_never_verifies_any_secret_against_any_hash()
    {
        var configured = new HmacApiSecretHasher(PepperA);
        var hash = configured.Hash("plaintext-secret-value");

        var unconfigured = new HmacApiSecretHasher(null);
        Assert.False(unconfigured.Verify("plaintext-secret-value", hash));
    }
}

public sealed class ApiSecretGeneratorTests
{
    [Fact]
    public void Generated_secrets_are_not_derivable_from_each_other_and_are_sufficiently_long()
    {
        var a = ApiSecretGenerator.Generate();
        var b = ApiSecretGenerator.Generate();

        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 32);
    }
}
