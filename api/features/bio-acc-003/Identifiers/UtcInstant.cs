using System.Globalization;

namespace Biolog.Features.AccessCache.Identifiers;

/// <summary>RFC 3339 UTC instant rendering (ADR-0001 Decision 2.1): literal <c>Z</c>, exactly three fractional digits, always UTC - fixed-width so ordinal string comparison sorts correctly, which this feature's revocation-delta cursor and presentation-event ordering both rely on.</summary>
public static class UtcInstant
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static string Now() => Render(DateTime.UtcNow);

    public static string Render(DateTime value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);
}
