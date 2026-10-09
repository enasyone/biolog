using Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.OcrEngine.Tests.Fixtures;

/// <summary>
/// Builds a deliberately noisy - but still nearest-match-correct - glyph cell bitmap for AC04/AC05
/// low-confidence tests, mirroring BIO-DOC-002's identically-named fixture. Greedily flips pixels,
/// keeping only flips that leave <see cref="OcrGlyphFont.MatchCell"/>'s nearest match unchanged -
/// self-verifying against the real production matcher rather than an assumption about it.
/// </summary>
public static class GlyphCorruption
{
    public static bool[,] LowerConfidenceButKeepCharacter(char character, int flipCount)
    {
        var bitmap = (bool[,])OcrGlyphFont.Glyphs[character].Clone();
        var flipped = 0;

        for (var y = 0; y < OcrGlyphFont.GlyphHeight && flipped < flipCount; y++)
        {
            for (var x = 0; x < OcrGlyphFont.GlyphWidth && flipped < flipCount; x++)
            {
                bitmap[y, x] = !bitmap[y, x];
                var (match, _) = OcrGlyphFont.MatchCell(bitmap);
                if (match == character)
                {
                    flipped++;
                }
                else
                {
                    bitmap[y, x] = !bitmap[y, x];
                }
            }
        }

        if (flipped < flipCount)
        {
            throw new InvalidOperationException(
                $"could only safely flip {flipped}/{flipCount} pixels for '{character}' without changing its nearest match");
        }

        return bitmap;
    }
}
