namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// AC05's own demo-seed write gateway, registered Singleton (see
/// <see cref="Demo.AttendanceSessionDemoBridgeService"/>'s own header for why this is a SEPARATE
/// class/interface from the ambient-tenant, Scoped <see cref="IAttendanceEventQueryGateway"/> - a
/// captive-dependency bug (Singleton depending on Scoped) that BIO-TRN-002's own build already
/// caught and fixed once, applied here from the start). Wraps BIO-ATT-001's own already-registered
/// (internal, <c>InternalsVisibleTo Biolog.Api</c>), Singleton
/// <c>IAttendanceEventRepository</c> directly - both this gateway's implementation and that
/// repository are Singleton, so there is no captive-dependency risk at all.
///
/// A real, disclosed exception to "gateways are read-only" (<see cref="CaptureDemoCheckInAsync"/>
/// genuinely writes into BIO-ATT-001's own event store) - justified the same way
/// BIO-TRN-002's own <c>ITransportDemoSeedProvisioner</c> is: the demo bridge has no other way to
/// produce evidence for a roster view to classify, since it cannot resolve a real
/// <c>ICurrentTenantProvider</c>-bound tenant context for a headerless UI POST.
/// </summary>
public interface IAttendanceDemoSeedGateway
{
    Task<AttendanceEvidenceRecord> CaptureDemoCheckInAsync(string tenantId, string personId, string deviceId, string location, string date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceEvidenceRecord>> ListForPersonOnDateAsync(string tenantId, string personId, string date, CancellationToken cancellationToken = default);
}
