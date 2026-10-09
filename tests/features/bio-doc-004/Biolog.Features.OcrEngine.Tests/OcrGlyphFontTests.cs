using Biolog.Features.OcrEngine.Model;
using Biolog.Features.OcrEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.OcrEngine.Tests;

public sealed class OcrGlyphFontTests
{
    [Fact]
    public void registers_42_characters_space_digits_letters_and_punctuation()
    {
        // space + 10 digits + 26 letters + 5 punctuation (. , ' - /) = 42.
        Assert.Equal(42, OcrGlyphFont.Glyphs.Count);
    }

    [Fact]
    public void every_glyph_template_is_pairwise_distinct()
    {
        var chars = OcrGlyphFont.Glyphs.Keys.ToList();
        for (var i = 0; i < chars.Count; i++)
        {
            for (var j = i + 1; j < chars.Count; j++)
            {
                var (match, confidence) = OcrGlyphFont.MatchCell(OcrGlyphFont.Glyphs[chars[i]]);
                if (match == chars[j])
                {
                    Assert.True(confidence < 1.0, $"'{chars[i]}' and '{chars[j]}' templates are identical");
                }
            }
        }
    }

    [Theory]
    [InlineData('A')]
    [InlineData('0')]
    [InlineData(' ')]
    [InlineData('.')]
    public void an_exact_template_matches_itself_with_full_confidence(char c)
    {
        var (match, confidence) = OcrGlyphFont.MatchCell(OcrGlyphFont.Glyphs[c]);
        Assert.Equal(c, match);
        Assert.Equal(1.0, confidence);
    }

    [Fact]
    public void a_corrupted_cell_still_returns_a_best_match_with_reduced_confidence()
    {
        var corrupted = GlyphCorruption.LowerConfidenceButKeepCharacter('O', flipCount: 3);

        var (match, confidence) = OcrGlyphFont.MatchCell(corrupted);

        Assert.Equal('O', match);
        Assert.True(confidence < 1.0);
    }

    [Fact]
    public void rejects_a_cell_of_the_wrong_shape()
    {
        Assert.Throws<ArgumentException>(() => OcrGlyphFont.MatchCell(new bool[3, 3]));
    }
}
