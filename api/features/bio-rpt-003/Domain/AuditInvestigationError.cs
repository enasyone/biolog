namespace Biolog.Features.AuditInvestigation.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class AuditInvestigationError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static AuditInvestigationError NotFound(string eventId) => new(
        $"no audit investigation record found for event id '{eventId}'", "audit_investigation_not_found", 404,
        new Dictionary<string, object?> { ["event_id"] = eventId });

    /// <summary>BIO-RPT-003-AC03: "Export is permissioned."</summary>
    public static AuditInvestigationError ExportForbidden() => new(
        "the requesting actor does not hold the 'audit.export' permission", "audit_export_forbidden", 403);

    public static AuditInvestigationError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static AuditInvestigationError AuditUnavailable() => new(
        "the audit sink failed; the export was not returned without an audit record naming it", "audit_unavailable", 503);

    public static AuditInvestigationError ActorRequired() => new(
        "X-Actor-Id is required to record who is asking - provenance, and BIO-RPT-003-AC04's view-time redaction/AC03 export-permission gates both resolve against it", "actor_required", 400);

    public static AuditInvestigationError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static AuditInvestigationError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);
}
