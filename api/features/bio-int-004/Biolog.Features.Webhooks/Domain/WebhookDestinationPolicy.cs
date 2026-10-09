using System.Net;
using System.Net.Sockets;

namespace Biolog.Features.Webhooks;

/// <summary>Thrown by <see cref="WebhookConnectCallback"/> when a resolved destination address
/// is rejected by <see cref="WebhookDestinationPolicy"/>. Mapped to
/// <see cref="WebhookDeliveryOutcome.PermanentFailure"/> in <see cref="HttpWebhookSender"/> -
/// never retried, since retrying a policy violation against the same subscription URL would
/// never succeed and would just look like a probe to whatever the blocked address belongs to.</summary>
public sealed class WebhookDestinationBlockedException(IPAddress address, string reason)
    : Exception($"destination address {address} is blocked by webhook delivery policy ({reason})")
{
    public IPAddress Address { get; } = address;
}

/// <summary>
/// BIO-INT-004-ZB-01/ZB-04 (Zainab, High - SSRF). A tenant-supplied webhook URL is, by
/// construction, caller-controlled, and the event payload it carries can hold personal/sensitive
/// data (AC02's own <c>payload_classification</c>) - without a destination policy, this story's
/// HTTP client would happily deliver that payload to loopback, a private range, a link-local
/// address (including the well-known cloud-metadata address <c>169.254.169.254</c>), or any
/// other non-public-internet service a subscriber chooses to point the subscription at.
///
/// <b>ZB-INT004-04 round 2: a positive "is this global unicast" policy, not a hand-enumerated
/// blocklist.</b> Round 1's blocklist missed several real IANA special-purpose ranges
/// (<c>198.18.0.0/15</c> benchmarking, <c>192.0.0.0/24</c> IETF protocol assignments,
/// <c>192.0.2.0/24</c>/<c>198.51.100.0/24</c>/<c>203.0.113.0/24</c> documentation/TEST-NET) -
/// exactly the "incomplete blocklist" failure mode enumerating ranges by hand invites. This is
/// now framed and implemented the other way around: <see cref="SpecialPurposeRanges"/> is a
/// checked-in snapshot of the IANA IPv4/IPv6 Special-Purpose Address Registry (RFC 6890 and its
/// updates) rather than a guessed subset, and <see cref="IsAllowed"/> allows an address only if
/// it falls in NONE of them - "global unicast" by construction, not by omission.
///
/// <b>ZB-INT004-06 round 3: this snapshot calling itself "complete" was itself the bug, twice.</b>
/// Round 2's table was still missing <c>64:ff9b:1::/48</c> (local-use NAT64 - Zainab's own
/// specific warning: this prefix can translate an apparently-allowed IPv6 destination into IPv4
/// space the policy already blocks, a live bypass vector, not merely a completeness gap),
/// <c>100:0:0:1::/64</c> (the Dummy IPv6 Prefix - distinct from the Discard-Only block
/// <c>100::/64</c> already present), <c>3fff::/20</c> (documentation, RFC 9637, 2024) and
/// <c>5f00::/16</c> (Segment Routing IPv6 SIDs, RFC 9602, 2024). Per her own explicit point - "the
/// table also calls itself complete and assumes every address outside it is global unicast, which
/// is not a safe invariant as the registry evolves" - this is now disclosed honestly instead:
/// <see cref="SpecialPurposeRangesLastVerifiedAgainstIanaRegistry"/> names the date this snapshot
/// was last checked against the live registry by hand (no live validation infrastructure exists
/// in this codebase to fetch or pin the registry automatically - a named, disclosed follow-up,
/// not a silently-assumed solved problem), so a future reader knows this needs periodic
/// re-verification rather than trusting the table's own presence as proof of completeness. A new
/// IANA allocation is still a one-line table addition - the honesty fix is dropping the claim that
/// no such addition will ever be needed, not changing how an addition is made.
///
/// <b>Enforced at CONNECT time, not URL-parse time</b> (see <see cref="WebhookConnectCallback"/>,
/// wired into <see cref="HttpWebhookSender"/>'s own <c>SocketsHttpHandler.ConnectCallback</c>) -
/// a hostname's DNS resolution can change between subscription creation and delivery (DNS
/// rebinding), and a redirect response's <c>Location</c> header is not known until delivery time
/// at all. Every socket connection <see cref="System.Net.Http.HttpClient"/> ever opens for one
/// logical request - the initial one AND every redirect hop, since each is its own fresh TCP
/// connection through the same callback - is checked here; nothing about this policy is
/// bypassable via a redirect, and a DNS answer that changes between two delivery attempts is
/// re-checked on each one, not trusted from a prior check.
///
/// <b>ZB-INT004-08 round 4: the deny-list alone was never a positive policy for IPv6.</b>
/// Everything outside <see cref="SpecialPurposeRanges"/> was treated as allowed - correct for
/// IPv4, where the named ranges plus <c>240.0.0.0/4</c>'s own catch-all already account for the
/// ENTIRE address space above the allocated unicast range (IPv4 has essentially nothing left
/// over to miss), but wrong for IPv6, where the vast majority of the space is currently
/// UNASSIGNED/reserved for future allocation and can never be fully enumerated as a deny-list -
/// the special-purpose registry only lists exceptions IANA has already carved out, not
/// "everything that isn't currently routable." <c>4000::1</c>, <c>6000::1</c>, <c>8000::1</c>,
/// <c>a000::1</c> and <c>c000::1</c> all passed round 3's policy despite being reserved, unlisted,
/// and potentially internally routed - a live SSRF path a deny-list can never fully close by
/// construction. Fixed with a genuine positive gate: IANA's own IPv6 Address Space registry
/// names <c>2000::/3</c> as the entire currently assigned global-unicast allocation, so
/// <see cref="IsAllowed"/> now requires membership in it for every IPv6 address, checked AFTER
/// the named deny-list above (so a specific special-purpose range inside <c>2000::/3</c> - like
/// documentation or 6to4 - still gets its own precise rejection reason, not a generic one).
///
/// <see cref="IsAllowed"/> is the single predicate both the connect-time check AND
/// <see cref="WebhookSubscriptionService"/>'s own best-effort, NON-authoritative create-time
/// literal-IP pre-check share, so the two can never silently diverge.
/// </summary>
public static class WebhookDestinationPolicy
{
    /// <summary>One CIDR block from the IANA IPv4/IPv6 Special-Purpose Address Registry (or the
    /// multicast ranges, which the registries list as their own address class rather than as
    /// "special-purpose unicast") - anything NOT in this table is global unicast.</summary>
    private readonly record struct SpecialRange(byte[] Network, int PrefixLength, AddressFamily Family, string Label)
    {
        public bool Contains(IPAddress address)
        {
            if (address.AddressFamily != Family)
            {
                return false;
            }

            var candidate = address.GetAddressBytes();
            var fullBytes = PrefixLength / 8;
            for (var i = 0; i < fullBytes; i++)
            {
                if (candidate[i] != Network[i])
                {
                    return false;
                }
            }

            var remainingBits = PrefixLength % 8;
            if (remainingBits == 0)
            {
                return true;
            }

            var mask = (byte)(0xFF << (8 - remainingBits));
            return (candidate[fullBytes] & mask) == (Network[fullBytes] & mask);
        }
    }

