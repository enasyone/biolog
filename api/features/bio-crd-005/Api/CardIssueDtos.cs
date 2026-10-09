using System.Text.Json;
using System.Text.Json.Serialization;
using Biolog.Features.CardIssuance.Audit;
using Biolog.Features.CardIssuance.Domain;

namespace Biolog.Features.CardIssuance.Api;

public static class CardIssueJsonOptions
{
    public static readonly JsonSerializerOptions Strict = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

public sealed record IssueCardWireRequest
{
    [JsonPropertyName("template_ref")]
    public required string TemplateRef { get; init; }

    [JsonPropertyName("photo_ref")]
    public string? PhotoRef { get; init; }

    [JsonPropertyName("logo_ref")]
    public string? LogoRef { get; init; }

    [JsonPropertyName("signature_ref")]
    public string? SignatureRef { get; init; }

    [JsonPropertyName("printer_ref")]
    public required string PrinterRef { get; init; }
}

/// <summary>Every mutating step other than issuance takes only the CAS precondition (ADR-0001 §3.2).</summary>
public sealed record CardIssueStepRequest
{
    [JsonPropertyName("expected_version")]
    public int? ExpectedVersion { get; init; }
}

public sealed record CardIssueProvenanceResponse
{
    [JsonPropertyName("template_ref")]
    public required string TemplateRef { get; init; }

    [JsonPropertyName("photo_ref")]
    public string? PhotoRef { get; init; }

    [JsonPropertyName("logo_ref")]
    public string? LogoRef { get; init; }

    [JsonPropertyName("signature_ref")]
    public string? SignatureRef { get; init; }

    [JsonPropertyName("operator_actor_id")]
    public required string OperatorActorId { get; init; }

    [JsonPropertyName("operator_actor_type")]
    public required string OperatorActorType { get; init; }

    [JsonPropertyName("printer_ref")]
    public required string PrinterRef { get; init; }

    [JsonPropertyName("issued_at")]
    public required string IssuedAt { get; init; }

    public static CardIssueProvenanceResponse From(CardIssueProvenance provenance) => new()
    {
        TemplateRef = provenance.TemplateRef,
        PhotoRef = provenance.PhotoRef,
        LogoRef = provenance.LogoRef,
        SignatureRef = provenance.SignatureRef,
        OperatorActorId = provenance.Operator.Id,
        OperatorActorType = provenance.Operator.Type.ToWireString(),
        PrinterRef = provenance.PrinterRef,
        IssuedAt = provenance.IssuedAt,
    };
}

public sealed record CardIssueResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("card_class")]
    public required string CardClass { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    /// <summary>BIO-CRD-005-AC01: the "001, 002, 003…" display form ADR-0001 §1.6 itself names verbatim as this feature's own business identifier.</summary>
    [JsonPropertyName("sequence_display")]
    public required string SequenceDisplay { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("provenance")]
    public required CardIssueProvenanceResponse Provenance { get; init; }

    [JsonPropertyName("encoded_at")]
    public string? EncodedAt { get; init; }

    [JsonPropertyName("printed_at")]
    public string? PrintedAt { get; init; }

    [JsonPropertyName("verified_at")]
    public string? VerifiedAt { get; init; }

    [JsonPropertyName("activated_at")]
    public string? ActivatedAt { get; init; }

    [JsonPropertyName("deactivated_at")]
    public string? DeactivatedAt { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static CardIssueResponse From(CardIssue issue) => new()
    {
        PersonId = issue.PersonId,
        CardClass = issue.CardClass,
        SequenceNumber = issue.SequenceNumber,
        SequenceDisplay = issue.SequenceNumber.ToString("D3"),
        Status = issue.Status.ToWireString(),
        Version = issue.Version,
        Provenance = CardIssueProvenanceResponse.From(issue.Provenance),
        EncodedAt = issue.EncodedAt,
        PrintedAt = issue.PrintedAt,
        VerifiedAt = issue.VerifiedAt,
        ActivatedAt = issue.ActivatedAt,
        DeactivatedAt = issue.DeactivatedAt,
        CreatedAt = issue.CreatedAt,
        UpdatedAt = issue.UpdatedAt,
    };
}

public sealed record CardIssueListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<CardIssueResponse> Data { get; init; }
}
