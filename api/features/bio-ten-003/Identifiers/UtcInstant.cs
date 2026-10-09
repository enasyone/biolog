using System.Globalization;

namespace Biolog.Features.TenantFeatureConfig.Identifiers;

/// <summary>
/// RFC 3339 UTC instant rendering (ADR-0001 Decision 2.1): literal <c>Z</c>, exactly three
/// fractional digits, always UTC. Copied from <c>api/features/bio-ten-001/Identifiers/UtcInstant.cs</c>
/// per this codebase's "each feature keeps its own copy" convention.
/// </summary>
public static class UtcInstant
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static string Now() => Render(DateTime.UtcNow);

    public static string Render(DateTime value) =>
        value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);
}
