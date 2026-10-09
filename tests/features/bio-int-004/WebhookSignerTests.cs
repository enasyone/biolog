using Biolog.Features.Webhooks;
using Xunit;

namespace Biolog.Features.Webhooks.Tests;

/// <summary>BIO-INT-004-AC03: "Deliveries are signed and protected against replay."</summary>
public sealed class WebhookSignerTests
{
    private const string Secret = "test-webhook-signing-secret-hex-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Matches_a_known_good_hmac_sha256_golden_vector_cross_checked_via_openssl_dgst_sha256_hmac()
    {
        var body = "{\"event_id\":\"evt_test\"}";
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000);

        var header = WebhookSigner.ComputeSignatureHeader(Secret, body, timestamp);

        Assert.Equal("t=1700000000000,v1=ea9eeb4808e5f511a64e470dccb3d64d23b2b72bdabc2afc7b791fe9d3ff3c47", header);
    }

    [Fact]
    public void The_timestamp_is_bound_into_the_signed_message_swapping_it_invalidates_the_signature()
    {
        var body = "{\"event_id\":\"evt_test\"}";
        var original = WebhookSigner.ComputeSignatureHeader(Secret, body, DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
        var differentTimestamp = WebhookSigner.ComputeSignatureHeader(Secret, body, DateTimeOffset.FromUnixTimeMilliseconds(1700000000001));

        Assert.NotEqual(original, differentTimestamp);
    }

    [Fact]
    public void A_different_body_under_the_same_secret_and_timestamp_produces_a_different_signature()
    {
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000);
        var a = WebhookSigner.ComputeSignatureHeader(Secret, "{\"a\":1}", timestamp);
        var b = WebhookSigner.ComputeSignatureHeader(Secret, "{\"a\":2}", timestamp);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void A_different_secret_under_the_same_body_and_timestamp_produces_a_different_signature()
    {
        var body = "{\"event_id\":\"evt_test\"}";
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000);
        var a = WebhookSigner.ComputeSignatureHeader(Secret, body, timestamp);
        var b = WebhookSigner.ComputeSignatureHeader("a-totally-different-synthetic-secret-value-here", body, timestamp);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void The_header_format_is_t_equals_unix_ms_comma_v1_equals_hex_hmac()
    {
        var header = WebhookSigner.ComputeSignatureHeader(Secret, "{}", DateTimeOffset.FromUnixTimeMilliseconds(42));

        Assert.Matches(@"^t=42,v1=[0-9a-f]{64}$", header);
    }
}
