namespace Biolog.Features.CardTemplates.Domain;

/// <summary>AC01/AC03: a template's content is governed, not arbitrary - the same "whitelist, not arbitrary tenant input" discipline BIO-RPT-002's own <c>ReportDefinitionValidator</c> established for report definitions.</summary>
public static class CardTemplateSnapshotValidator
{
    public static void Validate(CardTemplateSnapshot snapshot, string tenantId, ISignatureAssetGateway signatureGateway)
    {
        if (snapshot.FormatKind == CardFormatKind.Custom)
        {
            if (snapshot.CustomSizeMm is null || snapshot.CustomSizeMm.LongEdgeMm <= 0 || snapshot.CustomSizeMm.ShortEdgeMm <= 0)
            {
                throw CardTemplateError.CustomSizeRequired();
            }
        }
        else if (snapshot.CustomSizeMm is not null)
        {
            throw CardTemplateError.CustomSizeForbidden();
        }

        if (snapshot.Signature.Required)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Signature.LineageId) || snapshot.Signature.Version is not > 0)
            {
                throw CardTemplateError.SignatureLineageRequired();
            }

            if (!signatureGateway.Exists(tenantId, snapshot.Signature.LineageId, snapshot.Signature.Version.Value))
            {
                throw CardTemplateError.UnknownSignatureAsset(snapshot.Signature.LineageId, snapshot.Signature.Version.Value);
            }
        }
    }
}
