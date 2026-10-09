namespace Biolog.Features.OcrEngine.Model;

/// <summary>
/// One field a document template declares (BIO-DOC-004-AC01/AC04): where on the page it sits
/// (pixel-precise, since this engine reads at a fixed glyph pitch - no dynamic detection needed
/// when the template already says where to look, the same reasoning BIO-DOC-002's MRZ format
/// descriptors used for their own fixed field positions), what it means, how many characters wide
/// it is, and whether a low-confidence read requires human review (AC04's "critical fields").
/// </summary>
public sealed record OcrTemplateField(
    string FieldName,
    OcrFieldType FieldType,
    int Left,
    int Top,
    int CharacterCount,
    bool Critical);

/// <summary>A named collection of field regions for one known document layout
/// (BIO-DOC-004-AC01).</summary>
public sealed record OcrDocumentTemplate(string TemplateId, IReadOnlyList<OcrTemplateField> Fields);

/// <summary>
/// Registered document templates (BIO-DOC-004-AC01: "template-driven field extraction preferred
/// for known identity documents"). Data-driven, not a hard-coded per-document-type switch - the
/// same "configured" pattern BIO-DOC-002's <c>MrzFormatRegistry</c> and BIO-DOC-003's
/// <c>BarcodeDecoderRegistry</c> established. Ships with no pre-registered templates of its own
/// (unlike MRZ's three ICAO-standard formats, there is no universal "the" identity-document visual
/// layout to bundle by default); a caller registers the templates relevant to the documents it
/// actually processes.
/// </summary>
public sealed class OcrTemplateRegistry
{
    private readonly Dictionary<string, OcrDocumentTemplate> _templates = [];

    public void Register(OcrDocumentTemplate template) => _templates[template.TemplateId] = template;

    public OcrDocumentTemplate? Get(string templateId) => _templates.GetValueOrDefault(templateId);
}