    private static SpecialRange V4(string network, int prefixLength, string label) =>
        new(IPAddress.Parse(network).GetAddressBytes(), prefixLength, AddressFamily.InterNetwork, label);

    private static SpecialRange V6(string network, int prefixLength, string label) =>
        new(IPAddress.Parse(network).GetAddressBytes(), prefixLength, AddressFamily.InterNetworkV6, label);

    /// <summary>The date this checked-in snapshot was last hand-verified against the live IANA
    /// Special-Purpose Address Registry (ZB-INT004-06, Zainab round 3) - NOT a live-validated or
    /// automatically-pinned registry check (no such infrastructure exists in this codebase, and
    /// none is silently assumed to - this is a disclosed, named maintenance obligation, not a
    /// solved problem). A future reader relying on this table should re-verify it against
    /// <c>https://www.iana.org/assignments/iana-ipv4-special-registry/</c> and
    /// <c>https://www.iana.org/assignments/iana-ipv6-special-registry/</c> if this date is more
    /// than a year or two old, rather than trusting the table's own existence as proof it is
    /// still complete.</summary>
    public const string SpecialPurposeRangesLastVerifiedAgainstIanaRegistry = "2026-10-07";

    /// <summary>A checked-in snapshot of the IANA IPv4 Special-Purpose Address Registry (RFC 6890
    /// and its updates) plus the IPv4 multicast class, and the IANA IPv6 Special-Purpose Address
    /// Registry plus the IPv6 multicast class - see
    /// <see cref="SpecialPurposeRangesLastVerifiedAgainstIanaRegistry"/> for this snapshot's own
    /// staleness disclosure. IPv4-mapped IPv6 addresses are mapped to IPv4 before this table is
    /// consulted (see <see cref="IsAllowed"/>), so only one family's worth of entries is ever
    /// needed per real address. Narrower IANA sub-allocations already covered by a broader entry
    /// already in this table (for example several single-address and /128 assignments inside
    /// <c>2001::/23</c> - PCP Anycast, TURN Anycast, AMT, AS112-v6, ORCHIDv2, Drone RID) are
    /// deliberately not repeated here as their own rows; they would be redundant, not a gap.</summary>
    private static readonly SpecialRange[] SpecialPurposeRanges =
    [
        V4("0.0.0.0", 8, "\"this host on this network\" (RFC 791)"),
        V4("10.0.0.0", 8, "private-use (RFC 1918)"),
        V4("100.64.0.0", 10, "shared address space / CGNAT (RFC 6598)"),
        V4("127.0.0.0", 8, "loopback (RFC 1122)"),
        V4("169.254.0.0", 16, "link-local, includes the cloud-metadata address 169.254.169.254 (RFC 3927)"),
        V4("172.16.0.0", 12, "private-use (RFC 1918)"),
        V4("192.0.0.0", 24, "IETF protocol assignments (RFC 6890)"),
        V4("192.0.2.0", 24, "documentation, TEST-NET-1 (RFC 5737)"),
        V4("192.88.99.0", 24, "6to4 relay anycast (RFC 3068)"),
        V4("192.168.0.0", 16, "private-use (RFC 1918)"),
        V4("198.18.0.0", 15, "benchmarking (RFC 2544)"),
        V4("198.51.100.0", 24, "documentation, TEST-NET-2 (RFC 5737)"),
        V4("203.0.113.0", 24, "documentation, TEST-NET-3 (RFC 5737)"),
        V4("224.0.0.0", 4, "multicast (RFC 1112)"),
        V4("240.0.0.0", 4, "reserved for future use, includes the limited-broadcast address (RFC 1112/6890)"),

        V6("::1", 128, "loopback (RFC 4291)"),
        V6("::", 128, "unspecified (RFC 4291)"),
        V6("64:ff9b::", 96, "NAT64, well-known prefix (RFC 6052)"),
        V6("64:ff9b:1::", 48, "NAT64, local-use prefix (RFC 8215) - dangerous to omit: a deployment routing this prefix can translate an apparently-allowed IPv6 destination into IPv4 space this policy already blocks"),
        V6("100::", 64, "discard-only address block (RFC 6666)"),
        V6("100:0:0:1::", 64, "dummy IPv6 prefix - distinct from the discard-only block above"),
        V6("2001::", 23, "IETF protocol assignments, includes Teredo 2001::/32 (RFC 6890)"),
        V6("2001:2::", 48, "benchmarking (RFC 5180)"),
        V6("2001:db8::", 32, "documentation (RFC 3849)"),
        V6("2002::", 16, "6to4 (RFC 3056)"),
        V6("3fff::", 20, "documentation (RFC 9637)"),
        V6("5f00::", 16, "segment routing (SRv6) SIDs, not globally reachable (RFC 9602)"),
        V6("fc00::", 7, "unique local (RFC 4193)"),
        V6("fe80::", 10, "link-local (RFC 4291)"),
        V6("ff00::", 8, "multicast (RFC 4291)"),
    ];

