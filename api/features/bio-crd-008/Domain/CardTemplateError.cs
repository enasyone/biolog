namespace Biolog.Features.CardTemplates.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class CardTemplateError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static CardTemplateError NotFound(string templateId, int? version = null) => new(
        version is null
            ? $"no card template lineage found for id '{templateId}'"
            : $"no card template version {version} found for lineage '{templateId}'",
        "card_template_not_found", 404,
        new Dictionary<string, object?> { ["template_id"] = templateId, ["version"] = version });

    public static CardTemplateError NoPublishedVersion(string templateId) => new(
        $"card template lineage '{templateId}' has no currently published version", "card_template_not_published", 404,
        new Dictionary<string, object?> { ["template_id"] = templateId });

    /// <summary>AC01: "Published template is immutable."</summary>
    public static CardTemplateError Immutable(string templateId, int version, CardTemplateStatus status) => new(
        $"card template '{templateId}' version {version} is {status.ToWireString()} and cannot be edited in place - create a new version instead (AC02)",
        "card_template_immutable", 409,
        new Dictionary<string, object?> { ["template_id"] = templateId, ["version"] = version, ["status"] = status.ToWireString() });

    public static CardTemplateError InvalidTransition(CardTemplateStatus from, string action) => new(
        $"'{action}' is not a valid transition from status '{from.ToWireString()}'", "card_template_invalid_transition", 409,
        new Dictionary<string, object?> { ["from_status"] = from.ToWireString(), ["action"] = action });

    public static CardTemplateError ConcurrencyConflict(string templateId, int version, int expectedRevision) => new(
        $"card template '{templateId}' version {version} was modified concurrently (expected revision {expectedRevision})", "card_template_concurrency_conflict", 409,
        new Dictionary<string, object?> { ["template_id"] = templateId, ["version"] = version, ["expected_revision"] = expectedRevision });

    public static CardTemplateError UnknownFormat(CardFormatKind kind) => new(
        $"'{kind}' is not a governed card format", "card_template_format_not_whitelisted", 422,
        new Dictionary<string, object?> { ["format_kind"] = kind.ToString() });

    public static CardTemplateError CustomSizeRequired() => new(
        "a custom format requires custom_size_mm", "card_template_custom_size_required", 422);

    public static CardTemplateError CustomSizeForbidden() => new(
        "custom_size_mm is only valid for the custom format", "card_template_custom_size_forbidden", 422);

    public static CardTemplateError UnknownSignatureAsset(string lineageId, int version) => new(
        $"no signature asset found for lineage '{lineageId}' version {version} - a template's signature rule must reference a real, existing signature asset", "card_template_signature_asset_not_found", 422,
        new Dictionary<string, object?> { ["lineage_id"] = lineageId, ["version"] = version });

    public static CardTemplateError SignatureLineageRequired() => new(
        "a required signature rule needs both lineage_id and version", "card_template_signature_lineage_required", 422);

    public static CardTemplateError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static CardTemplateError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static CardTemplateError ActorRequired() => new(
        "X-Actor-Id is required to record who made this change (provenance)", "actor_required", 400);

    public static CardTemplateError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static CardTemplateError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);
}
