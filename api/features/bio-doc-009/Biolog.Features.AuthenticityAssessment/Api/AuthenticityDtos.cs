using System.Text.Json.Serialization;
using Biolog.Features.AuthenticityAssessment.Model;

namespace Biolog.Features.AuthenticityAssessment.Api;

public sealed record AuthenticityCheckResponse
{
    [JsonPropertyName("check_id")]
    public required string CheckId { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }

    public static AuthenticityCheckResponse From(AuthenticityCheckResult check) => new()
    {
        CheckId = check.CheckId.ToWireString(),
        Outcome = check.Outcome.ToWireString(),
        Detail = check.Detail,
    };
}

public sealed record AuthenticityOverrideResponse
{
    [JsonPropertyName("disposition")]
    public required string Disposition { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("overridden_by")]
    public required string OverriddenBy { get; init; }

    [JsonPropertyName("overridden_at")]
    public required string OverriddenAt { get; init; }

    public static AuthenticityOverrideResponse From(AuthenticityManualOverride manualOverride) => new()
    {
        Disposition = manualOverride.Disposition.ToWireString(),
        Reason = manualOverride.Reason,
        OverriddenBy = manualOverride.OverriddenBy,
        OverriddenAt = manualOverride.OverriddenAt,
    };
}

/// <summary>
/// BIO-DOC-009-AC01's full, explainable assessment - individual <see cref="Checks"/> plus the
/// overall <see cref="Disposition"/> - and AC04's evidence/template/ruleset versions. Also
/// satisfies AC06's wire contract: <c>state</c>/<c>message</c>/<c>confidence</c> are exactly the
/// three fields <c>web/features/documents/live.ts</c>'s <c>LiveStepBody</c> already reads from any
/// live response (BIO-UX-009, already reviewed/merged) - this response is a superset, not a
/// different shape, so the existing frontend needs no change to consume it.
/// </summary>
public sealed record AuthenticityAssessmentResponse
{
    [JsonPropertyName("document_session_id")]
    public required string DocumentSessionId { get; init; }

    /// <summary>web/features/documents/evidence.ts's EvidenceCheckState vocabulary.</summary>
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("confidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Confidence { get; init; }

    /// <summary>The policy engine's own computed result - never changed by a manual override.</summary>
    [JsonPropertyName("disposition")]
    public required string Disposition { get; init; }

    /// <summary>What the workflow should act on: the override's disposition if one is active,
    /// otherwise the same value as <see cref="Disposition"/>.</summary>
    [JsonPropertyName("effective_disposition")]
    public required string EffectiveDisposition { get; init; }

    [JsonPropertyName("checks")]
    public required IReadOnlyList<AuthenticityCheckResponse> Checks { get; init; }

    [JsonPropertyName("evidence_version")]
    public required int EvidenceVersion { get; init; }

    [JsonPropertyName("template_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateId { get; init; }

    [JsonPropertyName("template_version_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TemplateVersionNumber { get; init; }

    [JsonPropertyName("ruleset_version")]
    public required string RulesetVersion { get; init; }

    [JsonPropertyName("computed_at")]
    public required string ComputedAt { get; init; }

    [JsonPropertyName("audit_event_id")]
    public required string AuditEventId { get; init; }

    [JsonPropertyName("override")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthenticityOverrideResponse? Override { get; init; }

    /// <summary>The aggregate's own CAS version - pass back as <c>expected_version</c> on a
    /// subsequent override request.</summary>
    [JsonPropertyName("version")]
    public required int Version { get; init; }

    public static AuthenticityAssessmentResponse From(Model.AuthenticityAssessment assessment)
    {
        var result = assessment.Result;
        var effective = assessment.EffectiveDisposition;
        return new AuthenticityAssessmentResponse
        {
            DocumentSessionId = result.DocumentSessionId,
            State = effective.ToEvidenceCheckState(),
            Message = BuildMessage(result, assessment.Override),
            Disposition = result.PolicyDisposition.ToWireString(),
            EffectiveDisposition = effective.ToWireString(),
            Checks = [.. result.Checks.Select(AuthenticityCheckResponse.From)],
            EvidenceVersion = result.EvidenceVersion,
            TemplateId = result.TemplateId,
            TemplateVersionNumber = result.TemplateVersionNumber,
            RulesetVersion = result.RulesetVersion,
            ComputedAt = result.ComputedAt,
            AuditEventId = result.AuditEventId,
            Override = assessment.Override is { } manualOverride ? AuthenticityOverrideResponse.From(manualOverride) : null,
            Version = assessment.Version,
        };
    }

    private static string BuildMessage(AuthenticityAssessmentResult result, AuthenticityManualOverride? manualOverride)
    {
        if (manualOverride is not null)
        {
            return $"Manually overridden to {manualOverride.Disposition.ToWireString()} by {manualOverride.OverriddenBy}: {manualOverride.Reason}";
        }

        var failed = result.Checks.Where(c => c.Outcome == AuthenticityCheckOutcome.Failed).ToList();
        return result.PolicyDisposition switch
        {
            AuthenticityDisposition.DocumentAuthentic => "Document authenticity assessment: AUTHENTIC - supported checks passed and no conflicts were found.",
            AuthenticityDisposition.DocumentNotAuthentic when failed.Count > 0 =>
                $"Document authenticity assessment: NOT AUTHENTIC - {string.Join("; ", failed.Select(c => $"{c.CheckId.ToWireString()}: {c.Detail}"))}",
            AuthenticityDisposition.DocumentNotAuthentic => "Document authenticity assessment: NOT AUTHENTIC.",
            _ => "Document authenticity assessment: INCONCLUSIVE - insufficient supported evidence to reach a policy decision — routed to human review.",
        };
    }
}

public sealed record ApplyOverrideRequest
{
    [JsonPropertyName("disposition")]
    public required string Disposition { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("expected_version")]
    public required int ExpectedVersion { get; init; }
}