    /// <summary>ZB-INT004-08 (Zainab, High, round 4): IANA's own IPv6 Address Space registry
    /// names this as the entire currently assigned global-unicast allocation. Every OTHER IPv6
    /// address - the vast remaining reserved-for-future-use space - is denied by this positive
    /// gate, not merely "not yet named" in <see cref="SpecialPurposeRanges"/>.</summary>
    private static readonly SpecialRange Ipv6GlobalUnicastSpace = V6("2000::", 3, "n/a");

    public static void EnsureAllowed(IPAddress address)
    {
        if (!IsAllowed(address, out var reason))
        {
            throw new WebhookDestinationBlockedException(address, reason);
        }
    }

    public static bool IsAllowed(IPAddress address, out string reason)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        foreach (var range in SpecialPurposeRanges)
        {
            if (range.Contains(address))
            {
                reason = range.Label;
                return false;
            }
        }

        // ZB-INT004-08 (round 4): the deny-list above only ever excludes ranges IANA has
        // specifically named - for IPv6, requiring POSITIVE membership in the currently
        // assigned global-unicast space closes everything else (reserved, unassigned, or
        // future allocations the special-purpose registry has no entry for yet). Checked AFTER
        // the deny-list so a named special-purpose range inside 2000::/3 still gets its own
        // precise reason rather than this generic one.
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && !Ipv6GlobalUnicastSpace.Contains(address))
        {
            reason = "not within the currently assigned IPv6 global unicast space (2000::/3)";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

/// <summary>
/// The actual enforcement point: a <c>SocketsHttpHandler.ConnectCallback</c>. Resolves the
/// target host itself (never trusting the framework's own default connect behaviour, which
/// has no destination policy at all), rejects every resolved address
/// <see cref="WebhookDestinationPolicy"/> disallows BEFORE ever opening a socket to it, and
/// connects only to an allowed address. If DNS returns a mix of allowed and disallowed
/// addresses (a legitimate multi-record answer can do this), the disallowed ones are simply
/// skipped, never attempted - connecting to "whichever one happens to work" is exactly the
/// SSRF shape this policy exists to close.
/// </summary>
public static class WebhookConnectCallback
{
    public static async ValueTask<System.IO.Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);

        WebhookDestinationBlockedException? lastRejection = null;
        foreach (var address in addresses)
        {
            if (!WebhookDestinationPolicy.IsAllowed(address, out var reason))
            {
                lastRejection = new WebhookDestinationBlockedException(address, reason);
                continue;
            }

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw lastRejection ?? new WebhookDestinationBlockedException(IPAddress.None, "DNS resolution returned no addresses");
    }
}
