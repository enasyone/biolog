namespace Biolog.Features.AuditInvestigation.Domain;

/// <summary>
/// The one genuine cross-story runtime dependency this story declares (BIO-IDN-006). Implemented
/// by an adapter over <c>Biolog.Features.Rbac.PermissionService.HasPermissionAsync</c> in
/// <c>BioRpt003Module</c> - never referenced directly. No tenant id parameter: the wrapped service
/// resolves the SAME ambient tenant context this feature's own request already established
/// (<c>TenantContextMiddleware</c> runs once, before either feature's services do).
/// </summary>
public interface IAuditExportPermissionGateway
{
    Task<bool> HasPermissionAsync(string personId, string permissionKey, CancellationToken cancellationToken = default);
}

/// <summary>The two permission keys this story checks (BIO-IDN-006's own free-form, namespaced-verb permission-key shape - this story does not own or enumerate every permission the whole system defines).</summary>
public static class AuditPermissionKeys
{
    /// <summary>BIO-RPT-003-AC04's second gate: without this, a viewer's personal/sensitive-classified detail fields are withheld at read time, even though the producing feature already decided they were safe to capture.</summary>
    public const string ViewSensitiveDetail = "audit.view_sensitive_detail";

    /// <summary>BIO-RPT-003-AC03: "Export is permissioned."</summary>
    public const string Export = "audit.export";
}
