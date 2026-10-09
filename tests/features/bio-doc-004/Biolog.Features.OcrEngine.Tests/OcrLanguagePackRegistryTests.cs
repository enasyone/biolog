using Biolog.Features.OcrEngine.Model;
using Xunit;

namespace Biolog.Features.OcrEngine.Tests;

/// <summary>Developer tests for BIO-DOC-004-AC03 ("English is production baseline; French support
/// is architected as the next language pack").</summary>
public sealed class OcrLanguagePackRegistryTests
{
    [Fact]
    public void AC03_english_is_registered_by_default()
    {
        var registry = OcrLanguagePackRegistry.CreateDefault();
        var english = registry.Get(OcrLanguageCode.English);

        Assert.NotNull(english);
        Assert.Equal(OcrLanguageCode.English, english!.LanguageCode);
    }

    [Fact]
    public void AC03_french_is_not_shipped_but_the_language_code_and_seam_already_exist()
    {
        var registry = OcrLanguagePackRegistry.CreateDefault();

        Assert.Null(registry.Get(OcrLanguageCode.French));
        Assert.True(Enum.IsDefined(OcrLanguageCode.French));
    }

    [Fact]
    public void AC03_a_future_language_pack_registers_through_the_same_seam_without_engine_changes()
    {
        var registry = new OcrLanguagePackRegistry();
        registry.Register(new StubFrenchPack());

        var french = registry.Get(OcrLanguageCode.French);

        Assert.NotNull(french);
        Assert.Equal(OcrLanguageCode.French, french!.LanguageCode);
    }

    private sealed class StubFrenchPack : IOcrLanguagePack
    {
        public OcrLanguageCode LanguageCode => OcrLanguageCode.French;
        public (char Character, double Confidence) MatchCell(bool[,] cell) => ('?', 0.0);
    }
}
