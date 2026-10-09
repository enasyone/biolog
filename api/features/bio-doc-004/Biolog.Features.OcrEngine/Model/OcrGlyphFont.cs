namespace Biolog.Features.OcrEngine.Model;

/// <summary>
/// The English language pack's glyph templates (BIO-DOC-004-AC01/AC02/AC03): a native,
/// dependency-free stand-in for a real printed font, the same "genuine nearest-template
/// matching, not a real-world font reproduction" design BIO-DOC-002's <c>MrzGlyphFont</c> used.
///
/// <para>
/// <b>Character set scope.</b> Uppercase <c>A</c>-<c>Z</c>, digits <c>0</c>-<c>9</c>, space and
/// six punctuation marks (<c>. , ' - /</c> and space) - not full mixed-case prose. Identity
/// document visual zones (the printed area alongside a photo, distinct from the MRZ) are
/// overwhelmingly printed in uppercase for the same legibility/OCR-friendliness reason MRZ itself
/// is uppercase-only; this matches that real-world convention rather than inventing a much larger,
/// higher-transcription-risk lowercase font under this story's scope. Full mixed-case prose OCR
/// is a natural extension for a future language pack (see <see cref="OcrLanguagePackRegistry"/>),
/// not a gap silently left unaddressed.
/// </para>
/// </summary>
public static class OcrGlyphFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    public const int CellPitchX = GlyphWidth + 1;
    public const int CellPitchY = GlyphHeight + 1;

    public const char Space = ' ';

    public static readonly IReadOnlyDictionary<char, bool[,]> Glyphs = BuildGlyphs();

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
            [' '] = [".....", ".....", ".....", ".....", ".....", ".....", "....."],
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
            ['.'] = [".....", ".....", ".....", ".....", ".....", "..#..", "....."],
            [','] = [".....", ".....", ".....", ".....", "..#..", "..#..", ".#..."],
            ['\''] = ["..#..", "..#..", ".....", ".....", ".....", ".....", "....."],
            ['-'] = [".....", ".....", ".....", "#####", ".....", ".....", "....."],
            ['/'] = ["....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#...."],
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
