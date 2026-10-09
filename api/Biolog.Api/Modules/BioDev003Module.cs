using Biolog.Api.Features.DeviceCertification;
using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Biolog.Features.DeviceAdapters;
using Biolog.Features.DeviceAdapters.Adapters;

namespace Biolog.Api.Modules;

/// <summary>
/// BIO-DEV-003's vendor adapter framework, registered into this host via the same
/// thin-adapter shape every sibling separate-project feature uses (see
/// <c>Biolog.Features.DeviceAdapters.csproj</c>'s own comment for why the project itself
/// cannot reference <see cref="DeviceRegistry.DeviceService"/> or
/// <see cref="DeviceCertification.DeviceCertificationService"/> directly).
///
/// Endpoints live under the existing <c>/api/v1/devices</c> group
/// (<see cref="DeviceRegistryModule"/> owns registration/status; this module owns
/// capability discovery and health, BIO-DEV-003-AC02) - reading a device through
/// <see cref="DeviceService.GetAsync"/> exactly like every other tenant-scoped device read,
/// so the same fail-closed-on-missing-tenant and not-found-not-forbidden rules apply here
/// with no new code duplicating them.
///
/// Depends on <see cref="DeviceRegistryModule"/> (for <see cref="DeviceService"/>) and
/// <see cref="DeviceCertificationModule"/> (for <see cref="DeviceCertificationService"/>, via
/// <see cref="CertificationGateAdapter"/> below) in addition to <c>Tenant</c>.
/// </summary>
public sealed class BioDev003Module : IBiologModule
{
    public string Name => "DeviceAdapters";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "DeviceRegistry", "DeviceCertification"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDeviceAdapter, AcmeFingerprintScannerAdapter>();
        services.AddSingleton<IDeviceAdapter, NovaVisionFaceTerminalAdapter>();
        services.AddSingleton<IDeviceAdapter, OrbitIdNfcUhfReaderAdapter>();
        services.AddSingleton<IDeviceAdapter, PrintWorksCardPrinterAdapter>();
        services.AddSingleton<IDeviceAdapterRegistry, DeviceAdapterRegistry>();

        services.AddScoped<ICertificationGate>(
            sp => new CertificationGateAdapter(sp.GetRequiredService<DeviceCertificationService>()));

        services.AddScoped<DeviceAdapterService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/devices");

        group.MapGet("/{deviceId}/capabilities", async (
            string deviceId, DeviceService deviceService, DeviceAdapterService adapterService, CancellationToken cancellationToken) =>
        {
            var (device, problem) = await ResolveDeviceAsync(deviceId, deviceService, cancellationToken);
            if (problem is not null)
            {
                return problem;
            }

            try
            {
                var capabilities = await adapterService.DiscoverCapabilitiesAsync(ToDescriptor(device!), cancellationToken);
                return Results.Ok(capabilities);
            }
            catch (UnsupportedDeviceCombinationException ex)
            {
                return UnsupportedProblem(ex);
            }
            catch (DeviceAdapterNotAvailableException ex)
            {
                return AdapterUnavailableProblem(ex);
            }
        });

        group.MapGet("/{deviceId}/health", async (
            string deviceId, DeviceService deviceService, DeviceAdapterService adapterService, CancellationToken cancellationToken) =>
        {
            var (device, problem) = await ResolveDeviceAsync(deviceId, deviceService, cancellationToken);
            if (problem is not null)
            {
                return problem;
            }

            try
            {
                var health = await adapterService.CheckHealthAsync(ToDescriptor(device!), cancellationToken);
                return Results.Ok(health);
            }
            catch (UnsupportedDeviceCombinationException ex)
            {
                return UnsupportedProblem(ex);
            }
            catch (DeviceAdapterNotAvailableException ex)
            {
                return AdapterUnavailableProblem(ex);
            }
        });
    }

    private static async Task<(Device? Device, IResult? Problem)> ResolveDeviceAsync(
        string deviceId, DeviceService deviceService, CancellationToken cancellationToken)
    {
        try
        {
            var device = await deviceService.GetAsync(deviceId, cancellationToken);
            return device is null ? (null, Results.NotFound()) : (device, null);
        }
        catch (TenantContextMissingException ex)
        {
            return (null, Results.Problem(
                title: "tenant_context_missing", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }
    }

    private static DeviceDescriptor ToDescriptor(Device device) =>
        new(device.Vendor, device.Model, device.Firmware, device.Sdk, device.Serial);

    private static IResult UnsupportedProblem(UnsupportedDeviceCombinationException ex) => Results.Problem(
        title: "device_combination_unsupported",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?>
        {
            ["certification_status"] = ex.StatusLabel,
            ["certification_detail"] = ex.Detail,
        });

    private static IResult AdapterUnavailableProblem(DeviceAdapterNotAvailableException ex) => Results.Problem(
        title: "device_adapter_unavailable",
        detail: ex.Message,
        statusCode: StatusCodes.Status503ServiceUnavailable);

    /// <summary>
    /// BIO-DEV-003-AC03's real bridge: translates a resolved
    /// <see cref="DeviceCertification"/>'s <see cref="CertificationStatus"/> into
    /// <see cref="CertificationGateResult"/>. Everything except <c>CertificationStatus.Unsupported</c>
    /// and "no matrix entry at all" is treated as supported - <c>Deprecated</c>/<c>Experimental</c>
    /// still get a real capability/health answer (their own status word is not this AC's gate;
    /// only <c>Unsupported</c> is), matching <see cref="ICertificationGate"/>'s own doc.
    /// </summary>
    private sealed class CertificationGateAdapter(DeviceCertificationService certificationService) : ICertificationGate
    {
        public async Task<CertificationGateResult> CheckAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken)
        {
            var key = new DeviceCertificationKey(descriptor.Vendor, descriptor.Model, descriptor.Firmware, descriptor.Sdk);
            var certification = await certificationService.GetEffectiveAsync(key, cancellationToken);

            if (certification is null)
            {
                return new CertificationGateResult(IsSupported: false, StatusLabel: "NoCertificationRecord", Detail: null);
            }

            var isSupported = certification.Status != CertificationStatus.Unsupported;
            return new CertificationGateResult(isSupported, certification.Status.ToString(), certification.TestEvidenceReference);
        }
    }
}
