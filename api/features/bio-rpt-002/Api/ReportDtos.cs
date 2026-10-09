using System.Text.Json;
using System.Text.Json.Serialization;
using Biolog.Features.ReportBuilder.Domain;

namespace Biolog.Features.ReportBuilder.Api;

public static class ReportJsonOptions
{
    public static readonly JsonSerializerOptions Strict = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

public sealed record CreateReportRequest
{
    [JsonPropertyName("dataset")]
    public required string Dataset { get; init; }

    [JsonPropertyName("dimension")]
    public required string Dimension { get; init; }

    [JsonPropertyName("measure")]
    public required string Measure { get; init; }
}

public sealed record ReportDatasetSchemaResponse
{
    [JsonPropertyName("dataset")]
    public required string Dataset { get; init; }

    [JsonPropertyName("allowed_dimensions")]
    public required IReadOnlyList<string> AllowedDimensions { get; init; }

    [JsonPropertyName("allowed_measures")]
    public required IReadOnlyList<string> AllowedMeasures { get; init; }

    public static ReportDatasetSchemaResponse From(ReportDatasetSchema schema) => new()
    {
        Dataset = schema.Dataset.ToWireString(),
        AllowedDimensions = [.. schema.AllowedDimensions.Select(d => d.ToWireString())],
        AllowedMeasures = [.. schema.AllowedMeasures.Select(m => m.ToWireString())],
    };
}

public sealed record ReportSchemaResponse
{
    [JsonPropertyName("datasets")]
    public required IReadOnlyList<ReportDatasetSchemaResponse> Datasets { get; init; }
}

public sealed record ReportResultResponse
{
    [JsonPropertyName("columns")]
    public required IReadOnlyList<string> Columns { get; init; }

    [JsonPropertyName("rows")]
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }

    [JsonPropertyName("source_record_count")]
    public required int SourceRecordCount { get; init; }

    [JsonPropertyName("generated_at")]
    public required string GeneratedAt { get; init; }

    public static ReportResultResponse From(ReportResult result) => new()
    {
        Columns = result.Columns,
        Rows = result.Rows,
        SourceRecordCount = result.SourceRecordCount,
        GeneratedAt = result.GeneratedAt,
    };
}

public sealed record ReportJobResponse
{
    [JsonPropertyName("job_id")]
    public required string JobId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("dataset")]
    public required string Dataset { get; init; }

    [JsonPropertyName("dimension")]
    public required string Dimension { get; init; }

    [JsonPropertyName("measure")]
    public required string Measure { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("error_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReportResultResponse? Result { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static ReportJobResponse From(ReportJob job) => new()
    {
        JobId = job.JobId,
        Status = job.Status.ToWireString(),
        Dataset = job.Definition.Dataset.ToWireString(),
        Dimension = job.Definition.Dimension.ToWireString(),
        Measure = job.Definition.Measure.ToWireString(),
        Version = job.Version,
        ErrorCode = job.ErrorCode,
        Result = job.Result is null ? null : ReportResultResponse.From(job.Result),
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt,
    };
}
