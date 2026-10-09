using System.Text.Json;
using System.Text.Json.Serialization;
using Biolog.Features.CardProduction.Audit;
using Biolog.Features.CardProduction.Domain;

namespace Biolog.Features.CardProduction.Api;

public static class ProductionJsonOptions
{
    public static readonly JsonSerializerOptions Strict = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

public sealed record CreateJobRequest
{
    [JsonPropertyName("printer_ref")]
    public required string PrinterRef { get; init; }

    [JsonPropertyName("workstation_ref")]
    public required string WorkstationRef { get; init; }

    [JsonPropertyName("reprint_reason")]
    public string? ReprintReason { get; init; }
}

public sealed record StepRequest
{
    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("expected_version")]
    public int? ExpectedVersion { get; init; }
}

public sealed record RejectRequest
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("expected_version")]
    public int? ExpectedVersion { get; init; }
}

public sealed record VersionedRequest
{
    [JsonPropertyName("expected_version")]
    public int? ExpectedVersion { get; init; }
}

public sealed record ProductionStepResultResponse
{
    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("recorded_at")]
    public required string RecordedAt { get; init; }

    public static ProductionStepResultResponse From(ProductionStepResult result) => new()
    {
        Passed = result.Passed,
        Detail = result.Detail,
        RecordedAt = result.RecordedAt,
    };
}

public sealed record ProductionJobResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("card_class")]
    public required string CardClass { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("job_sequence_number")]
    public required int JobSequenceNumber { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("printer_ref")]
    public required string PrinterRef { get; init; }

    [JsonPropertyName("operator_actor_id")]
    public required string OperatorActorId { get; init; }

    [JsonPropertyName("operator_actor_type")]
    public required string OperatorActorType { get; init; }

    [JsonPropertyName("workstation_ref")]
    public required string WorkstationRef { get; init; }

    [JsonPropertyName("reprint_reason")]
    public string? ReprintReason { get; init; }

    [JsonPropertyName("rejection_reason")]
    public string? RejectionReason { get; init; }

    [JsonPropertyName("print")]
    public ProductionStepResultResponse? Print { get; init; }

    [JsonPropertyName("encode")]
    public ProductionStepResultResponse? Encode { get; init; }

    [JsonPropertyName("readback_qc")]
    public ProductionStepResultResponse? ReadBackQc { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("package_id")]
    public string? PackageId { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static ProductionJobResponse From(ProductionJob job) => new()
    {
        PersonId = job.PersonId,
        CardClass = job.CardClass,
        SequenceNumber = job.SequenceNumber,
        JobSequenceNumber = job.JobSequenceNumber,
        Status = job.Status.ToWireString(),
        Version = job.Version,
        PrinterRef = job.PrinterRef,
        OperatorActorId = job.Operator.Id,
        OperatorActorType = job.Operator.Type.ToWireString(),
        WorkstationRef = job.WorkstationRef,
        ReprintReason = job.ReprintReason,
        RejectionReason = job.RejectionReason,
        Print = job.Print is null ? null : ProductionStepResultResponse.From(job.Print),
        Encode = job.Encode is null ? null : ProductionStepResultResponse.From(job.Encode),
        ReadBackQc = job.ReadBackQc is null ? null : ProductionStepResultResponse.From(job.ReadBackQc),
        Source = job.Source,
        PackageId = job.PackageId,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt,
    };
}

public sealed record ProductionJobListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<ProductionJobResponse> Data { get; init; }
}

public sealed record IssuePackageRequest
{
    [JsonPropertyName("printer_ref")]
    public required string PrinterRef { get; init; }

    [JsonPropertyName("workstation_ref")]
    public required string WorkstationRef { get; init; }

    [JsonPropertyName("reprint_reason")]
    public string? ReprintReason { get; init; }

    [JsonPropertyName("ttl_minutes")]
    public int? TtlMinutes { get; init; }
}

public sealed record ProductionPackageResponse
{
    [JsonPropertyName("package_id")]
    public required string PackageId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("card_class")]
    public required string CardClass { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("issued_at")]
    public required string IssuedAt { get; init; }

    [JsonPropertyName("expires_at")]
    public required string ExpiresAt { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    public static ProductionPackageResponse From(ProductionPackage package, string token) => new()
    {
        PackageId = package.PackageId,
        PersonId = package.PersonId,
        CardClass = package.CardClass,
        SequenceNumber = package.SequenceNumber,
        IssuedAt = package.IssuedAt,
        ExpiresAt = package.ExpiresAt,
        Token = token,
    };
}

public sealed record OfflineCompleteRequest
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("print_passed")]
    public required bool PrintPassed { get; init; }

    [JsonPropertyName("print_detail")]
    public string? PrintDetail { get; init; }

    [JsonPropertyName("encode_passed")]
    public required bool EncodePassed { get; init; }

    [JsonPropertyName("encode_detail")]
    public string? EncodeDetail { get; init; }

    [JsonPropertyName("readback_qc_passed")]
    public required bool ReadBackQcPassed { get; init; }

    [JsonPropertyName("readback_qc_detail")]
    public string? ReadBackQcDetail { get; init; }
}

public sealed record CardIssueSnapshotResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("card_class")]
    public required string CardClass { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    public static CardIssueSnapshotResponse From(CardIssueSnapshot snapshot) => new()
    {
        PersonId = snapshot.PersonId,
        CardClass = snapshot.CardClass,
        SequenceNumber = snapshot.SequenceNumber,
        Status = snapshot.Status.ToString().ToLowerInvariant(),
        Version = snapshot.Version,
    };
}
