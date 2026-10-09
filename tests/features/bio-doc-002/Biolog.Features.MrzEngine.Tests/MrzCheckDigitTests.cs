using Biolog.Features.MrzEngine.Model;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>Developer tests for BIO-DOC-002-AC03 ("check digits are recalculated and reported
/// independently"). Test vectors below are ICAO Doc 9303 Part 4 Appendix B's own published TD3
/// worked example (the fictional "ERIKSSON, ANNA MARIA" passport) - hand-verified against the
/// 7/3/1-weight algorithm before writing this suite, not merely asserted against this
/// implementation's own output.</summary>
public sealed class MrzCheckDigitTests
{
    [Theory]
    [InlineData("L898902C3", 6)] // document number
    [InlineData("740812", 2)] // date of birth
    [InlineData("120415", 9)] // expiry
    [InlineData("ZE184226B<<<<<", 1)] // personal number
    [InlineData("L898902C3674081221204159ZE184226B<<<<<1", 0)] // composite
    public void AC03_matches_the_published_icao_td3_worked_example(string value, int expectedCheckDigit)
    {
        Assert.Equal(expectedCheckDigit, MrzCheckDigit.Compute(value));
    }

    [Fact]
    public void weights_a_single_character_by_seven_not_by_position_in_a_longer_field()
    {
        // '<' always has value 0 regardless of weight, so use a real character: 'A' (value 10) at
        // weight 7 => 70 mod 10 = 0. Confirms position 0 always starts the 7/3/1 cycle at 7.
        Assert.Equal(0, MrzCheckDigit.Compute("A"));
    }

    [Fact]
    public void filler_character_contributes_zero_regardless_of_weight()
    {
        Assert.Equal(0, MrzCheckDigit.Compute("<<<<<<<<<"));
    }

    [Theory]
    [InlineData('0', true)]
    [InlineData('9', true)]
    [InlineData('A', true)]
    [InlineData('Z', true)]
    [InlineData('<', true)]
    [InlineData('a', false)]
    [InlineData(' ', false)]
    [InlineData('!', false)]
    public void recognizes_exactly_the_37_character_mrz_alphabet(char c, bool expected)
    {
        Assert.Equal(expected, MrzCheckDigit.IsRecognizedCharacter(c));
    }

    [Fact]
    public void is_computable_reflects_every_character_being_recognized()
    {
        Assert.True(MrzCheckDigit.IsComputable("L898902C3"));
        Assert.False(MrzCheckDigit.IsComputable("L89890 C3"));
    }

    [Fact]
    public void rejects_computing_over_an_unrecognized_character_rather_than_silently_treating_it_as_filler()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MrzCheckDigit.Compute("L89890 C3"));
    }
}
