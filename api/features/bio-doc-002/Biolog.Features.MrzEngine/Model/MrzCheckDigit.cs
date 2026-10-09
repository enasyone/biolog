namespace Biolog.Features.MrzEngine.Model;

/// <summary>
/// ICAO Doc 9303 Part 3 §4.9 check-digit algorithm (BIO-DOC-002-AC03: "check digits are
/// recalculated and reported independently"). Not documented anywhere else in this repo (checked:
/// no local restatement of the ICAO field layout or algorithm exists) - this is a direct,
/// from-scratch implementation of the public ICAO standard, verified by hand against ICAO's own
/// published TD3 worked example in <c>MrzTd3ParserTests</c> (Erikssen/Anna Maria passport).
///
/// <para>
/// This type only COMPUTES a check digit from a value string; it never compares against or
/// "corrects" a read one. Comparison lives in the parser, which always reports both the read
/// digit and this computed one side by side (AC03's "independently" - a caller can see they
/// disagree, this type does not resolve the disagreement).
/// </para>
/// </summary>
public static class MrzCheckDigit
{
    private static readonly int[] Weights = [7, 3, 1];

    /// <summary>
    /// Computes the ICAO 9303 check digit for <paramref name="value"/>: each character's numeric
    /// value (digit = itself, letter = 10 + its 0-based position in the alphabet, <c>&lt;</c> = 0)
    /// is multiplied by a repeating 7/3/1 weight cycle (position 0 always weight 7) and the sum is
    /// taken mod 10.
    /// </summary>
    public static int Compute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var sum = 0;
        for (var i = 0; i < value.Length; i++)
        {
            sum += CharacterValue(value[i]) * Weights[i % 3];
        }

        return sum % 10;
    }

    /// <summary>True if <paramref name="value"/> contains only characters this algorithm can
    /// assign a value to (A-Z, 0-9, &lt;). A recognized-but-nonsensical character here is an
    /// AC05 "requires review" signal, not something this method silently tolerates by treating
    /// it as filler.</summary>
    public static bool IsComputable(string value) => value.All(IsRecognizedCharacter);

    public static bool IsRecognizedCharacter(char c) => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '<';

    private static int CharacterValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'Z' => 10 + (c - 'A'),
        '<' => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "not a recognized MRZ check-digit character (A-Z, 0-9, '<')"),
    };
}
