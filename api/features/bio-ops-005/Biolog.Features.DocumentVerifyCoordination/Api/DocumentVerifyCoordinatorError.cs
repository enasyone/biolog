namespace Biolog.Features.DocumentVerifyCoordination.Api;

/// <summary>
/// This feature's domain error, shaped identically to <c>AuthenticityAssessmentError</c>/
/// <c>DocumentTemplatePackError</c> (ADR-0004 S2.1: "(message, code, status, detail) ... without
/// any of them extending a common base").
/// </summary>
public sealed class DocumentVerifyCoordinatorError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public static DocumentVerifyCoordinatorError TenantContextMissing() => new(
        "no tenant could be resolved for this request", "tenant_context_missing", 400);

    /// <summary>Point 4 of Kemi's ruling: every step after <c>capture</c> resolves the session
    /// from the <c>document_session_id</c> cookie - a missing/expired/foreign-tenant cookie is a
    /// well-formed, expected client error, never a 500.</summary>
    public static DocumentVerifyCoordinatorError NoActiveSession() => new(
        "no active document-verify session cookie was presented, or it names a session that does not exist for this tenant - call capture first",
        "no_active_session", 409);
}
