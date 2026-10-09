namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>A governed report's output: one row per distinct value of the requested dimension, one column for that value and one for the requested measure's aggregate.</summary>
public sealed record ReportResult
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }
    public required int SourceRecordCount { get; init; }
    public required string GeneratedAt { get; init; }
}
