using Biolog.Features.BarcodeEngine.Model;
using Xunit;

namespace Biolog.Features.BarcodeEngine.Tests;

/// <summary>Developer tests for the generated Code 39 pattern table's own internal invariants -
/// the self-check this table's design doc relies on in place of cross-referencing a historical
/// table from memory (see <see cref="Code39Table"/>'s remarks).</summary>
public sealed class Code39TableTests
{
    [Fact]
    public void registers_44_characters_ten_digits_26_letters_seven_symbols_and_start_stop()
    {
        // 10 digits + 26 letters + 7 symbols (- . space $ / + %) = 43 data characters, plus '*'.
        Assert.Equal(44, Code39Table.Patterns.Count);
        Assert.True(Code39Table.Patterns.ContainsKey(Code39Table.StartStop));
    }

    [Fact]
    public void every_pattern_has_exactly_nine_elements_and_exactly_three_wide()
    {
        foreach (var (character, pattern) in Code39Table.Patterns)
        {
            Assert.Equal(9, pattern.Length);
            Assert.Equal(3, pattern.Count(w => w));
        }
    }

    [Fact]
    public void every_pattern_is_pairwise_distinct()
    {
        var patterns = Code39Table.Patterns.ToList();
        for (var i = 0; i < patterns.Count; i++)
        {
            for (var j = i + 1; j < patterns.Count; j++)
            {
                Assert.False(
                    patterns[i].Value.SequenceEqual(patterns[j].Value),
                    $"'{patterns[i].Key}' and '{patterns[j].Key}' share an identical pattern");
            }
        }
    }
}
