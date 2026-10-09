using System.Net;
using System.Net.Http;
using Biolog.Features.Webhooks;
using Xunit;

namespace Biolog.Features.Webhooks.Tests;

/// <summary>BIO-INT-004-ZB-01/ZB-04 (Zainab, High x2 - SSRF, round 1 + round 2's complete
/// IANA special-purpose registry).</summary>
public sealed class WebhookDestinationPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.255")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.254")]
    [InlineData("169.254.169.254")] // the well-known cloud-metadata address
    [InlineData("169.254.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    public void Blocks_every_loopback_private_link_local_and_metadata_address(string literal)
    {
        var address = IPAddress.Parse(literal);

        Assert.False(WebhookDestinationPolicy.IsAllowed(address, out var reason));
        Assert.NotEmpty(reason);
    }

    /// <summary>BIO-INT-004-ZB-04 (round 2): the specific gaps named in the review - round 1's
    /// hand-enumerated blocklist missed these real IANA special-purpose ranges entirely.</summary>
    [Theory]
    [InlineData("198.18.0.0")] // benchmarking (RFC 2544) - named explicitly in the review
    [InlineData("198.19.255.255")] // top of the same /15
    [InlineData("192.0.0.1")] // IETF protocol assignments - named explicitly in the review
    [InlineData("192.0.0.255")]
    [InlineData("192.0.2.1")] // documentation, TEST-NET-1
    [InlineData("192.88.99.1")] // 6to4 relay anycast
    [InlineData("198.51.100.1")] // documentation, TEST-NET-2
    [InlineData("203.0.113.1")] // documentation, TEST-NET-3
    [InlineData("240.0.0.1")] // reserved for future use
    [InlineData("255.255.255.255")] // limited broadcast
    [InlineData("::")] // unspecified
    [InlineData("64:ff9b::1")] // NAT64 well-known prefix
    [InlineData("100::1")] // discard-only
    [InlineData("2001::1")] // IETF protocol assignments
    [InlineData("2001:0:1234::1")] // Teredo (within 2001::/23)
    [InlineData("2001:2::1")] // benchmarking
    [InlineData("2001:db8::1")] // documentation
    [InlineData("2002::1")] // 6to4
    public void Blocks_every_previously_missed_special_purpose_range(string literal)
    {
        var address = IPAddress.Parse(literal);

        Assert.False(WebhookDestinationPolicy.IsAllowed(address, out var reason));
        Assert.NotEmpty(reason);
    }

    /// <summary>BIO-INT-004-ZB-06/SY-INT004-02 (round 3): the specific IPv6 ranges the round-3
    /// review named as still missing - including the local-use NAT64 prefix flagged as
    /// security-relevant in its own right, since a deployment routing it can translate an
    /// apparently-allowed IPv6 destination into IPv4 space this policy already blocks.</summary>
    [Theory]
    [InlineData("64:ff9b:1::1")] // NAT64, local-use prefix - the dangerous one, not just incomplete
    [InlineData("64:ff9b:1:ffff:ffff:ffff:ffff:ffff")]
    [InlineData("100:0:0:1::1")] // dummy IPv6 prefix - distinct from the discard-only block
    [InlineData("3fff::1")] // documentation (RFC 9637)
    [InlineData("3fff:fff:ffff:ffff:ffff:ffff:ffff:ffff")] // top of the same /20
    [InlineData("5f00::1")] // segment routing (SRv6) SIDs (RFC 9602)
    [InlineData("5f00:ffff:ffff:ffff:ffff:ffff:ffff:ffff")] // top of the same /16
    public void Blocks_every_round_3_ipv6_gap(string literal)
    {
        var address = IPAddress.Parse(literal);

        Assert.False(WebhookDestinationPolicy.IsAllowed(address, out var reason));
        Assert.NotEmpty(reason);
    }

    /// <summary>BIO-INT-004-ZB-08/SY-INT004-02 (High, round 4): reserved IPv6 space OUTSIDE
    /// the currently assigned global-unicast block (<c>2000::/3</c>) - none of these are named in
    /// <see cref="WebhookDestinationPolicy"/>'s own deny-list table (they are not "special-purpose"
    /// exceptions, just never-yet-assigned), which is exactly why round 3's deny-list-only policy
    /// let every one of these through despite them being reserved and potentially internally
    /// routed. Named directly in the review as examples that passed round 3.</summary>
    [Theory]
    [InlineData("1000::1")] // below 2000::/3 entirely
    [InlineData("4000::1")]
    [InlineData("6000::1")]
    [InlineData("8000::1")]
    [InlineData("a000::1")]
    [InlineData("c000::1")]
    [InlineData("e000::1")]
    public void Blocks_reserved_ipv6_space_outside_the_assigned_global_unicast_block(string literal)
    {
        var address = IPAddress.Parse(literal);

        Assert.False(WebhookDestinationPolicy.IsAllowed(address, out var reason));
        Assert.Contains("2000::/3", reason);
    }

    /// <summary>Boundary proof for the positive <c>2000::/3</c> gate itself, the same "exactly at
    /// the edge, not just comfortably inside/outside" discipline every other boundary test in this
    /// file already applies.</summary>
    [Theory]
    [InlineData("1fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false)] // one address below 2000::/3 - must be blocked
    [InlineData("2000::", true)] // the very first address of 2000::/3 - must be allowed
    [InlineData("3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", true)] // the very last address of 2000::/3 (and outside the narrower 3fff::/20 special-purpose sub-block) - must be allowed
    [InlineData("4000::", false)] // one address above 2000::/3 - must be blocked
    public void Enforces_the_2000_colon_colon_slash_3_boundary_exactly(string literal, bool expectedAllowed)
    {
        var address = IPAddress.Parse(literal);

        Assert.Equal(expectedAllowed, WebhookDestinationPolicy.IsAllowed(address, out _));
    }

    /// <summary>A /15 and a /23 are the two odd (non-byte-aligned) prefix lengths in the table -
    /// confirms the bit-level mask logic in SpecialRange.Contains is correct exactly at the
    /// boundary, not just comfortably inside the range.</summary>
    [Theory]
    [InlineData("198.17.255.255")] // one address below 198.18.0.0/15 - must be allowed
    [InlineData("198.20.0.0")] // one address above 198.18.0.0/15 - must be allowed
    [InlineData("2000:ffff:ffff:ffff:ffff:ffff:ffff:ffff")] // one address below 2001::/23 - must be allowed
    [InlineData("2001:200::1")] // one address above 2001::/23 - must be allowed
    [InlineData("3ffe:ffff:ffff:ffff:ffff:ffff:ffff:ffff")] // one address below 3fff::/20 - must be allowed
    [InlineData("3fff:1000::")] // one address above 3fff::/20 - must be allowed
    public void Allows_addresses_immediately_outside_an_odd_prefix_length_boundary(string literal)
    {
        var address = IPAddress.Parse(literal);

        Assert.True(WebhookDestinationPolicy.IsAllowed(address, out var reason));
        Assert.Empty(reason);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700:4700::1111")]
    public void Allows_ordinary_public_addresses(string literal)
    {
        var address = IPAddress.Parse(literal);

        Assert.True(WebhookDestinationPolicy.IsAllowed(address, out var reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void EnsureAllowed_throws_for_a_blocked_address_and_names_it_in_the_message()
    {
        var ex = Assert.Throws<WebhookDestinationBlockedException>(() => WebhookDestinationPolicy.EnsureAllowed(IPAddress.Parse("169.254.169.254")));

        Assert.Equal(IPAddress.Parse("169.254.169.254"), ex.Address);
        Assert.Contains("169.254.169.254", ex.Message);
    }

    [Fact]
    public void EnsureAllowed_does_not_throw_for_an_allowed_address()
    {
        WebhookDestinationPolicy.EnsureAllowed(IPAddress.Parse("8.8.8.8"));
    }

    /// <summary>
    /// HttpWebhookSender.SendAsync must classify a connect-time destination rejection as
    /// PermanentFailure (never retried - the URL resolves to the same disallowed address every
    /// time) rather than falling through to the generic network-error TransientFailure path.
    /// Uses a handler whose ConnectCallback throws WebhookDestinationBlockedException directly -
    /// exercising the REAL exception-unwrapping logic in HttpWebhookSender without needing a real
    /// DNS lookup or socket connection (which the sandbox may not permit anyway).
    /// </summary>
    [Fact]
    public async Task HttpWebhookSender_classifies_a_connect_time_destination_rejection_as_permanent_never_retried()
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = (_, _) => throw new WebhookDestinationBlockedException(IPAddress.Parse("169.254.169.254"), "link-local/cloud-metadata (169.254.0.0/16)"),
        };
        using var httpClient = new HttpClient(handler);
        var sender = new HttpWebhookSender(httpClient);

        var result = await sender.SendAsync(new Uri("https://example-synthetic.test/hook"), "{}", "t=1,v1=abc", CancellationToken.None);

        Assert.Equal(WebhookDeliveryOutcome.PermanentFailure, result.Outcome);
        Assert.Contains("169.254.169.254", result.Detail);
    }
}
