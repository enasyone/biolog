namespace Biolog.Features.DeviceOperations;

/// <summary>BIO-DEV-004-AC03: "Firmware/config changes require audit." This project's own
/// narrow shape handed to <c>IRemoteJobAuditGateway</c>, translated by <c>BioDev004Module</c>
/// into a real <c>Biolog.Api.Audit.AuditEvent</c> recorded through the SAME shared
/// <c>IAuditSink</c> singleton <c>Features.DeviceRegistry.DeviceService</c> already writes
/// to - one real, shared audit trail, not a second parallel store nobody else reads.</summary>
public sealed record RemoteJobAuditRecord(
    string DeviceId,
    string TenantId,
    RemoteJobType JobType,
    string ActorType,
    string ActorId,
    string? CorrelationId,
    DateTimeOffset OccurredAt);
