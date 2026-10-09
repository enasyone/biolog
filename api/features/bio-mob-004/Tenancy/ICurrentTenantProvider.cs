namespace Biolog.Features.MobileVerification.Tenancy;

/// <summary>This feature's own local seam for reading the resolved ambient tenant - used only by the human-facing (browser/admin) device-role write route; the device-authenticated routes never use this, they resolve their tenant from <see cref="Domain.IDeviceTenantResolutionGateway"/> instead. Mirrors every sibling feature's own project-local copy of this interface.</summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
