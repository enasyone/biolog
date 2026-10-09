namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>One attendance event's projection onto this feature's own whitelisted Attendance dimensions (BIO-RPT-002-AC01) - raw event fields only (mode/source/verification method), never the derived present/late/absent status, which would additionally require reading BIO-ATT-001's own <c>AttendancePolicy</c> - out of this foundation story's declared dependency scope.</summary>
public sealed record AttendanceRecord(string Mode, string Source, string VerificationMethod);

/// <summary>Real cross-story dependency (BIO-ATT-001). Implemented by an adapter in <c>BioRpt002Module</c> wrapping BIO-ATT-001's own already-registered, PUBLIC <c>IAttendanceEventRepository.ListForTenantAsync</c> directly.</summary>
public interface IAttendanceGateway
{
    Task<IReadOnlyList<AttendanceRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default);
}
