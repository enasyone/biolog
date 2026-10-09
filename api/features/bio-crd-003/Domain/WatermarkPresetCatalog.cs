namespace Biolog.Features.CardTypography.Domain;

/// <summary>
/// BIO-CRD-003-AC05's real content: a starter behaviour for each of STAFF/STUDENT/VISITOR - a
/// diagonal, low-opacity text watermark, the conventional treatment for a badge category marker
/// (readable as a background cue, never obscuring the photo/name fields printed over it). No
/// master-spec section or ADR in this repository names concrete values, so these are this
/// story's own, deliberately named starter values - one place to revise, not a magic number
/// scattered through the feature, the same resolution every sibling story has used for its own
/// unspecified defaults (BIO-CRD-001's DPI floor, BIO-CRD-002's custom-size bounds).
/// VISITOR carries a higher default opacity than STAFF/STUDENT - a visitor badge benefits from a
/// more visible category marker, since a visitor is the case most likely to need a fast visual
/// challenge at a glance. Every preset is a STARTING point (BIO-CRD-003-AC04 lets a designer
/// change opacity/rotation/layer afterwards) - not a locked, unchangeable default.
/// </summary>
public sealed record WatermarkPreset(string Text, double DefaultOpacityPercent, double DefaultRotationDeg);

public static class WatermarkPresetCatalog
{
    public static readonly IReadOnlyDictionary<WatermarkPresetKind, WatermarkPreset> Presets = new Dictionary<WatermarkPresetKind, WatermarkPreset>
    {
        [WatermarkPresetKind.Staff] = new WatermarkPreset("STAFF", 20.0, -30.0),
        [WatermarkPresetKind.Student] = new WatermarkPreset("STUDENT", 20.0, -30.0),
        [WatermarkPresetKind.Visitor] = new WatermarkPreset("VISITOR", 35.0, -30.0),
    };

    public static WatermarkPreset? TryGetPreset(WatermarkPresetKind kind) =>
        Presets.TryGetValue(kind, out var preset) ? preset : null;
}
