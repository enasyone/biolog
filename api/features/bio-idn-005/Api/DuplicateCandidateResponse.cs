using System.Text.Json.Serialization;
using Biolog.Features.DuplicateReview.Domain;

namespace Biolog.Features.DuplicateReview.Api;

public sealed record DuplicateCandidateResponse
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id_low")]
    public required string PersonIdLow { get; init; }

    [JsonPropertyName("person_id_high")]
    public required string PersonIdHigh { get; init; }

    [JsonPropertyName("signal_sources")]
    public required IReadOnlyList<string> SignalSources { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

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

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    public static DuplicateCandidateResponse From(DuplicateCandidate candidate) => new()
    {
        TenantId = candidate.TenantId,
        PersonIdLow = candidate.PersonIdLow,
        PersonIdHigh = candidate.PersonIdHigh,
        SignalSources = [.. candidate.SignalSources.Select(s => s.ToWireString()).OrderBy(s => s, StringComparer.Ordinal)],
        Status = candidate.Status.ToWireString(),
        DecidedAt = candidate.DecidedAt,
        DecidedByActorId = candidate.DecidedByActorId,
        DecisionReasonCode = candidate.DecisionReasonCode,
        DecisionNotes = candidate.DecisionNotes,
        Version = candidate.Version,
        CreatedAt = candidate.CreatedAt,
        UpdatedAt = candidate.UpdatedAt,
    };
}

public sealed record DuplicateCandidateListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<DuplicateCandidateResponse> Data { get; init; }
}
