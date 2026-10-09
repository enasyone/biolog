using System.Text.RegularExpressions;

namespace Biolog.Features.PhotoQuality.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the three types this feature
/// touches: <c>per_</c> (the person a capture attaches to), <c>ten_</c> (defense-in-depth shape
/// validation for the resolved tenant) and <c>evt_</c> (audit/correlation events, minted here).
/// This is a local, schema-conformant helper - not the shared contract; no generated .NET
/// binding exists yet. This project registers no new identifier type of its own -
/// <see cref="Domain.PhotoQualityCapture"/> deliberately has no canonical id of its own kind
/// (its natural key is <c>(PersonId, SequenceNumber)</c>, the same choice <c>PhotoMaster</c>
/// (BIO-PHO-001) and <c>BoothCapture</c> (BIO-PHO-002) made).
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";

    [GeneratedRegex(@"^per_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex PersonIdPattern();

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    public static bool IsPersonId(string? value) => value is not null && PersonIdPattern().IsMatch(value);

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    /// <summary>Mint a fresh <c>evt_</c> identifier for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
