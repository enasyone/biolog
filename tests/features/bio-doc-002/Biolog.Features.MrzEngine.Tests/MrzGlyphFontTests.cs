using Biolog.Features.MrzEngine.Model;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>Developer tests for the glyph template table BIO-DOC-002-AC01/AC04's recognition step
/// is built on.</summary>
public sealed class MrzGlyphFontTests
{
    [Fact]
    public void registers_exactly_the_37_character_mrz_alphabet()
    {
        Assert.Equal(37, MrzGlyphFont.Glyphs.Count);
        foreach (var c in MrzGlyphFont.Alphabet)
        {
            Assert.True(MrzGlyphFont.Glyphs.ContainsKey(c), $"missing template for '{c}'");
        }
    }

    [Fact]
    public void every_glyph_template_is_pairwise_distinct()
    {
        // A hand-authored 37-glyph bitmap table is exactly the kind of place a copy/paste
        // duplicate slips in unnoticed; this is the regression net for that, not just a sanity
        // check of this test suite's own fixtures.
        var chars = MrzGlyphFont.Glyphs.Keys.ToList();
        for (var i = 0; i < chars.Count; i++)
        {
            for (var j = i + 1; j < chars.Count; j++)
            {
                var (match, confidence) = MrzGlyphFont.MatchCell(MrzGlyphFont.Glyphs[chars[i]]);
                if (match == chars[j])
                {
                    Assert.True(confidence < 1.0, $"'{chars[i]}' and '{chars[j]}' templates are identical");
                }
            }
        }
    }

    [Theory]
    [InlineData('0')]
    [InlineData('O')]
    [InlineData('<')]
    [InlineData('Z')]
    public void an_exact_template_matches_itself_with_full_confidence(char c)
    {
        var (match, confidence) = MrzGlyphFont.MatchCell(MrzGlyphFont.Glyphs[c]);
        Assert.Equal(c, match);
        Assert.Equal(1.0, confidence);
    }

    [Fact]
    public void a_corrupted_cell_still_returns_a_best_match_with_reduced_confidence()
    {
        var corrupted = (bool[,])MrzGlyphFont.Glyphs['0'].Clone();
        corrupted[0, 0] = !corrupted[0, 0];
        corrupted[1, 1] = !corrupted[1, 1];

        var (match, confidence) = MrzGlyphFont.MatchCell(corrupted);

        Assert.Equal('0', match); // still the nearest template
        Assert.True(confidence < 1.0);
    }

    [Fact]
    public void rejects_a_cell_of_the_wrong_shape()
    {
        var wrongShape = new bool[3, 3];
        Assert.Throws<ArgumentException>(() => MrzGlyphFont.MatchCell(wrongShape));
    }
}
