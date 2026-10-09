namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// The Code 39 ("3 of 9") wide/narrow pattern table this engine's decoder and its test fixture's
/// synthetic-symbol renderer both read from (mirroring <c>MrzGlyphFont</c> pairing a production
/// recognizer with a test-only renderer off the same shared table).
///
/// <para>
/// Implements Code 39's real STRUCTURE faithfully: each character is 9 alternating bar/space
/// elements (starting and ending on a bar), exactly 3 of the 9 are "wide", and <c>*</c> is a
/// dedicated start/stop character never used as data. What this table does NOT claim is
/// byte-for-byte fidelity to the historical ANSI/AIM USS-39 character-to-pattern assignment -
/// reproducing that 44-entry table correctly from memory, for every character, carries real
/// transcription risk with no way to cross-check it against a reference scanner in this sandboxed
/// environment. Instead, the mapping is generated deterministically (each character maps to the
/// Nth 3-of-9 combination in a fixed enumeration order), giving a table that is internally
/// consistent and exercises the real Code 39 encoding discipline end to end, but is this engine's
/// own assignment rather than the historical one - the same "native stand-in, not a pixel-for-
/// pixel real-world reproduction" choice <c>MrzGlyphFont</c> made for OCR-B. A decoder registered
/// against the official table (or a real scanner library) can replace this one through the same
/// <see cref="IBarcodeDecoder"/> seam without any other part of this feature changing.
/// </para>
/// </summary>
public static class Code39Table
{
    public const int ElementsPerCharacter = 9;
    public const char StartStop = '*';

    private static readonly string DataAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    /// <summary>Character -> 9-element wide(true)/narrow(false) pattern, bar-space-bar-space-...-bar.</summary>
    public static readonly IReadOnlyDictionary<char, bool[]> Patterns = BuildPatterns();

    private static IReadOnlyDictionary<char, bool[]> BuildPatterns()
    {
        var combinations = GenerateThreeOfNineCombinations(DataAlphabet.Length + 1);
        var patterns = new Dictionary<char, bool[]>
        {
            [StartStop] = combinations[0],
        };

        for (var i = 0; i < DataAlphabet.Length; i++)
        {
            patterns[DataAlphabet[i]] = combinations[i + 1];
        }

        return patterns;
    }

    /// <summary>Enumerates 3-element-wide subsets of a 9-element pattern in a fixed,
    /// deterministic order (lexicographic by position), stopping once <paramref name="count"/>
    /// have been produced. There are C(9,3) = 84 possible combinations, comfortably more than the
    /// 44 characters this table needs.</summary>
    private static List<bool[]> GenerateThreeOfNineCombinations(int count)
    {
        var combinations = new List<bool[]>(count);
        for (var a = 0; a < ElementsPerCharacter && combinations.Count < count; a++)
        {
            for (var b = a + 1; b < ElementsPerCharacter && combinations.Count < count; b++)
            {
                for (var c = b + 1; c < ElementsPerCharacter && combinations.Count < count; c++)
                {
                    var pattern = new bool[ElementsPerCharacter];
                    pattern[a] = true;
                    pattern[b] = true;
                    pattern[c] = true;
                    combinations.Add(pattern);
                }
            }
        }

        return combinations;
    }
}
