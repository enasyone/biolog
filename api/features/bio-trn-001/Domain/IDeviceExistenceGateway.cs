namespace Biolog.Features.Transport.Domain;

/// <summary>
/// This feature's own narrow, read-only seam onto BIO-DEV-001 (device registry), the same
/// gateway-not-<c>ProjectReference</c> shape as <see cref="IPersonExistenceGateway"/>. Used for
/// AC03's <see cref="VehicleRecord.DeviceId"/> linkage - existence only, deliberately NOT status-gated
/// (a not-yet-<c>Active</c> device can still be pre-paired with a vehicle during inventory
/// setup - see <see cref="VehicleRecord"/>'s own header). <c>Biolog.Api</c>'s <c>BioTrn001Module</c>
/// implements this by wrapping BIO-DEV-001's own already-registered
/// <c>Biolog.Api.Features.DeviceRegistry.DeviceService.GetAsync</c> directly - BIO-DEV-001 is not
/// a story-keyed feature project (it lives inside <c>Biolog.Api</c> itself), so this gateway is
/// this feature's ONLY coupling to it, exactly the same discipline as every cross-project gateway
/// elsewhere in this codebase.
/// </summary>
public interface IDeviceExistenceGateway
{
    Task<bool> ExistsAsync(string deviceId, CancellationToken cancellationToken = default);
}
