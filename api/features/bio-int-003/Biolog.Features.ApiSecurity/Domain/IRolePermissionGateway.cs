namespace Biolog.Features.ApiSecurity;

/// <summary>
/// Security-review fix (Zainab, ZB-INT003-03, High - round 3): a service account's scopes are
/// role-shaped labels (<see cref="KnownRoles"/>), but a role label is NOT itself a domain
/// permission - whether a role carries a given permission is BIO-IDN-006's own tenant-scoped,
/// independently-configurable <c>RolePermissionGrant</c> fact (natural key
/// <c>(TenantId, Role, PermissionKey)</c>, fail-closed default: an absent grant means NOT
/// granted). Round 2's fix checked a live scope LABEL against itself (`token.Scopes.Contains(
/// "ReadOnly")`) - that proves the label survives revocation/narrowing, but never consults
/// whether "ReadOnly" actually carries the permission a protected resource requires, and a
/// tenant can never grant a NARROWER permission to a role without reassigning the whole label.
///
/// This seam resolves a ROLE (not a person) against that same live grant: <c>Biolog.Api</c>'s
/// <c>BioInt003Module</c> implements it by calling BIO-IDN-006's own
/// <c>IRolePermissionGrantRepository.GetAsync</c> DIRECTLY - not
/// <c>PermissionService.GetGrantAsync</c>, which resolves its tenant AMBIENTLY via
/// <c>ICurrentTenantProvider</c> (the Host-header channel every browser/admin request uses).
/// An OAuth2 bearer-token caller never goes through that channel - the TOKEN's own embedded
/// tenant id is the tenant, by design (see <see cref="ServiceAccountService"/>'s own remarks) -
/// and ADR-0002 Decision 3 makes <c>ITenantContextAccessor.Context</c> read-only precisely so no
/// consumer can swap the ambient tenant mid-request to work around this. The REPOSITORY layer,
/// unlike the SERVICE layer, already takes <c>tenantId</c> as an explicit parameter - exactly
/// the shape a non-Host-channel consumer like this one needs, with no change to BIO-IDN-006's
/// own code required.
/// </summary>
internal interface IRolePermissionGateway
{
    Task<bool> RoleHasPermissionAsync(string tenantId, string role, string permissionKey, CancellationToken cancellationToken = default);
}
