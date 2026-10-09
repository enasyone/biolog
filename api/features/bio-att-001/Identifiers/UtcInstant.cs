using System.Globalization;

namespace Biolog.Features.Attendance.Identifiers;

/// <summary>RFC 3339 UTC instant rendering (ADR-0001 Decision 2.1): literal <c>Z</c>, exactly three fractional digits, always UTC.</summary>
public static class UtcInstant
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static string Now() => Render(DateTime.UtcNow);

    public static string Render(DateTime value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);

    /// <summary>True for a value this type can parse back with <see cref="Parse"/> - used to validate caller-supplied <c>occurred_at</c> values (AC01).</summary>
    public static bool TryParse(string? value, out DateTime result) =>
        DateTime.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out result);
}
