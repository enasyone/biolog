using Biolog.Features.MrzEngine.Model;

namespace Biolog.Features.MrzEngine.Tests.Fixtures;

/// <summary>
/// Builds a deliberately noisy - but still nearest-match-correct - glyph cell bitmap for AC05
/// "low confidence" tests. Rather than hand-picking pixels to flip and hoping they do not also
/// change which character <see cref="MrzGlyphFont.MatchCell"/> resolves to (37 hand-authored
/// templates make that unverifiable by inspection), this greedily flips pixels and calls the real
/// production matcher after each one, keeping only flips that leave the nearest match unchanged -
/// self-verifying by construction against the exact code under test, not an assumption about it.
/// </summary>
public static class GlyphCorruption
{
    public static bool[,] LowerConfidenceButKeepCharacter(char character, int flipCount)
    {
        var bitmap = (bool[,])MrzGlyphFont.Glyphs[character].Clone();
        var flipped = 0;

        for (var y = 0; y < MrzGlyphFont.GlyphHeight && flipped < flipCount; y++)
        {
            for (var x = 0; x < MrzGlyphFont.GlyphWidth && flipped < flipCount; x++)
            {
                bitmap[y, x] = !bitmap[y, x];
                var (match, _) = MrzGlyphFont.MatchCell(bitmap);
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
