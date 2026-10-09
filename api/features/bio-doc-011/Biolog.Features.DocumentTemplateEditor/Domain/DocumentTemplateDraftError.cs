namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class DocumentTemplateDraftError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    public static DocumentTemplateDraftError NotFound(string templateId, int? version = null) => new(
        version is null
            ? $"no document template draft lineage found for id '{templateId}'"
            : $"no document template draft version {version} found for lineage '{templateId}'",
        "document_template_draft_not_found", 404,
        new Dictionary<string, object?> { ["template_id"] = templateId, ["version"] = version });

    public static DocumentTemplateDraftError NoPublishedVersion(string templateId) => new(
        $"document template draft lineage '{templateId}' has no currently published version", "document_template_draft_not_published", 404,
        new Dictionary<string, object?> { ["template_id"] = templateId });

    /// <summary>AC05: "Published versions are immutable."</summary>
    public static DocumentTemplateDraftError Immutable(string templateId, int version, DocumentTemplateDraftStatus status) => new(
        $"document template draft '{templateId}' version {version} is {status.ToWireString()} and cannot be edited in place - create a new version instead",
        "document_template_draft_immutable", 409,
        new Dictionary<string, object?> { ["template_id"] = templateId, ["version"] = version, ["status"] = status.ToWireString() });

    public static DocumentTemplateDraftError InvalidTransition(DocumentTemplateDraftStatus from, string action) => new(
        $"'{action}' is not a valid transition from status '{from.ToWireString()}'", "document_template_draft_invalid_transition", 409,
        new Dictionary<string, object?> { ["from_status"] = from.ToWireString(), ["action"] = action });

    public static DocumentTemplateDraftError ConcurrencyConflict(string templateId, int version, int expectedRevision) => new(
        $"document template draft '{templateId}' version {version} was modified concurrently (expected revision {expectedRevision})", "document_template_draft_concurrency_conflict", 409,
        new Dictionary<string, object?> { ["template_id"] = templateId, ["version"] = version, ["expected_revision"] = expectedRevision });

    /// <summary>AC01: a field/graphic region cannot be drawn before a reference layout exists to draw it against.</summary>
    public static DocumentTemplateDraftError ReferenceLayoutRequired() => new(
        "a reference layout must be uploaded before field/graphic regions can be defined", "document_template_draft_reference_layout_required", 422);

    public static DocumentTemplateDraftError RegionOutOfBounds(string regionName, int left, int top, int width, int height, int layoutWidth, int layoutHeight) => new(
        $"region '{regionName}' ({left},{top},{width}x{height}) does not fit within the uploaded reference layout ({layoutWidth}x{layoutHeight})",
        "document_template_draft_region_out_of_bounds", 422,
        new Dictionary<string, object?> { ["region_name"] = regionName });

    public static DocumentTemplateDraftError InvalidRegionGeometry(string regionName) => new(
        $"region '{regionName}' must have non-negative left/top and a positive size", "document_template_draft_invalid_region_geometry", 422,
        new Dictionary<string, object?> { ["region_name"] = regionName });

    public static DocumentTemplateDraftError DuplicateRegionName(string regionName) => new(
        $"region name '{regionName}' is declared more than once", "document_template_draft_duplicate_region_name", 422,
        new Dictionary<string, object?> { ["region_name"] = regionName });

    public static DocumentTemplateDraftError NoFieldRegions() => new(
        "at least one field region is required before a draft can be submitted for test", "document_template_draft_no_field_regions", 422);

    /// <summary>AC03: "Test corpus must pass before publish" - an empty corpus proves nothing, so it can never satisfy the gate.</summary>
    public static DocumentTemplateDraftError EmptyCorpus() => new(
        "a test corpus must contain at least one sample", "document_template_draft_empty_corpus", 422);

    public static DocumentTemplateDraftError UnsupportedCharacter(string fieldName, char character) => new(
        $"field '{fieldName}' expected value contains '{character}', which this engine's glyph font cannot render (uppercase A-Z, 0-9, space and . , ' - / only)",
        "document_template_draft_unsupported_character", 422,
        new Dictionary<string, object?> { ["field_name"] = fieldName });

    public static DocumentTemplateDraftError ValueTooLongForField(string fieldName, int characterCount) => new(
        $"field '{fieldName}' expected value is longer than its declared character_count ({characterCount})", "document_template_draft_value_too_long", 422,
        new Dictionary<string, object?> { ["field_name"] = fieldName, ["character_count"] = characterCount });

    public static DocumentTemplateDraftError MissingExpectedValue(string sampleId, string fieldName) => new(
        $"corpus sample '{sampleId}' does not supply an expected value for field '{fieldName}'", "document_template_draft_missing_expected_value", 422,
        new Dictionary<string, object?> { ["sample_id"] = sampleId, ["field_name"] = fieldName });

    public static DocumentTemplateDraftError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static DocumentTemplateDraftError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static DocumentTemplateDraftError ActorRequired() => new(
        "X-Actor-Id is required to record who made this change (provenance)", "actor_required", 400);

    public static DocumentTemplateDraftError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static DocumentTemplateDraftError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);

    /// <summary>ZB-DOC011-01: the resolved, provenance-verified actor does not hold the permission this action requires.</summary>
    public static DocumentTemplateDraftError PermissionRequired(string permissionKey) => new(
        $"the asserted actor does not hold the required permission '{permissionKey}'", "permission_required", 403,
        new Dictionary<string, object?> { ["permission_key"] = permissionKey });
}
