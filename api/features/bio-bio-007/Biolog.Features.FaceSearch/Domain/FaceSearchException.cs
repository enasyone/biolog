namespace Biolog.Features.FaceSearch.Domain;

/// <summary>Domain-level error codes this feature raises. Plain strings, not an HTTP status -
/// this project has no ASP.NET dependency, the same deferred boundary every prior BIO-BIO story
/// documents.</summary>
public sealed class FaceSearchException : InvalidOperationException
{
    public string Code { get; }

    private FaceSearchException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>AC02: the resolved tenant has not explicitly enabled 1:N face search.</summary>
    public static FaceSearchException FeatureNotEnabledForTenant(string tenantId) =>
        new("face_search_feature_not_enabled", $"tenant '{tenantId}' has not explicitly enabled 1:N face search.");

    /// <summary>BIO-BIO-007-QA-01: the claimed identity has no current BIO-BIO-003-approved face
    /// reference to index - a genuine precondition failure, never a fallback to an unapproved
    /// raw sample.</summary>
    public static FaceSearchException NoApprovedReference(string personId) =>
        new("face_search_no_approved_reference", $"person '{personId}' has no current approved face reference to index into the search gallery.");

    /// <summary>AC05: the audit write could not be confirmed durable - a search this story
    /// cannot honestly claim happened without a trail must not silently succeed.</summary>
    public static FaceSearchException AuditUnavailable() =>
        new("face_search_audit_unavailable", "the search audit event could not be confirmed durable.");
}
