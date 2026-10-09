namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class ReportError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public IReadOnlyList<Api.ValidationErrorItem>? Errors { get; init; }

    /// <summary>
    /// Takes the raw <see cref="ReportDataset"/> value, not its wire string: this is the one
    /// path reachable for an enum value the whitelist itself does not recognise (a valid
    /// <c>ReportDataset</c> member that <see cref="ReportSchemaRegistry"/> simply has no entry
    /// for yet, or - in principle, since a caller-supplied string can never reach this far, see
    /// <c>Api.ReportEndpoints.ParseDefinition</c>'s own upstream <c>TryParse</c> gate - an
    /// out-of-range value from a non-HTTP caller), so it must not itself call
    /// <see cref="ReportDatasetExtensions.ToWireString"/> (whose own exhaustive switch throws
    /// for exactly that case - caught by this feature's own developer tests, not assumed safe).
    /// </summary>
    public static ReportError UnknownDataset(ReportDataset dataset) => new(
        $"'{dataset}' is not a governed report dataset", "report_dataset_not_whitelisted", 422,
        new Dictionary<string, object?> { ["dataset"] = dataset.ToString() });

    public static ReportError DimensionNotWhitelisted(ReportDataset dataset, ReportDimension dimension) => new(
        $"dimension '{dimension.ToWireString()}' is not whitelisted for dataset '{dataset.ToWireString()}'", "report_dimension_not_whitelisted", 422,
        new Dictionary<string, object?> { ["dataset"] = dataset.ToWireString(), ["dimension"] = dimension.ToWireString() });

    public static ReportError MeasureNotWhitelisted(ReportDataset dataset, ReportMeasure measure) => new(
        $"measure '{measure.ToWireString()}' is not whitelisted for dataset '{dataset.ToWireString()}'", "report_measure_not_whitelisted", 422,
        new Dictionary<string, object?> { ["dataset"] = dataset.ToWireString(), ["measure"] = measure.ToWireString() });

    public static ReportError JobNotFound(string jobId) => new(
        $"no report job found for id '{jobId}'", "report_job_not_found", 404,
        new Dictionary<string, object?> { ["job_id"] = jobId });

    public static ReportError JobNotCompleted(string jobId, ReportJobStatus status) => new(
        $"report job '{jobId}' is {status.ToWireString()}, not completed - no result is available yet", "report_job_not_completed", 409,
        new Dictionary<string, object?> { ["job_id"] = jobId, ["status"] = status.ToWireString() });

    public static ReportError JobNotCancellable(string jobId, ReportJobStatus status) => new(
        $"report job '{jobId}' is already {status.ToWireString()} and cannot be cancelled", "report_job_not_cancellable", 409,
        new Dictionary<string, object?> { ["job_id"] = jobId, ["status"] = status.ToWireString() });

    /// <summary>AC02: the export "hook" exists for every format, but only CSV actually renders in this foundation story - Excel/PDF are a disclosed, documented HYBRID gap (docs/features/bio-rpt-002/README.md), not a silently-dead button.</summary>
    public static ReportError ExportFormatNotImplemented(ReportExportFormat format) => new(
        $"export format '{format}' is not yet implemented - CSV is the only live export in this foundation story", "report_export_format_not_implemented", 501,
        new Dictionary<string, object?> { ["format"] = format.ToString().ToLowerInvariant() });

    public static ReportError UnknownExportFormat(string? format) => new(
        $"'{format}' is not a recognised export format", "report_export_format_unknown", 422,
        new Dictionary<string, object?> { ["format"] = format });

    public static ReportError Validation(IReadOnlyList<Api.ValidationErrorItem> errors) => new(
        "the request failed validation", "validation_failed", 422)
    { Errors = errors };

    public static ReportError AuditUnavailable() => new(
        "the audit sink failed; the change was not committed without an audit record", "audit_unavailable", 503);

    public static ReportError ActorRequired() => new(
        "X-Actor-Id is required to record who requested this report", "actor_required", 400);

    public static ReportError InvalidActorType(string actorType) => new(
        "X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown", "invalid_actor_type", 400,
        new Dictionary<string, object?> { ["actor_type"] = actorType });

    public static ReportError TenantContextMissing() => new(
        "tenant_context_missing: no tenant channel has resolved a context for this request", "tenant_context_missing", 400);
}
