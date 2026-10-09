using System.Globalization;

namespace Biolog.Features.MobileVerification.Identifiers;

/// <summary>RFC 3339 UTC instant rendering (ADR-0001 Decision 2.1): literal <c>Z</c>, exactly three fractional digits, always UTC - fixed-width so ordinal string comparison sorts correctly.</summary>
public static class UtcInstant
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static string Now() => Render(DateTime.UtcNow);

    public static string Render(DateTime value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

    public static bool TryParse(string? value, out string normalized)
    {
        if (DateTime.TryParse(
            value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            normalized = Render(parsed);
            return true;
        }

        normalized = "";
        return false;
    }
}
