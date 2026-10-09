using Biolog.Features.Authentication;
using Xunit;

namespace Biolog.Features.Authentication.Tests;

public sealed class SessionTokenTests
{
    private const string SecretA = "test-session-token-secret-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SecretB = "test-session-token-secret-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void A_freshly_issued_token_validates_and_round_trips_its_fields()
    {
        var codec = new HmacSessionTokenCodec(SecretA);
        var roles = new HashSet<string> { "RegistrarHr", "Gate" };
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        var token = codec.Issue("ten_a", "provider-1", "subject-1", "per_1", roles, expiresAt);
        var validated = codec.TryValidate(token, out var principal);

        Assert.True(validated);
        Assert.Equal("ten_a", principal!.TenantId);
        Assert.Equal("provider-1", principal.ProviderId);
        Assert.Equal("subject-1", principal.SubjectId);
        Assert.Equal("per_1", principal.PersonId);
        Assert.Equal(roles, principal.Roles);
    }

    [Fact]
    public void A_token_signed_with_a_different_secret_does_not_validate()
    {
        var issuer = new HmacSessionTokenCodec(SecretA);
        var verifier = new HmacSessionTokenCodec(SecretB);

        var token = issuer.Issue("ten_a", "provider-1", "subject-1", "per_1", new HashSet<string>(), DateTimeOffset.UtcNow.AddHours(1));
        var validated = verifier.TryValidate(token, out var principal);

        Assert.False(validated);
        Assert.Null(principal);
    }

    [Fact]
    public void A_tampered_payload_does_not_validate()
    {
        var codec = new HmacSessionTokenCodec(SecretA);
        var token = codec.Issue("ten_a", "provider-1", "subject-1", "per_1", new HashSet<string> { "ReadOnly" }, DateTimeOffset.UtcNow.AddHours(1));

        var parts = token.Split('.', 2);
        var payloadBytes = Convert.FromBase64String(parts[0]);
        var tamperedPayload = System.Text.Encoding.UTF8.GetString(payloadBytes).Replace("ReadOnly", "SuperAdmin");
        var tamperedToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tamperedPayload)) + "." + parts[1];

        var validated = codec.TryValidate(tamperedToken, out var principal);

        Assert.False(validated);
        Assert.Null(principal);
    }

    [Fact]
    public void An_expired_token_does_not_validate()
    {
        var codec = new HmacSessionTokenCodec(SecretA);
        var token = codec.Issue("ten_a", "provider-1", "subject-1", "per_1", new HashSet<string>(), DateTimeOffset.UtcNow.AddMinutes(-1));

        var validated = codec.TryValidate(token, out var principal);

        Assert.False(validated);
        Assert.Null(principal);
    }

    // --- TD-PROVENANCE-DUMMYKEY-SWEEP, normalization pass (Kemi's ruling): fail CLOSED, no
    // fallback key of any kind - mirrors BIO-INT-003's own HmacApiAccessTokenCodec tests for the
    // identical shape. The prior "unconfigured instances share the same documented dummy key"
    // behavior (both would still validate each other's tokens) was itself the bug this
    // normalization pass closes, not merely a quirk to preserve.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void An_unconfigured_or_too_short_secret_refuses_to_issue_a_token(string? configuredSecret)
    {
        var codec = new HmacSessionTokenCodec(configuredSecret);

        Assert.Throws<InvalidOperationException>(() =>
            codec.Issue("ten_a", "provider-1", "subject-1", "per_1", new HashSet<string>(), DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public void Two_independently_unconfigured_codecs_can_no_longer_forge_or_accept_each_others_tokens()
    {
        var unconfiguredA = new HmacSessionTokenCodec(null);
        var unconfiguredB = new HmacSessionTokenCodec("");

        Assert.Throws<InvalidOperationException>(() =>
            unconfiguredA.Issue("ten_a", "provider-1", "subject-1", "per_1", new HashSet<string>(), DateTimeOffset.UtcNow.AddHours(1)));

        var configured = new HmacSessionTokenCodec(SecretA);
        var forgedAttempt = configured.Issue("ten_a", "provider-1", "subject-1", "per_1", new HashSet<string>(), DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(unconfiguredB.TryValidate(forgedAttempt, out var principal));
        Assert.Null(principal);
    }

    [Fact]
    public void A_configured_codec_never_validates_anything_once_unconfigured()
    {
        var configured = new HmacSessionTokenCodec(SecretA);
        var token = configured.Issue("ten_a", "provider-1", "subject-1", "per_1", new HashSet<string>(), DateTimeOffset.UtcNow.AddHours(1));

        var unconfigured = new HmacSessionTokenCodec(null);
        Assert.False(unconfigured.TryValidate(token, out var principal));
        Assert.Null(principal);
    }
}
