namespace Biolog.Features.DeviceOperations;

/// <summary>
/// This project's own, minimal, read-only copy of a real
/// <c>Features.DeviceRegistry.Device</c> (BIO-DEV-001) - field-for-field matching its own
/// names, so <c>BioDev004Module</c>'s <see cref="Gateways.IDeviceDirectoryGateway"/>
/// implementation can build one from a real <c>Device</c> with a straight field copy, the
/// same shape BIO-DEV-003's own <c>DeviceDescriptor</c> already established. <see cref="Status"/>
/// is carried as a plain string (BIO-DEV-001's own <c>DeviceStatus</c> lifecycle vocabulary
/// is display-only here - nothing in this project branches on its value, only
/// <see cref="Health"/> does), so no second lifecycle-status enum needs to exist in this
/// project at all.
/// </summary>
public sealed record DeviceSnapshot(
    string Id,
    string TenantId,
    string Vendor,
    string Model,
    string Firmware,
    string Sdk,
    string? Serial,
    string Status,
    DateTimeOffset? LastSeenAt,
    DeviceHealthStatus? Health,
    string? HealthDetail,
    DateTimeOffset? HealthCheckedAt,
    int Version);
