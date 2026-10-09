namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// This feature's own narrow seam for "is DeviceId a real, ACTIVE device in the resolved
/// tenant" (BIO-PHO-002-AC02) - deliberately NOT a <c>ProjectReference</c>, even though
/// <c>api/Biolog.Api/Features/DeviceRegistry</c> is a public, in-host type this story COULD
/// reference directly: this project still owns no reference to any other feature's concrete
/// types, only to this interface, applying the identical BIO-IDN-002-QA-01-derived discipline
/// even where the language would not strictly force it. The real implementation is an adapter
/// in <c>Biolog.Api.Modules.BioPho002Module</c> wrapping BIO-DEV-001's own already-registered
/// <c>DeviceService.GetAsync</c>, requiring <c>DeviceStatus.Active</c> - a retired or not-yet-
/// activated device must never authenticate an upload.
/// </summary>
public interface IDeviceExistenceGateway
{
    Task<bool> IsActiveAsync(string deviceId, CancellationToken cancellationToken = default);
}
