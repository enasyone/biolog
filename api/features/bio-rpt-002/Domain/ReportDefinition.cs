namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>
/// A caller's requested report (BIO-RPT-002-AC01). Deliberately carries no <c>TenantId</c> - the
/// tenant a report runs against always comes from <see cref="Tenancy.ICurrentTenantProvider"/>,
/// never from request content (AC03: a caller cannot even ask to see another tenant's data,
/// let alone be refused for asking).
/// </summary>
public sealed record ReportDefinition
{
    public required ReportDataset Dataset { get; init; }
    public required ReportDimension Dimension { get; init; }
    public required ReportMeasure Measure { get; init; }
}

public static class ReportDefinitionValidator
{
    /// <summary>AC01: reject anything outside <see cref="ReportSchemaRegistry"/>'s whitelist, both structurally (an unknown enum value slipping through) and semantically (a real dimension/measure that this particular dataset does not allow).</summary>
    public static void Validate(ReportDefinition definition)
    {
        if (!ReportSchemaRegistry.TryGet(definition.Dataset, out var schema))
        {
            throw ReportError.UnknownDataset(definition.Dataset);
        }

        if (!schema.AllowedDimensions.Contains(definition.Dimension))
        {
            throw ReportError.DimensionNotWhitelisted(definition.Dataset, definition.Dimension);
        }

        if (!schema.AllowedMeasures.Contains(definition.Measure))
        {
            throw ReportError.MeasureNotWhitelisted(definition.Dataset, definition.Measure);
        }
    }
}
