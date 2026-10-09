namespace Biolog.Features.DocumentTemplatePack.Api;

/// <summary>
/// This feature's domain error, shaped identically to <c>AuthenticityAssessmentError</c>/
/// <c>PersonError</c> (ADR-0004 S2.1: "(message, code, status, detail) ... without any of them
/// extending a common base").
/// </summary>
public sealed class DocumentTemplatePackError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public static DocumentTemplatePackError NoPublishedPack() => new(
        "no governed document template pack has been published yet", "pack_not_published", 503);

    public static DocumentTemplatePackError AuditUnavailable() => new(
        "the audit sink is currently unavailable - no verification was recorded", "audit_unavailable", 503);
}
