namespace Biolog.Features.DeviceOperations;

/// <summary>BIO-DEV-004-AC03: "Firmware/config changes require audit." Implemented by
/// <c>BioDev004Module</c> against the real shared <c>Biolog.Api.Audit.IAuditSink</c>.</summary>
public interface IRemoteJobAuditGateway
{
    Task RecordAsync(RemoteJobAuditRecord record, CancellationToken cancellationToken);
}
