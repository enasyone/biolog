using Biolog.Features.Attendance.Domain;
using Biolog.Features.Attendance.Identifiers;
using Biolog.Features.Attendance.Tenancy;

namespace Biolog.Features.Attendance;

/// <summary>
/// AC02/AC04's read-time composition: fetches raw events for one person/date and the tenant's
/// current policy, then hands both to the pure <see cref="AttendanceStatusEvaluator"/>. Nothing
/// here writes anything - a derived status is computed fresh on every call, never cached or
/// stored, the same "compute in effect at read time, never auto-mutate" discipline BIO-IDN-002
/// established for time-based status.
/// </summary>
internal sealed class AttendanceStatusService(
    ICurrentTenantProvider tenantProvider,
    IAttendanceEventRepository eventRepository,
    IAttendancePolicyRepository policyRepository)
{
    public async Task<DerivedAttendanceStatus> GetDailyStatusAsync(string personId, string date, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantProvider.TenantId ?? throw AttendanceError.TenantContextMissing();

        var dayEvents = await eventRepository.ListForPersonOnDateAsync(tenantId, personId, date, cancellationToken);
        var policy = await policyRepository.GetAsync(tenantId, cancellationToken);

        return AttendanceStatusEvaluator.Evaluate(tenantId, personId, date, dayEvents, policy, UtcInstant.Now());
    }
}
