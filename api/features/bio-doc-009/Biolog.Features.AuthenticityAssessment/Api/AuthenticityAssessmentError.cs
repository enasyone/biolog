namespace Biolog.Features.AuthenticityAssessment.Api;

/// <summary>
/// This feature's domain error, shaped identically to <c>PersonError</c>/<c>PhotoBoothError</c>
/// (ADR-0004 S2.1: "(message, code, status, detail) ... without any of them extending a common
/// base").
/// </summary>
public sealed class AuthenticityAssessmentError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public static AuthenticityAssessmentError ActorRequired() => new(
        "this action requires the caller to name itself via X-Actor-Id", "actor_required", 400);

    public static AuthenticityAssessmentError AssessmentNotFound(string documentSessionId) => new(
        $"no authenticity assessment exists yet for document session: {documentSessionId}", "assessment_not_found", 404,
        new Dictionary<string, object?> { ["document_session_id"] = documentSessionId });

    public static AuthenticityAssessmentError ConcurrencyConflict(string documentSessionId, int expected, int actual) => new(
        "the assessment was modified since the version this request expected", "concurrency_conflict", 409,
        new Dictionary<string, object?> { ["document_session_id"] = documentSessionId, ["expected_version"] = expected, ["actual_version"] = actual });

    public static AuthenticityAssessmentError Validation(string field, string reason) => new(
        $"validation failed: {field} {reason}", "validation_error", 422,
        new Dictionary<string, object?> { ["field"] = field, ["reason"] = reason });

    public static AuthenticityAssessmentError AuditUnavailable() => new(
        "the audit sink is currently unavailable - no assessment was recorded", "audit_unavailable", 503);
}
