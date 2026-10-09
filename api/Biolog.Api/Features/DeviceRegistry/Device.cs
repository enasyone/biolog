using Biolog.Features.DeviceAdapters;

namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>
/// A device record (BIO-DEV-001-AC01): the tenant-plane <c>dev_</c> aggregate ADR-0001
/// §1.2 already registers, for readers, terminals, scanners, printers and edge devices
/// before any vendor adapter exists. Immutable snapshot, matching ADR-0001 Decision 3's
/// "clients echo <see cref="Version"/>, they do not compute it" - a mutation produces a new
/// snapshot with <see cref="Version"/> incremented by exactly 1, never an in-place edit.
///
/// <see cref="CredentialReference"/> is BIO-DEV-001-AC02: an opaque pointer to wherever a
/// device's real credential (a certificate, a shared secret) actually lives - never the
/// credential material itself. Nothing in this feature resolves it to a secret value;
/// that belongs to whatever secret store a later story wires up.
///
/// <see cref="LastSeenAt"/> was reserved by this story but deliberately left unpopulated -
/// no story before BIO-DEV-004 ever wrote to it. <see cref="Health"/>/
/// <see cref="HealthDetail"/>/<see cref="HealthCheckedAt"/> are BIO-DEV-004-AC01's own
/// addition: a device's last recorded health-poll snapshot. <see cref="DeviceHealthStatus"/>
/// is reused directly from BIO-DEV-003's own vendor-adapter framework (already a real
/// <c>ProjectReference</c> of this host project - see <c>BioDev003Module</c>) rather than a
/// second, parallel vocabulary for the identical concept this feature itself persists but
/// BIO-DEV-003 only ever computed on demand.
/// </summary>
public sealed record Device
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string Serial { get; init; }
    public required string Vendor { get; init; }
    public required string Model { get; init; }
    public required string Firmware { get; init; }
    public required string Sdk { get; init; }
    public required string Site { get; init; }
    public required NetworkMetadata Network { get; init; }
    public required DeviceStatus Status { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }
    public DeviceHealthStatus? Health { get; init; }
    public string? HealthDetail { get; init; }
    public DateTimeOffset? HealthCheckedAt { get; init; }
    public string? CredentialReference { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
