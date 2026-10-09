using Biolog.Features.DeviceAdapters;
using Biolog.Features.DeviceAdapters.Adapters;
using Xunit;

namespace Biolog.Features.DeviceAdapters.Tests;

/// <summary>BIO-DEV-003-AC02 (capability discovery and health) and AC03 (unsupported
/// firmware/SDK combinations surface certification status), exercised against
/// <see cref="DeviceAdapterService"/> directly with a fake certification gate so this file
/// proves the ORCHESTRATION logic - gate-before-adapter, never the reverse - independent of
/// the real certification matrix (proven separately through the real host in
/// <c>Biolog.IntegrationTests.DeviceAdapterEndpointsTests</c>).</summary>
public sealed class DeviceAdapterServiceTests
{
    private static readonly DeviceDescriptor Acme = new("Acme", "FS-100", "2.3.1", "sdk-4.0", "SN-ONLINE-1");
    private static readonly DeviceDescriptor AcmeOffline = new("Acme", "FS-100", "2.3.1", "sdk-4.0", "SN-OFFLINE-1");
    private static readonly DeviceDescriptor AcmeDegraded = new("Acme", "FS-100", "2.3.1", "sdk-4.0", "SN-DEGRADED-1");
    private static readonly DeviceDescriptor UnknownVendor = new("Unknown", "X", "1.0.0", "sdk-1");

    private static DeviceAdapterRegistry SupportedRegistry() => new([new AcmeFingerprintScannerAdapter()]);

    [Fact]
    public async Task AC02_discovers_capabilities_for_a_certified_combination()
    {
        var service = new DeviceAdapterService(SupportedRegistry(), new FakeCertificationGate(isSupported: true));

        var capabilities = await service.DiscoverCapabilitiesAsync(Acme, CancellationToken.None);

        Assert.Equal(DeviceCategory.FingerprintScanner, capabilities.Category);
        Assert.Contains(DeviceOperation.CaptureFingerprint, capabilities.Operations);
        Assert.Contains(DeviceOperation.VerifyFingerprint, capabilities.Operations);
    }

    [Fact]
    public async Task AC02_reports_online_health_for_a_healthy_device()
    {
        var service = new DeviceAdapterService(SupportedRegistry(), new FakeCertificationGate(isSupported: true));

        var health = await service.CheckHealthAsync(Acme, CancellationToken.None);

        Assert.Equal(DeviceHealthStatus.Online, health.Status);
        Assert.NotNull(health.Latency);
    }

    [Fact]
    public async Task AC02_reports_offline_health_when_the_simulated_device_is_unreachable()
    {
        var service = new DeviceAdapterService(SupportedRegistry(), new FakeCertificationGate(isSupported: true));

        var health = await service.CheckHealthAsync(AcmeOffline, CancellationToken.None);

        Assert.Equal(DeviceHealthStatus.Offline, health.Status);
        Assert.Null(health.Latency);
    }

    [Fact]
    public async Task AC02_reports_degraded_health_for_elevated_latency()
    {
        var service = new DeviceAdapterService(SupportedRegistry(), new FakeCertificationGate(isSupported: true));

        var health = await service.CheckHealthAsync(AcmeDegraded, CancellationToken.None);

        Assert.Equal(DeviceHealthStatus.Degraded, health.Status);
    }

    [Fact]
    public async Task AC03_an_unsupported_combination_throws_before_any_adapter_is_reached_and_carries_the_certification_status()
    {
        var gate = new FakeCertificationGate(isSupported: false, statusLabel: "Unsupported", detail: "evidence://none");
        var service = new DeviceAdapterService(SupportedRegistry(), gate);

        var ex = await Assert.ThrowsAsync<UnsupportedDeviceCombinationException>(
            () => service.DiscoverCapabilitiesAsync(Acme, CancellationToken.None));

        Assert.Equal("Unsupported", ex.StatusLabel);
        Assert.Equal("evidence://none", ex.Detail);
    }

    [Fact]
    public async Task AC03_the_certification_gate_is_checked_before_the_registry_is_even_consulted()
    {
        // A registry that would throw if Resolve() were ever called - proves the gate runs
        // first, not merely that both eventually run.
        var throwingRegistry = new ThrowIfResolvedRegistry();
        var gate = new FakeCertificationGate(isSupported: false, statusLabel: "Unsupported");
        var service = new DeviceAdapterService(throwingRegistry, gate);

        await Assert.ThrowsAsync<UnsupportedDeviceCombinationException>(
            () => service.CheckHealthAsync(Acme, CancellationToken.None));

        Assert.False(throwingRegistry.WasResolved);
    }

    [Fact]
    public async Task certified_but_no_registered_adapter_throws_a_distinct_not_available_exception()
    {
        var service = new DeviceAdapterService(new DeviceAdapterRegistry([]), new FakeCertificationGate(isSupported: true));

        await Assert.ThrowsAsync<DeviceAdapterNotAvailableException>(
            () => service.DiscoverCapabilitiesAsync(UnknownVendor, CancellationToken.None));
    }

    private sealed class ThrowIfResolvedRegistry : IDeviceAdapterRegistry
    {
        public bool WasResolved { get; private set; }

        public IDeviceAdapter? Resolve(DeviceDescriptor descriptor)
        {
            WasResolved = true;
            throw new InvalidOperationException("Resolve() should never be reached when the certification gate fails.");
        }
    }
}
