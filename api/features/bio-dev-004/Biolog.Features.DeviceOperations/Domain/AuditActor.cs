namespace Biolog.Features.DeviceOperations;

/// <summary>This project's own narrow copy of <c>Biolog.Api.Audit.AuditActor</c>'s shape -
/// who performed a heartbeat poll or remote job, carried through to the real shared audit
/// sink by <c>BioDev004Module</c>'s <see cref="Gateways.IRemoteJobAuditGateway"/>
/// implementation. Accepted directly from the caller, not derived from a verified session -
/// the same "hook, not resolver" gap BIO-DEV-001's own <c>DeviceService.ChangeStatusAsync</c>
/// already documents for actor identity, unchanged here.</summary>
public sealed record AuditActor(string ActorType, string ActorId);
