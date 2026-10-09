using System.Text.Json.Serialization;

namespace Biolog.Api.Features.DeviceCertification;

/// <summary>
/// One certification matrix entry (BIO-DEV-002-AC01): a device+SDK+firmware combination's
/// supported/certified/experimental/deprecated/unsupported status, the exact firmware and
/// SDK versions it applies to, and a reference to the test evidence behind that status.
/// Immutable snapshot, matching <see cref="Features.DeviceRegistry.Device"/>'s own pattern
/// (ADR-0001 Decision 3): a mutation produces a new snapshot with <see cref="Version"/>
/// incremented by exactly 1, never an in-place edit.
///
/// <see cref="TenantId"/> is <c>null</c> for the platform-wide default entry every tenant
/// sees unless it has its own, and a real tenant id for that one tenant's own override
/// (BIO-DEV-002-AC03) - the same "null means control-plane, a real value means one
/// tenant's own record" shape ADR-0003's audit-event schema already uses for the same
/// reason (see <c>TenantAuditEvent.TenantId</c>, BIO-TEN-001).
///
/// <see cref="TestEvidenceReference"/> is a pointer to wherever the real test report
/// lives (a document id, a URL, a ticket key) - never the evidence content itself; this
/// story records a reference, not a file store, the same "reference, not the material
/// itself" shape <see cref="Features.DeviceRegistry.Device.CredentialReference"/> already
/// established for a different field.
/// </summary>
public sealed record DeviceCertification
{
    public required string Vendor { get; init; }
    public required string Model { get; init; }
    public required string Firmware { get; init; }
    public required string Sdk { get; init; }
    public string? TenantId { get; init; }
    public required CertificationStatus Status { get; init; }
    public required string TestEvidenceReference { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Convenience for internal code; not part of the wire contract - the flat
    /// Vendor/Model/Firmware/Sdk fields above already carry this, and duplicating it as a
    /// nested object in every JSON response would be redundant.</summary>
    [JsonIgnore]
    public DeviceCertificationKey Key => new(Vendor, Model, Firmware, Sdk);
}
