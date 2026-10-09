namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>
/// This feature's own narrow seam for "is DeviceId a real, ACTIVE device in the resolved
/// tenant" (TD-CRD-006-DENY-LIST-DEVICE-AUTH) - deliberately NOT a <c>ProjectReference</c>, the
/// same BIO-IDN-002-QA-01-derived discipline <see cref="ICardIssueGateway"/> already applies to
/// its own cross-story dependency. Mirrors BIO-PHO-002's own <c>IDeviceExistenceGateway</c>
/// exactly. The real implementation is an adapter in
/// <c>Biolog.Api.Modules.BioCrd006Module</c> wrapping BIO-DEV-001's own already-registered
/// <c>DeviceService.GetAsync</c>, requiring <c>DeviceStatus.Active</c> - a retired or not-yet-
/// activated device must never authenticate a deny-list read.
///
/// <b>Tenant binding, for free.</b> <c>DeviceService.GetAsync</c> resolves the ambient tenant
/// itself (the same <c>ICurrentTenantProvider</c>/<c>ITenantContextAccessor</c> mechanism this
/// endpoint's own <c>RequireTenant()</c> uses) and looks the device up only within that tenant's
/// own device set - a caller who authenticates with a real device id/secret pair registered to
/// a DIFFERENT tenant than the one the request's ambient host resolves to gets a device that
/// does not exist in the resolved tenant, and therefore does not authenticate. This is the
/// remediation's "preserving tenant binding" requirement: it falls out of reusing the SAME
/// tenant-scoped lookup every other endpoint already trusts, not a new mechanism.
/// </summary>
public interface IDeviceExistenceGateway
{
    Task<bool> IsActiveAsync(string deviceId, CancellationToken cancellationToken = default);
}
