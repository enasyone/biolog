using Biolog.Features.CardTemplates.Domain;
using Biolog.Features.CardTemplates.Tenancy;

namespace Biolog.Features.CardTemplates.Tests;

public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>A deterministic double, not a real BIO-CRD-004 signature store - lets tests assert both the "exists" and "does not exist" branches of AC03's signature-rule validation without any cross-project reference.</summary>
public sealed class FakeSignatureAssetGateway : ISignatureAssetGateway
{
    private readonly HashSet<(string TenantId, string LineageId, int Version)> _known = [];

    public FakeSignatureAssetGateway WithAsset(string tenantId, string lineageId, int version)
    {
        _known.Add((tenantId, lineageId, version));
        return this;
    }

    public bool Exists(string tenantId, string lineageId, int version) => _known.Contains((tenantId, lineageId, version));
}

public static class TestData
{
    public const string TenantA = "ten_018f2e5a-1111-7abc-89ab-0123456789ab";
    public const string TenantB = "ten_018f2e5a-2222-7abc-89ab-0123456789ab";

    public static readonly Biolog.Features.CardTemplates.Audit.AuditActor Actor =
        new(Biolog.Features.CardTemplates.Audit.ActorType.User, "usr_test-operator");

    public static CardTemplateSnapshot MinimalSnapshot() => new()
    {
        FormatKind = CardFormatKind.Cr80,
        Orientation = CardOrientation.Portrait,
        FrontElements = [new CardTemplateElement
        {
            Id = "el-1",
            Kind = CardTemplateElementKind.Text,
            Label = "Name field",
            XPx = 10,
            YPx = 10,
            WidthPx = 200,
            HeightPx = 40,
            ZIndex = 0,
            FontFamily = "Inter",
            FontSizePx = 14,
            Text = "{{full_name}}",
        }],
        Watermark = WatermarkPresetKind.Staff,
        Barcode = BarcodeProfileKind.Qr,
    };

    public static CardTemplateSnapshot CustomFormatSnapshot() => MinimalSnapshot() with
    {
        FormatKind = CardFormatKind.Custom,
        CustomSizeMm = new CustomSizeMm(85.6, 54.0),
    };

    public static CardTemplateSnapshot WithRequiredSignature(string lineageId, int version) => MinimalSnapshot() with
    {
        Signature = new SignatureRule { Required = true, LineageId = lineageId, Version = version },
    };
}
