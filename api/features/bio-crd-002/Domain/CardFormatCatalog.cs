namespace Biolog.Features.CardDesigner.Domain;

/// <summary>
/// BIO-CRD-002-AC01: "standard sizes use correct physical dimensions." No master-spec section
/// or ADR in this repository defines CR80/CR79/CR100's physical dimensions at the time of
/// writing (the "Card Designer v0.3" specification named in `docs/BRAND-ASSETS.md` is not a
/// file present anywhere in this repository), so <see cref="Standard"/> uses the real,
/// widely-published industry-standard blank sizes these names refer to across the card-printer
/// industry (Fargo/HID, Zebra, Evolis and others all publish the identical figures for these
/// three names) - not invented values, and not something this story has standing to redefine:
///
/// - **CR80** (ISO/IEC 7810 ID-1, the standard credit-card/ID-card size): 85.60mm x 53.98mm.
/// - **CR79**: 83.90mm x 51.90mm - fractionally smaller than CR80, used by some photo-ID
///   printers/laminators that require a slightly undersized blank.
/// - **CR100**: 98.50mm x 67.00mm - a larger visitor/access-badge format.
///
/// Each is stored as <see cref="CardPhysicalSize.LongEdgeMm"/>/<see cref="CardPhysicalSize.ShortEdgeMm"/>
/// (orientation-independent) - BIO-CRD-002-AC02's landscape/portrait switch is which edge is
/// horizontal, never a different physical size.
/// </summary>
public sealed record CardPhysicalSize(double LongEdgeMm, double ShortEdgeMm);

public static class CardFormatCatalog
{
    public static readonly IReadOnlyDictionary<CardFormatKind, CardPhysicalSize> Standard = new Dictionary<CardFormatKind, CardPhysicalSize>
    {
        [CardFormatKind.Cr80] = new CardPhysicalSize(85.60, 53.98),
        [CardFormatKind.Cr79] = new CardPhysicalSize(83.90, 51.90),
        [CardFormatKind.Cr100] = new CardPhysicalSize(98.50, 67.00),
    };

    public static CardPhysicalSize? TryGetStandardSize(CardFormatKind kind) =>
        Standard.TryGetValue(kind, out var size) ? size : null;
}
