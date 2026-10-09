using System.Text.RegularExpressions;

namespace Biolog.Features.MobileVerification.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1) - this feature's own local,
/// schema-conformant mirror, not the shared contract; no generated .NET binding exists yet,
/// the same disclosed limitation every sibling feature project's own copy carries.
///
/// <b>No new prefix is minted here.</b> Minting a brand-new canonical-id prefix is a shared-
/// contract change owned by Kemi - <c>mobile/lib/core/identifiers/canonical_id.dart</c>'s own
/// header is explicit: "Do not add or rename prefixes here." A verification-request id therefore
/// reuses the existing, already-registered <c>evt_</c> prefix - the identical choice BIO-ACC-003's
/// own <c>PresentationEvent.EventId</c> and BIO-MOB-005's own enrolment-package/duplicate-
/// candidate ids already made. <see cref="IsVerificationRequestId"/> is named for readability at
/// call sites; it is exactly <see cref="IsEventId"/>.
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";

    [GeneratedRegex(@"^dev_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex DeviceIdPattern();

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^per_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex PersonIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    public static bool IsDeviceId(string? value) => value is not null && DeviceIdPattern().IsMatch(value);

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsPersonId(string? value) => value is not null && PersonIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    public static bool IsVerificationRequestId(string? value) => IsEventId(value);

    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";

    /// <summary>Mint a fresh verification-request id - mobile mints this locally itself (the request's own idempotency key), the same convention BIO-ACC-003's presentation events and BIO-MOB-005's enrolment packages already use.</summary>
    public static string NewVerificationRequestId() => NewEventId();
}
