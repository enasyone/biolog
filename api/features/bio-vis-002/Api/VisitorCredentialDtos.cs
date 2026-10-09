using System.Text.Json.Serialization;
using Biolog.Features.VisitorCredentials.Domain;

namespace Biolog.Features.VisitorCredentials.Api;

// --- Request DTOs (wire shape only - the service owns all parsing/validation) ---

public sealed record GrantVisitorCredentialRequest
{
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; init; }
}

public sealed record RevokeVisitorCredentialRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>Deliberately the exact same wire shape as BIO-VIS-001's own <c>VersionedVisitorRequest</c> (<c>{"version": ...}</c>) - this endpoint takes over that story's own route, and nothing about the caller's request contract should need to change.</summary>
public sealed record VersionedCheckOutRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

/// <summary>BIO-VIS-002-F06's own remediation (Seyi round-5 FAIL) - the operator-recovery reconcile endpoint's own request shape.</summary>
public sealed record ReconcileStrandedCardsRequest
{
    [JsonPropertyName("person_id")]
    public string? PersonId { get; init; }
}

// --- Response DTOs ---

public sealed record VisitorCredentialResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("visitor_record_id")]
    public required string VisitorRecordId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    [JsonPropertyName("expires_at")]
    public required string ExpiresAt { get; init; }

    [JsonPropertyName("card_class")]
    public required string CardClass { get; init; }

    /// <summary>Null while <see cref="Status"/> is <c>pending</c> (no backing card issued yet) - BIO-VIS-002-F01's own remediation.</summary>
    [JsonPropertyName("card_sequence_number")]
    public int? CardSequenceNumber { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("revoked_at")]
    public string? RevokedAt { get; init; }

    [JsonPropertyName("revoke_reason")]
    public string? RevokeReason { get; init; }

    /// <summary>Set only when <see cref="Status"/> is <c>failed</c> - BIO-VIS-002-F01's own remediation.</summary>
    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; init; }

    [JsonPropertyName("failed_at")]
    public string? FailedAt { get; init; }

    [JsonPropertyName("granted_by")]
    public required string GrantedBy { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    public static VisitorCredentialResponse From(VisitorCredential c) => new()
    {
        Id = c.Id,
        TenantId = c.TenantId,
        VisitorRecordId = c.VisitorRecordId,
        PersonId = c.PersonId,
        Scope = c.Scope.ToWireString(),
        ExpiresAt = c.ExpiresAt,
        CardClass = c.CardClass,
        CardSequenceNumber = c.CardSequenceNumber,
        Status = c.Status.ToWireString(),
        RevokedAt = c.RevokedAt,
        RevokeReason = c.RevokeReason,
        FailureReason = c.FailureReason,
        FailedAt = c.FailedAt,
        GrantedBy = c.GrantedBy,
        CreatedAt = c.CreatedAt,
        Version = c.Version,
    };
}

public sealed record VisitorCheckOutResponse
{
    [JsonPropertyName("visitor_id")]
    public required string VisitorId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("visit_status")]
    public required string VisitStatus { get; init; }

    [JsonPropertyName("effective_status")]
    public required string EffectiveStatus { get; init; }

    [JsonPropertyName("checked_out_at")]
    public string? CheckedOutAt { get; init; }

    [JsonPropertyName("revoked_credential")]
    public VisitorCredentialResponse? RevokedCredential { get; init; }
}

public sealed record VisitorCredentialHistoryResponse
{
    [JsonPropertyName("visitor_id")]
    public required string VisitorId { get; init; }

    [JsonPropertyName("full_name")]
    public required string FullName { get; init; }

    [JsonPropertyName("host_approval_status")]
    public required string HostApprovalStatus { get; init; }

    [JsonPropertyName("visit_status")]
    public required string VisitStatus { get; init; }

    [JsonPropertyName("effective_status")]
    public required string EffectiveStatus { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("checked_in_at")]
    public string? CheckedInAt { get; init; }

    [JsonPropertyName("checked_out_at")]
    public string? CheckedOutAt { get; init; }

    [JsonPropertyName("expires_at")]
    public required string ExpiresAt { get; init; }

    [JsonPropertyName("credentials")]
    public required IReadOnlyList<VisitorCredentialResponse> Credentials { get; init; }
}

/// <summary>BIO-VIS-002-F06's own remediation (Seyi round-5 FAIL) - the operator-recovery reconcile endpoint's own response shape.</summary>
public sealed record ReconcileStrandedCardsResponse
{
    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("resolved_card_sequence_numbers")]
    public required IReadOnlyList<int> ResolvedSequenceNumbers { get; init; }

    [JsonPropertyName("unresolved_card_sequence_numbers")]
    public required IReadOnlyList<int> UnresolvedSequenceNumbers { get; init; }
}
