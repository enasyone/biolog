using Biolog.Features.DeviceAdapters;

namespace Biolog.Features.DeviceAdapters.Tests;

/// <summary>Test double for <see cref="ICertificationGate"/> - the real translation from a
/// <c>DeviceCertificationService</c> lookup is proven separately, through the real host, in
/// <c>Biolog.IntegrationTests.DeviceAdapterEndpointsTests</c> (BIO-DEV-003-AC03). This double
/// isolates <see cref="DeviceAdapterService"/>'s own gating logic (AC02/AC03) from that
/// dependency.</summary>
public sealed class FakeCertificationGate(bool isSupported, string statusLabel = "Certified", string? detail = null)
    : ICertificationGate
{
    public int CallCount { get; private set; }

    public Task<CertificationGateResult> CheckAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(new CertificationGateResult(isSupported, statusLabel, detail));
    }
}
