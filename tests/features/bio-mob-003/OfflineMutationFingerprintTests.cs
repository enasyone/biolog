// Developer tests for BIO-MOB-003. SYNTHETIC fixtures only.
//
// Regression coverage for BIO-MOB-003-QA-02 (Chioma, acceptance round 2): the idempotency
// fingerprint previously covered only the mutation's own content fields (mutation_id,
// aggregate_id, base_version, operation, payload), never the identity-bearing claims
// (tenant_id, device_id). OfflineMutationIntakeService.ComputeFingerprint now binds both in, as
// defense-in-depth alongside the explicit tenant/device cross-validation checks - see that
// method's own header for why this matters even though the current call order already rejects a
// mismatched claim earlier. Calls the real internal method directly (InternalsVisibleTo), not a
// re-implementation, so this proves what the service actually does.
using System.Text.Json;
using Biolog.Features.OfflineSync.Api;
using Xunit;

namespace Biolog.Features.OfflineSync.Tests;

public sealed class OfflineMutationFingerprintTests
{
    private static OfflineMutationDeltaRequest Request(string tenantId, string deviceId) => new()
    {
        MutationId = "evt_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b",
        TenantId = tenantId,
        DeviceId = deviceId,
        AggregateId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b",
        BaseVersion = 1,
        IdempotencyKey = "idem-fingerprint-test",
        OccurredAt = "2026-09-23T00:00:00.000Z",
        Operation = "ADDITION",
        Payload = JsonDocument.Parse("""{"field":"SYNTHETIC"}""").RootElement,
    };

    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string OtherTenantId = "ten_018f2e5a-5678-7abc-89ab-0123456789cd";
    private const string DeviceId = "dev_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string OtherDeviceId = "dev_018f2e5a-1111-7a41-9b2e-4f7c8d1a6e5b";

    [Fact]
    public void Two_requests_identical_except_for_tenant_id_produce_different_fingerprints()
    {
        var a = OfflineMutationIntakeService.ComputeFingerprint(Request(TenantId, DeviceId));
        var b = OfflineMutationIntakeService.ComputeFingerprint(Request(OtherTenantId, DeviceId));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Two_requests_identical_except_for_device_id_produce_different_fingerprints()
    {
        var a = OfflineMutationIntakeService.ComputeFingerprint(Request(TenantId, DeviceId));
        var b = OfflineMutationIntakeService.ComputeFingerprint(Request(TenantId, OtherDeviceId));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Two_requests_with_identical_content_and_claims_produce_the_same_fingerprint()
    {
        var a = OfflineMutationIntakeService.ComputeFingerprint(Request(TenantId, DeviceId));
        var b = OfflineMutationIntakeService.ComputeFingerprint(Request(TenantId, DeviceId));

        Assert.Equal(a, b);
    }
}
