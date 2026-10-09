namespace Biolog.Features.ParserRegistry.Model;

/// <summary>
/// Which tenants are permitted to author their own parsers (BIO-DOC-005-AC01: "BTS and permitted
/// tenants can define parsers" - not every tenant automatically qualifies). This is a tenant-level
/// entitlement record (closer to ADR-0002's tenant-context/routing concern than to ADR-0006's
/// actor-identity concern - see <see cref="ParserCaller"/>'s remarks for why this story does not
/// attempt real actor authentication), granted or revoked by BTS out of band from this library's
/// own scope (a later provisioning/admin story's job; this type only records and checks the
/// resulting grant).
/// </summary>
public sealed class TenantParserAuthorityRegistry
{
    private readonly HashSet<string> _authorizedTenantIds = [];

    public void Grant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("must be a non-empty string", nameof(tenantId));
        }

        _authorizedTenantIds.Add(tenantId);
    }

    public void Revoke(string tenantId) => _authorizedTenantIds.Remove(tenantId);

    public bool IsAuthorized(string tenantId) => _authorizedTenantIds.Contains(tenantId);
}
