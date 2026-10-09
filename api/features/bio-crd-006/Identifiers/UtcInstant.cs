using System.Globalization;

namespace Biolog.Features.CardLifecycle.Identifiers;

/// <summary>
/// RFC 3339 UTC instant rendering (ADR-0001 Decision 2.1): literal <c>Z</c>, exactly three
/// fractional digits, always UTC. Mirrors <c>contracts/js/clock.mjs</c>'s wire format.
/// </summary>
public static class UtcInstant
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static string Now() => Render(DateTime.UtcNow);

    public static string Render(DateTime value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);
}
