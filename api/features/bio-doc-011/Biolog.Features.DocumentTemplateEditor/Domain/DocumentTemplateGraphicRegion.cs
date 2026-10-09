namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>
/// AC01's "graphic region": a named region with no OCR field semantics (e.g. a security
/// hologram area, a signature box) - purely descriptive geometry, unlike a field region (which
/// IS a <c>Biolog.Features.OcrEngine.Model.OcrTemplateField</c> directly, this project's one
/// declared cross-feature type reuse). Pixel-rect geometry mirrors BIO-CRD-008's own
/// <c>CardTemplateElement</c> shape (<c>XPx</c>/<c>YPx</c>/<c>WidthPx</c>/<c>HeightPx</c>),
/// renamed to plain ints here since this editor has no sub-pixel/rotation concept.
/// </summary>
public sealed record DocumentTemplateGraphicRegion(string Name, int Left, int Top, int Width, int Height);
