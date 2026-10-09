namespace Biolog.Features.MrzEngine.Model;

/// <summary>
/// The fixed-pitch glyph templates this engine recognizes an MRZ (Machine Readable Zone) cell
/// against (BIO-DOC-002-AC01/AC04): a native, dependency-free stand-in for the real OCR-B font
/// MRZ documents are printed in, since no image-decoding or OCR library exists anywhere in this
/// repo (see docs/features/bio-doc-002/MRZ-ENGINE.md). Real OCR-B glyph shapes are irrelevant
/// here - what matters is that recognition is a genuine nearest-template match against a known
/// alphabet, not a pass-through of pre-labelled input. The 37-character MRZ alphabet (ICAO Doc
/// 9303 Part 3 §3: <c>A</c>-<c>Z</c>, <c>0</c>-<c>9</c>, filler <c>&lt;</c>) is represented as
/// 5-wide x 7-tall dot-matrix bitmaps, the same table both this engine's recognizer and the test
/// suite's synthetic-image fixture builder read from (mirroring
/// <c>tests/dotnet/Biolog.UnitTests/Features/BrandAssets/SyntheticImages.cs</c> pairing with
/// <c>RasterImageInspector.cs</c>, except here the "font" is shared production data both sides
/// need, not test-only).
/// </summary>
public static class MrzGlyphFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    /// <summary>Horizontal pixels from one glyph cell's left edge to the next (1px gap).</summary>
    public const int CellPitchX = GlyphWidth + 1;

    /// <summary>Vertical pixels from one text line's top edge to the next (1px gap).</summary>
    public const int CellPitchY = GlyphHeight + 1;

    /// <summary>The 37-character MRZ alphabet, in a fixed, stable order (used only for iteration
    /// in tests/diagnostics - matching is against the full <see cref="Glyphs"/> table).</summary>
    public static readonly IReadOnlyList<char> Alphabet = "<0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    public static readonly IReadOnlyDictionary<char, bool[,]> Glyphs = BuildGlyphs();

    /// <summary>
    /// Matches a <paramref name="cell"/> (must be exactly <see cref="GlyphHeight"/> x
    /// <see cref="GlyphWidth"/>) against every known template and returns the nearest one by
    /// Hamming distance, plus a confidence in [0, 1] (1 = exact match, 0 = every pixel differs).
    /// Always returns the single best match - it never refuses to guess a character; a caller
    /// (the parser/validator) decides what to do with a low-confidence result (AC05), this method
    /// only reports how confident the match was.
    /// </summary>
    public static (char Character, double Confidence) MatchCell(bool[,] cell)
    {
        if (cell.GetLength(0) != GlyphHeight || cell.GetLength(1) != GlyphWidth)
        {
            throw new ArgumentException(
                $"cell shape ({cell.GetLength(1)}x{cell.GetLength(0)}) must be ({GlyphWidth}x{GlyphHeight})",
                nameof(cell));
        }

        var bestChar = '\0';
        var bestDistance = int.MaxValue;

        foreach (var (character, template) in Glyphs)
        {
            var distance = HammingDistance(cell, template);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestChar = character;
            }
        }

        const int totalBits = GlyphWidth * GlyphHeight;
        var confidence = 1.0 - (double)bestDistance / totalBits;
        return (bestChar, confidence);
    }

    private static int HammingDistance(bool[,] a, bool[,] b)
    {
        var distance = 0;
        for (var y = 0; y < GlyphHeight; y++)
        {
            for (var x = 0; x < GlyphWidth; x++)
            {
                if (a[y, x] != b[y, x])
                {
                    distance++;
                }
            }
        }

        return distance;
    }

    private static IReadOnlyDictionary<char, bool[,]> BuildGlyphs()
    {
        var rows = new Dictionary<char, string[]>
        {
            ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
            ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
            ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
            ['3'] = [".###.", "#...#", "....#", "..##.", "....#", "#...#", ".###."],
            ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
            ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
            ['6'] = ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
            ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
            ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
            ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
            ['A'] = ["..#..", ".#.#.", "#...#", "#...#", "#####", "#...#", "#...#"],
            ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
            ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
            ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
            ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
            ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
            ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
            ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
            ['I'] = [".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
            ['J'] = ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
            ['K'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
            ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
            ['M'] = ["#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#", "#...#"],
            ['N'] = ["#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#"],
            ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
            ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
            ['Q'] = [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
            ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
            ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
            ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
            ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
            ['V'] = ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
            ['W'] = ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#."],
            ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
            ['Y'] = ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
            ['Z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
            ['<'] = ["...##", "..##.", ".##..", "#....", ".##..", "..##.", "...##"],
        };

        var glyphs = new Dictionary<char, bool[,]>();
        foreach (var (character, pattern) in rows)
        {
            var bitmap = new bool[GlyphHeight, GlyphWidth];
            for (var y = 0; y < GlyphHeight; y++)
            {
                for (var x = 0; x < GlyphWidth; x++)
                {
                    bitmap[y, x] = pattern[y][x] == '#';
                }
            }

            glyphs[character] = bitmap;
        }

        return glyphs;
    }
}
