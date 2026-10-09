using System.Text.Json.Serialization;
using Biolog.Features.DocumentTemplatePack.Model;

namespace Biolog.Features.DocumentTemplatePack.Api;

public sealed record DocumentTemplateFieldViolationResponse
{
    [JsonPropertyName("field_name")]
    public required string FieldName { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    public static DocumentTemplateFieldViolationResponse From(DocumentTemplatePackFieldViolation violation) => new()
    {
        FieldName = violation.FieldName,
        Reason = violation.Reason,
    };
}

/// <summary>
/// BIO-DOC-010-AC01/AC04's own wire contract, and AC05's: <c>state</c>/<c>message</c> are exactly
/// the two fields <c>web/features/documents/live.ts</c>'s <c>LiveStepBody</c> already reads from
/// any live response at <c>POST /api/v1/documents/identify</c> (the existing, previously
/// unconnected "document-identification" step - BIO-UX-009, already reviewed/merged) - this
/// response is a superset, not a different shape, so the existing frontend needs no change to
/// consume it (the same reasoning BIO-DOC-009's own <c>AuthenticityAssessmentResponse</c> already
/// documented for itself).
/// </summary>
public sealed record DocumentTemplateIdentifyResponse
{
    /// <summary>web/features/documents/evidence.ts's EvidenceCheckState vocabulary.</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("pack_id")]
    public required string PackId { get; init; }

    [JsonPropertyName("pack_version")]
    public required string PackVersion { get; init; }

    [JsonPropertyName("entry_id")]
    public required string EntryId { get; init; }

    [JsonPropertyName("country")]
    public required string Country { get; init; }

    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("document_class")]
    public required string DocumentClass { get; init; }

    [JsonPropertyName("series")]
    public required string Series { get; init; }

    [JsonPropertyName("violations")]
    public required IReadOnlyList<DocumentTemplateFieldViolationResponse> Violations { get; init; }

    [JsonPropertyName("record_id")]
    public required string RecordId { get; init; }

    [JsonPropertyName("recorded_at")]
    public required string RecordedAt { get; init; }

    public static DocumentTemplateIdentifyResponse From(DocumentTemplateVerificationOutcome outcome) => new()
    {
        State = outcome.State.ToWireString(),
        Message = BuildMessage(outcome),
        PackId = outcome.PackId,
        PackVersion = outcome.PackVersion,
        EntryId = outcome.EntryId,
        Country = outcome.Country,
        Issuer = outcome.Issuer,
        DocumentClass = outcome.DocumentClass,
        Series = outcome.Series,
        Violations = [.. outcome.Violations.Select(DocumentTemplateFieldViolationResponse.From)],
        RecordId = outcome.RecordId,
        RecordedAt = outcome.RecordedAt,
    };

    private static string BuildMessage(DocumentTemplateVerificationOutcome outcome)
    {
        var identity = $"{outcome.Issuer} {outcome.DocumentClass} ({outcome.Series})";
        return outcome.State switch
        {
            DocumentTemplateVerificationState.Consistent =>
                $"Identified as {identity} — matches governed pack {outcome.PackVersion}, all required fields present.",
            DocumentTemplateVerificationState.Failed =>
                $"Governed-pack validation failed against {identity} — pack {outcome.PackVersion}: {DescribeViolations(outcome.Violations)}.",
            _ =>
                $"Partially matched {identity} — pack {outcome.PackVersion}; {DescribeViolations(outcome.Violations)} — routed to human review.",
        };
    }

    private static string DescribeViolations(IReadOnlyList<DocumentTemplatePackFieldViolation> violations) =>
        string.Join("; ", violations.Select(v => $"{v.FieldName}: {v.Reason}"));
}
