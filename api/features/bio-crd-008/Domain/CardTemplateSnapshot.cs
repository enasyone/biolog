namespace Biolog.Features.CardTemplates.Domain;

public sealed record CustomSizeMm(double LongEdgeMm, double ShortEdgeMm);

/// <summary>
/// AC03: "Template retains card size/orientation, objects/fonts/watermarks/assets/photo spec/
/// barcode/signature rules." The whole content of one template version - immutable once its
/// owning <see cref="CardTemplate"/> reaches <see cref="CardTemplateStatus.Published"/> (AC01).
/// </summary>
public sealed record CardTemplateSnapshot
{
    public required CardFormatKind FormatKind { get; init; }
    public required CardOrientation Orientation { get; init; }

    /// <summary>Required when <see cref="FormatKind"/> is <see cref="CardFormatKind.Custom"/>, forbidden otherwise.</summary>
    public CustomSizeMm? CustomSizeMm { get; init; }

    public IReadOnlyList<CardTemplateElement> FrontElements { get; init; } = [];
    public IReadOnlyList<CardTemplateElement> BackElements { get; init; } = [];

    public WatermarkPresetKind Watermark { get; init; } = WatermarkPresetKind.None;
    public PhotoSpec Photo { get; init; } = PhotoSpec.NotRequired;
    public BarcodeProfileKind Barcode { get; init; } = BarcodeProfileKind.None;
    public SignatureRule Signature { get; init; } = SignatureRule.NotRequired;
}
