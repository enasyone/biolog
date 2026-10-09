using System.Text.Json.Serialization;
using Biolog.Features.PhotoQuality.Domain;

namespace Biolog.Features.PhotoQuality.Api;

/// <summary>BIO-PHO-003-AC01/AC02: one independent quality verdict, always carrying its own reasons - empty exactly when <c>passed</c> is true.</summary>
public sealed record QualityVerdictResponse
{
    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("reasons")]
    public required IReadOnlyList<string> Reasons { get; init; }
}

public sealed record PhotoQualityCaptureResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("format")]
    public required string Format { get; init; }

    [JsonPropertyName("width_px")]
    public required int WidthPx { get; init; }

    [JsonPropertyName("height_px")]
    public required int HeightPx { get; init; }

    [JsonPropertyName("native_dpi")]
    public double? NativeDpi { get; init; }

    [JsonPropertyName("print_quality")]
    public required QualityVerdictResponse PrintQuality { get; init; }

    [JsonPropertyName("biometric_suitability")]
    public required QualityVerdictResponse BiometricSuitability { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("decision_reason")]
    public string? DecisionReason { get; init; }

    [JsonPropertyName("promoted_master_version")]
    public int? PromotedMasterVersion { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("submitted_at")]
    public required string SubmittedAt { get; init; }

    [JsonPropertyName("decided_at")]
    public string? DecidedAt { get; init; }

    public static PhotoQualityCaptureResponse From(PhotoQualityCapture capture) => new()
    {
        PersonId = capture.PersonId,
        TenantId = capture.TenantId,
        SequenceNumber = capture.SequenceNumber,
        Format = capture.Format.ToWireString(),
        WidthPx = capture.WidthPx,
        HeightPx = capture.HeightPx,
        NativeDpi = capture.NativeDpi,
        PrintQuality = new QualityVerdictResponse { Passed = capture.PrintQualityPassed, Reasons = capture.PrintQualityReasons },
        BiometricSuitability = new QualityVerdictResponse { Passed = capture.BiometricSuitabilityPassed, Reasons = capture.BiometricSuitabilityReasons },
        Status = capture.Status.ToString(),
        DecisionReason = capture.DecisionReason,
        PromotedMasterVersion = capture.PromotedMasterVersion,
        Version = capture.Version,
        SubmittedAt = capture.SubmittedAt,
        DecidedAt = capture.DecidedAt,
    };
}

public sealed record PhotoQualityCaptureListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<PhotoQualityCaptureResponse> Data { get; init; }
}

/// <summary>Body of <c>POST /api/v1/photo-quality/captures/{personId}/{sequenceNumber}/reject</c>.</summary>
public sealed record RejectCaptureRequest
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("expected_version")]
    public required int ExpectedVersion { get; init; }
}

/// <summary>Body of <c>POST /api/v1/photo-quality/captures/{personId}/{sequenceNumber}/approve</c>.</summary>
public sealed record ApproveCaptureRequest
{
    [JsonPropertyName("expected_version")]
    public required int ExpectedVersion { get; init; }
}
