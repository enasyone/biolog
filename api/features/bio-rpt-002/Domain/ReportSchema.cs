namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>
/// A governed report's data source (BIO-RPT-002-AC01). Deliberately small for this foundation
/// story: exactly the two declared dependencies (BIO-CRD-007 card production, BIO-ATT-001
/// attendance events) - no arbitrary tenant table/column is ever reachable, only these two
/// gateway-mediated, whitelisted views. A later story adds more datasets by extending this enum
/// plus <see cref="ReportSchemaRegistry"/>, never by accepting a caller-supplied table/column
/// name (that is the exact "arbitrary tenant SQL" this story's user story exists to rule out).
/// </summary>
public enum ReportDataset
{
    CardProduction,
    Attendance,
}

public static class ReportDatasetExtensions
{
    public static string ToWireString(this ReportDataset dataset) => dataset switch
    {
        ReportDataset.CardProduction => "card_production",
        ReportDataset.Attendance => "attendance",
        _ => throw new ArgumentOutOfRangeException(nameof(dataset), dataset, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out ReportDataset dataset)
    {
        switch (value)
        {
            case "card_production": dataset = ReportDataset.CardProduction; return true;
            case "attendance": dataset = ReportDataset.Attendance; return true;
            default: dataset = default; return false;
        }
    }
}

/// <summary>
/// A groupable field (BIO-RPT-002-AC01). One flat enum shared across datasets, not a per-dataset
/// type, so <see cref="ReportSchemaRegistry"/> can express "these three of the five known
/// dimensions are valid for CardProduction" as a plain whitelist check rather than a type-level
/// one - the same tradeoff <see cref="ReportDataset"/>'s own header explains: small and explicit
/// now, extended deliberately later, never inferred from caller input.
/// </summary>
public enum ReportDimension
{
    Status,
    Source,
    CardClass,
    Mode,
    VerificationMethod,
}

public static class ReportDimensionExtensions
{
    public static string ToWireString(this ReportDimension dimension) => dimension switch
    {
        ReportDimension.Status => "status",
        ReportDimension.Source => "source",
        ReportDimension.CardClass => "card_class",
        ReportDimension.Mode => "mode",
        ReportDimension.VerificationMethod => "verification_method",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out ReportDimension dimension)
    {
        switch (value)
        {
            case "status": dimension = ReportDimension.Status; return true;
            case "source": dimension = ReportDimension.Source; return true;
            case "card_class": dimension = ReportDimension.CardClass; return true;
            case "mode": dimension = ReportDimension.Mode; return true;
            case "verification_method": dimension = ReportDimension.VerificationMethod; return true;
            default: dimension = default; return false;
        }
    }
}

/// <summary>An aggregation (BIO-RPT-002-AC01). One measure for this foundation story - a row count per group; a later story adds sum/average measures over a numeric field without changing this shape's own contract.</summary>
public enum ReportMeasure
{
    Count,
}

public static class ReportMeasureExtensions
{
    public static string ToWireString(this ReportMeasure measure) => measure switch
    {
        ReportMeasure.Count => "count",
        _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out ReportMeasure measure)
    {
        switch (value)
        {
            case "count": measure = ReportMeasure.Count; return true;
            default: measure = default; return false;
        }
    }
}

public sealed record ReportDatasetSchema
{
    public required ReportDataset Dataset { get; init; }
    public required IReadOnlyList<ReportDimension> AllowedDimensions { get; init; }
    public required IReadOnlyList<ReportMeasure> AllowedMeasures { get; init; }
}

/// <summary>
/// The whitelist itself (BIO-RPT-002-AC01: "dimensions/measures are whitelisted"). A static,
/// code-defined table - not configuration, not caller-suppliable - so "governed" is a property
/// of the deployed binary, not of runtime state a tenant could influence.
/// </summary>
public static class ReportSchemaRegistry
{
    public static readonly IReadOnlyDictionary<ReportDataset, ReportDatasetSchema> Datasets =
        new Dictionary<ReportDataset, ReportDatasetSchema>
        {
            [ReportDataset.CardProduction] = new ReportDatasetSchema
            {
                Dataset = ReportDataset.CardProduction,
                AllowedDimensions = [ReportDimension.Status, ReportDimension.Source, ReportDimension.CardClass],
                AllowedMeasures = [ReportMeasure.Count],
            },
            [ReportDataset.Attendance] = new ReportDatasetSchema
            {
                Dataset = ReportDataset.Attendance,
                AllowedDimensions = [ReportDimension.Mode, ReportDimension.Source, ReportDimension.VerificationMethod],
                AllowedMeasures = [ReportMeasure.Count],
            },
        };

    public static bool TryGet(ReportDataset dataset, out ReportDatasetSchema schema) =>
        Datasets.TryGetValue(dataset, out schema!);
}
