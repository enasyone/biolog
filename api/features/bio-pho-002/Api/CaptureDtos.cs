using System.Text.Json.Serialization;
using Biolog.Features.PhotoBooth.Domain;

namespace Biolog.Features.PhotoBooth.Api;

/// <summary>BIO-PHO-002-AC01: the versioned capture contract a booth fetches before capturing.</summary>
public sealed record CaptureSpecificationResponse
{
    [JsonPropertyName("specification_version")]
    public required int SpecificationVersion { get; init; }

    [JsonPropertyName("frame_width_px")]
    public required int FrameWidthPx { get; init; }

    [JsonPropertyName("frame_height_px")]
    public required int FrameHeightPx { get; init; }

    [JsonPropertyName("aspect_ratio_width")]
    public required int AspectRatioWidth { get; init; }

    [JsonPropertyName("aspect_ratio_height")]
    public required int AspectRatioHeight { get; init; }

    [JsonPropertyName("minimum_dpi")]
    public required int MinimumDpi { get; init; }

    [JsonPropertyName("min_face_height_ratio")]
    public required double MinFaceHeightRatio { get; init; }

    [JsonPropertyName("max_face_height_ratio")]
    public required double MaxFaceHeightRatio { get; init; }

    [JsonPropertyName("background_requirement")]
    public required string BackgroundRequirement { get; init; }

    public static CaptureSpecificationResponse From(CaptureSpecification spec) => new()
    {
        SpecificationVersion = spec.SpecificationVersion,
        FrameWidthPx = spec.FrameWidthPx,
        FrameHeightPx = spec.FrameHeightPx,
        AspectRatioWidth = spec.AspectRatioWidth,
        AspectRatioHeight = spec.AspectRatioHeight,
        MinimumDpi = spec.MinimumDpi,
        MinFaceHeightRatio = spec.MinFaceHeightRatio,
        MaxFaceHeightRatio = spec.MaxFaceHeightRatio,
        BackgroundRequirement = spec.BackgroundRequirement,
    };
}

public sealed record BoothCaptureResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("device_id")]
    public required string DeviceId { get; init; }

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

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("rejection_reasons")]
    public required IReadOnlyList<string> RejectionReasons { get; init; }

    [JsonPropertyName("promoted_master_version")]
    public int? PromotedMasterVersion { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("submitted_at")]
    public required string SubmittedAt { get; init; }

    [JsonPropertyName("decided_at")]
    public string? DecidedAt { get; init; }

    public static BoothCaptureResponse From(BoothCapture capture) => new()
    {
        PersonId = capture.PersonId,
        TenantId = capture.TenantId,
        DeviceId = capture.DeviceId,
        SequenceNumber = capture.SequenceNumber,
        Format = capture.Format.ToWireString(),
        WidthPx = capture.WidthPx,
        HeightPx = capture.HeightPx,
        NativeDpi = capture.NativeDpi,
        Status = capture.Status.ToString(),
        RejectionReasons = capture.RejectionReasons,
        PromotedMasterVersion = capture.PromotedMasterVersion,
        Version = capture.Version,
        SubmittedAt = capture.SubmittedAt,
        DecidedAt = capture.DecidedAt,
    };
}

public sealed record BoothCaptureListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<BoothCaptureResponse> Data { get; init; }
}

/// <summary>Body of <c>POST /api/v1/photo-booth/captures/{personId}/{sequenceNumber}/reject</c>.</summary>
public sealed record RejectCaptureRequest
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("expected_version")]
    public required int ExpectedVersion { get; init; }
}

/// <summary>Body of <c>POST /api/v1/photo-booth/captures/{personId}/{sequenceNumber}/approve</c>.</summary>
public sealed record ApproveCaptureRequest
{
    [JsonPropertyName("expected_version")]
    public required int ExpectedVersion { get; init; }
}
