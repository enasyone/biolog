using System.Text.Json.Serialization;

namespace Biolog.Features.DocumentVerifyCoordination.Api;

/// <summary>
/// Kemi's BIO-OPS-005 ruling, point 7 (verbatim): <c>web/features/documents/live.ts</c>'s
/// <c>LiveStepBody</c> gains a REQUIRED <c>confidence: number</c> and a REQUIRED
/// <c>provenance: {{ source, evidenceSequenceNumber?, fieldEvidenceSequenceNumbers? }}</c>. The
/// ruling's own TypeScript spelling is camelCase (not this codebase's usual
/// <c>SnakeCaseLower</c> DTO convention - see <c>AuthenticityAssessmentJsonOptions</c> for that
/// convention elsewhere); every property name below is set explicitly to match the ruling's own
/// frontend shape exactly, not inferred from a naming policy.
/// </summary>
public sealed record EvidenceStepResponse
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("provenance")]
    public required EvidenceProvenanceResponse Provenance { get; init; }

    public static EvidenceStepResponse From(EvidenceStepOutcome outcome) => new()
    {
        State = outcome.State,
        Message = outcome.Message,
        Confidence = outcome.Confidence,
        Provenance = EvidenceProvenanceResponse.From(outcome.Provenance),
    };
}

public sealed record EvidenceProvenanceResponse
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("evidenceSequenceNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EvidenceSequenceNumber { get; init; }

    [JsonPropertyName("fieldEvidenceSequenceNumbers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? FieldEvidenceSequenceNumbers { get; init; }

    public static EvidenceProvenanceResponse From(EvidenceProvenance provenance) => new()
    {
        Source = provenance.Source,
        EvidenceSequenceNumber = provenance.EvidenceSequenceNumber,
        FieldEvidenceSequenceNumbers = provenance.FieldEvidenceSequenceNumbers,
    };
}
