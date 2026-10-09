using System.Text.Json.Serialization;
using Biolog.Features.ImportConflictReview.Domain;

namespace Biolog.Features.ImportConflictReview.Api;

public sealed record ImportFieldConflictResponse
{
    [JsonPropertyName("person_field")]
    public required string PersonField { get; init; }

    [JsonPropertyName("existing_value")]
    public string? ExistingValue { get; init; }

    [JsonPropertyName("incoming_value")]
    public string? IncomingValue { get; init; }

    public static ImportFieldConflictResponse From(ImportFieldConflict conflict) => new()
    {
        PersonField = conflict.PersonField,
        ExistingValue = conflict.ExistingValue,
        IncomingValue = conflict.IncomingValue,
    };
}

public sealed record ImportConflictReviewResponse
{
    [JsonPropertyName("mapping_id")]
    public required string MappingId { get; init; }

    [JsonPropertyName("external_key")]
    public required string ExternalKey { get; init; }

    [JsonPropertyName("person_id")]
    public string? PersonId { get; init; }

    [JsonPropertyName("conflicts")]
    public required IReadOnlyList<ImportFieldConflictResponse> Conflicts { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("source_batch_id")]
    public required string SourceBatchId { get; init; }

    [JsonPropertyName("raised_at")]
    public required string RaisedAt { get; init; }

    [JsonPropertyName("last_seen_at")]
    public required string LastSeenAt { get; init; }

    [JsonPropertyName("decided_at")]
    public string? DecidedAt { get; init; }

    [JsonPropertyName("decided_by_actor_id")]
    public string? DecidedByActorId { get; init; }

    [JsonPropertyName("decision_reason_code")]
    public string? DecisionReasonCode { get; init; }

    [JsonPropertyName("decision_notes")]
    public string? DecisionNotes { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    public static ImportConflictReviewResponse From(Domain.ImportConflictReview review) => new()
    {
        MappingId = review.MappingId,
        ExternalKey = review.ExternalKey,
        PersonId = review.PersonId,
        Conflicts = [.. review.Conflicts.Select(ImportFieldConflictResponse.From)],
        Status = review.Status.ToWireString(),
        SourceBatchId = review.SourceBatchId,
        RaisedAt = review.RaisedAt,
        LastSeenAt = review.LastSeenAt,
        DecidedAt = review.DecidedAt,
        DecidedByActorId = review.DecidedByActorId,
        DecisionReasonCode = review.DecisionReasonCode,
        DecisionNotes = review.DecisionNotes,
        Version = review.Version,
    };
}
