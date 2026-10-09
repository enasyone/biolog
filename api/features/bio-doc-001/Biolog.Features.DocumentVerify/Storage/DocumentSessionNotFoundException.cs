namespace Biolog.Features.DocumentVerify.Storage;

/// <summary>
/// Thrown for both "no such session" and "session belongs to a different tenant" - the same
/// exception, deliberately, for both. ADR-0002 §4.3: "a denied cross-tenant access answers
/// 'not found', never 403" - a 403 (or a distinct exception a caller could tell apart) would
/// confirm the record exists under another tenant, turning any session id into an existence
/// oracle. This store has no HTTP layer of its own yet, but the invariant belongs at the data
/// boundary regardless of what sits above it, so a later story's HTTP mapping starts from a
/// store that already fails closed rather than having to retrofit the distinction away.
/// </summary>
public sealed class DocumentSessionNotFoundException : InvalidOperationException
{
    public string TenantId { get; }
    public string SessionId { get; }

    public DocumentSessionNotFoundException(string tenantId, string sessionId)
        : base($"document_session_not_found: '{sessionId}'")
    {
        TenantId = tenantId;
        SessionId = sessionId;
    }
}
