namespace Biolog.Features.AccessCache.Domain;

/// <summary>BIO-ACC-003-AC03's two outcomes: a presentation was matched against the locally cached deny-list and allowed, or denied because the presented person_id was found revoked in that cache.</summary>
public enum PresentationOutcome
{
    Allowed,
    Denied,
}

public static class PresentationOutcomeExtensions
{
    public static string ToWireString(this PresentationOutcome outcome) => outcome switch
    {
        PresentationOutcome.Allowed => "ALLOWED",
        PresentationOutcome.Denied => "DENIED",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out PresentationOutcome outcome)
    {
        switch (value)
        {
            case "ALLOWED": outcome = PresentationOutcome.Allowed; return true;
            case "DENIED": outcome = PresentationOutcome.Denied; return true;
            default: outcome = default; return false;
        }
    }
}

/// <summary>
/// A single card-presentation decision the mobile edge made against its own locally cached
/// deny-list (BIO-ACC-003-AC03), uploaded here once connectivity returns. <see cref="OccurredAt"/>
/// is when the device actually made the decision (may be long before <see cref="ReceivedAt"/>
/// on a device that was offline for a while) - the ordering the human Sync Resolution Queue /
/// access-events view should honour is <see cref="OccurredAt"/>, not arrival order.
///
/// <see cref="Stale"/> (BIO-ACC-003-AC04's own "explicit" requirement): true when the device's
/// local revocation cache was older than its own documented staleness threshold at the moment
/// of decision (see <c>mobile/lib/core/access/stale_cache_policy.dart</c>). Staleness NEVER
/// changes <see cref="Outcome"/> itself - a stale cache still correctly denies anything it
/// already knows is revoked (BIO-CRD-006's own doc: "a stale local deny list is exactly what a
/// genuinely revoked card is supposed to still be denied against") - it only flags that the
/// decision was made with a cache that might be missing a MORE RECENT revocation, so a human
/// reviewing this log later knows to treat an ALLOWED+stale decision with more scrutiny than an
/// ALLOWED+fresh one.
/// </summary>
public sealed record PresentationEvent(
    string EventId,
    string TenantId,
    string DeviceId,
    string PersonId,
    PresentationOutcome Outcome,
    string ReasonCode,
    string OccurredAt,
    string ReceivedAt,
    string? CacheSnapshotAt,
    bool Stale);
