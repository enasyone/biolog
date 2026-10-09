namespace Biolog.Features.OcrEngine.Model;

public enum OcrLanguageCode
{
    English,
    French,
}

/// <summary>
/// One language's glyph-matching capability (BIO-DOC-004-AC03: "English is production baseline;
/// French support is architected as the next language pack"). Both engines
/// (<see cref="TemplateOcrEngine"/>, <see cref="GenericOcrEngine"/>) take a pack rather than
/// calling <see cref="OcrGlyphFont"/> directly, so a French (or any further) pack plugs in without
/// either engine changing - the same "replaceable interface" shape BIO-DOC-003's
/// <c>IBarcodeDecoder</c> established for barcode symbologies.
/// </summary>
public interface IOcrLanguagePack
{
    OcrLanguageCode LanguageCode { get; }

    (char Character, double Confidence) MatchCell(bool[,] cell);
}

/// <summary>The production baseline (BIO-DOC-004-AC03): English, backed by
/// <see cref="OcrGlyphFont"/>.</summary>
public sealed class EnglishLanguagePack : IOcrLanguagePack
{
    public OcrLanguageCode LanguageCode => OcrLanguageCode.English;

    public (char Character, double Confidence) MatchCell(bool[,] cell) => OcrGlyphFont.MatchCell(cell);
}

/// <summary>
/// Registered language packs, pre-populated with English only (BIO-DOC-004-AC03). French is
/// deliberately NOT pre-registered - "architected as the next language pack" means the seam
/// exists (<see cref="Register"/>, <see cref="OcrLanguageCode.French"/> already named), not that a
/// full accented-character glyph set was built for a language this story does not ship. A future
/// story registers a real <c>FrenchLanguagePack</c> here without either OCR engine changing.
/// </summary>
public sealed class OcrLanguagePackRegistry
{
    private readonly Dictionary<OcrLanguageCode, IOcrLanguagePack> _packs = [];

    public void Register(IOcrLanguagePack pack) => _packs[pack.LanguageCode] = pack;

    public IOcrLanguagePack? Get(OcrLanguageCode languageCode) => _packs.GetValueOrDefault(languageCode);

    public static OcrLanguagePackRegistry CreateDefault()
    {
        var registry = new OcrLanguagePackRegistry();
        registry.Register(new EnglishLanguagePack());
        return registry;
    }
}
