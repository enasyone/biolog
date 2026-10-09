using System.Text.RegularExpressions;

namespace Biolog.Features.OperationalDashboard.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the one prefix this feature
/// touches: <c>evt_</c>, for correlation ids only (this feature mints no durable aggregate of
/// its own, so it has no new id prefix to add).
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
