using System.Text.Json.Serialization;
using Biolog.Features.OfflineEnrolment.Domain;

namespace Biolog.Features.OfflineEnrolment.Api;

public sealed record CapturePolicyResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("allowed_fields")]
    public required IReadOnlyList<string> AllowedFields { get; init; }

    [JsonPropertyName("allow_photo_capture")]
    public required bool AllowPhotoCapture { get; init; }

    [JsonPropertyName("allowed_biometric_modalities")]
    public required IReadOnlyList<string> AllowedBiometricModalities { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static CapturePolicyResponse From(EnrolmentCapturePolicy policy) => new()
    {
        TenantId = policy.TenantId,
        AllowedFields = [.. policy.AllowedFields],
        AllowPhotoCapture = policy.AllowPhotoCapture,
        AllowedBiometricModalities = [.. policy.AllowedBiometricModalities],
        Version = policy.Version,
        UpdatedAt = policy.UpdatedAt,
    };
}

public sealed record SetCapturePolicyRequest
{
    [JsonPropertyName("allowed_fields")]
    public required IReadOnlyList<string> AllowedFields { get; init; }

    [JsonPropertyName("allow_photo_capture")]
    public bool AllowPhotoCapture { get; init; }

    [JsonPropertyName("allowed_biometric_modalities")]
    public IReadOnlyList<string> AllowedBiometricModalities { get; init; } = [];
}

/// <summary>Wire shape for a device's enrolment-package submission. <c>package_id</c> is minted locally on the device (its own idempotency key, mirroring <c>mobile/lib/core/offline/offline_mutation.dart</c>'s own id-minting convention).</summary>
public sealed record EnrolmentPackageSubmitRequest
{
    [JsonPropertyName("package_id")]
    public required string PackageId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("device_id")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("field_values")]
    public required IReadOnlyDictionary<string, string> FieldValues { get; init; }

    [JsonPropertyName("photo_captured")]
    public bool PhotoCaptured { get; init; }

    [JsonPropertyName("photo_content_base64")]
    public string? PhotoContentBase64 { get; init; }

    [JsonPropertyName("biometric_modalities_captured")]
    public IReadOnlyList<string> BiometricModalitiesCaptured { get; init; } = [];

    [JsonPropertyName("biometric_payloads_base64")]
    public IReadOnlyDictionary<string, string> BiometricPayloadsBase64 { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("submitted_at")]
    public required string SubmittedAt { get; init; }
}

public sealed record EnrolmentPackageAckResponse
{
    [JsonPropertyName("package_id")]
    public required string PackageId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("received_at")]
    public required string ReceivedAt { get; init; }

    public static EnrolmentPackageAckResponse From(EnrolmentPackage package, string receivedAt) => new()
    {
        PackageId = package.Id,
        Status = package.Status.ToWireString(),
        ReceivedAt = receivedAt,
    };
}

public sealed record EnrolmentPackageResponse
{
    [JsonPropertyName("package_id")]
    public required string PackageId { get; init; }

    [JsonPropertyName("field_values")]
    public required IReadOnlyDictionary<string, string> FieldValues { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("submitted_at")]
    public required string SubmittedAt { get; init; }

    public static EnrolmentPackageResponse From(EnrolmentPackage package) => new()
    {
        PackageId = package.Id,
        FieldValues = package.FieldValues,
        Status = package.Status.ToWireString(),
        SubmittedAt = package.SubmittedAt,
    };
}

public sealed record EnrolmentPackageListResponse
{
    [JsonPropertyName("items")]
    public required IReadOnlyList<EnrolmentPackageResponse> Items { get; init; }
}

public sealed record DuplicateCandidateResponse
{
    [JsonPropertyName("candidate_id")]
    public required string CandidateId { get; init; }

    [JsonPropertyName("package_id")]
    public required string PackageId { get; init; }

    [JsonPropertyName("matched_package_id")]
    public required string MatchedPackageId { get; init; }

    [JsonPropertyName("signal_source")]
    public required string SignalSource { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("raised_at")]
    public required string RaisedAt { get; init; }

    public static DuplicateCandidateResponse From(EnrolmentDuplicateCandidate candidate) => new()
    {
        CandidateId = candidate.Id,
        PackageId = candidate.PackageId,
        MatchedPackageId = candidate.MatchedPackageId,
        SignalSource = candidate.SignalSource.ToWireString(),
        Status = candidate.Status.ToWireString(),
        RaisedAt = candidate.RaisedAt,
    };
}

public sealed record DuplicateCandidateListResponse
{
    [JsonPropertyName("items")]
    public required IReadOnlyList<DuplicateCandidateResponse> Items { get; init; }
}

/// <summary>AC04: a human decision on a raised duplicate candidate always carries a mandatory reason code (ADR-0001 §3.5's "no silent last-write-wins," applied to identity data - a decision here is never inferred, only explicitly recorded).</summary>
public sealed record DuplicateCandidateDecisionRequest
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("reason_code")]
    public required string ReasonCode { get; init; }
}
