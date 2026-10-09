using Biolog.Features.WorkforceAttendance.Api;
using Biolog.Features.WorkforceAttendance.Audit;
using Biolog.Features.WorkforceAttendance.Domain;
using Biolog.Features.WorkforceAttendance.Identifiers;
using Biolog.Features.WorkforceAttendance.Tenancy;

namespace Biolog.Features.WorkforceAttendance;

/// <summary>
/// Application service for AC02 ("clock pairs are validated"), AC03 ("overtime is derived, not
/// raw-event mutation") and AC04 ("exceptions require audit reason"). <see cref="GetDailySummaryAsync"/>
/// is a read: BIO-ATT-001's own raw clock events (<see cref="IAttendanceEventGateway"/>) plus a
/// named <see cref="WorkforceShift"/> go in; <see cref="ClockPairEvaluator"/> and
/// <see cref="OvertimeEvaluator"/> derive the result - nothing here is ever persisted back onto
/// a raw event. <see cref="CreateAdjustmentAsync"/> is this feature's other authorization
/// decision (ADR-0006 Decision 1/2): it overrides what every subsequent summary read shows as a
/// person's effective overtime for one date, the same two-tier never-mutate shape
/// BIO-ATT-002's own <c>AttendanceAdjustmentService</c> established.
/// </summary>
internal sealed class WorkforceAttendanceService(
    ICurrentTenantProvider tenantProvider,
    IAttendanceEventGateway eventGateway,
    IWorkforceShiftRepository shiftRepository,
    IWorkforceAttendanceAdjustmentRepository adjustmentRepository,
    IWorkforceAttendanceAuditSink auditSink,
    IPermissionGateway permissionGateway,
    IPersonExistenceGateway personGateway)
{
    public const string AdjustmentManagePermission = "workforce_attendance.adjustment_manage";

    public async Task<WorkforceDailySummary> GetDailySummaryAsync(string personId, string date, string shiftId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!CanonicalId.IsPersonId(personId))
        {
            throw WorkforceAttendanceError.Validation([new ValidationErrorItem("person_id", "invalid_format", "person_id must be a canonical per_ identifier")]);
        }

        if (!await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw WorkforceAttendanceError.PersonNotFound(personId);
        }

        var shift = await shiftRepository.GetAsync(tenantId, shiftId, cancellationToken) ?? throw WorkforceAttendanceError.ShiftNotFound(shiftId);

        var dayEvents = await eventGateway.ListForPersonOnDateAsync(personId, date, cancellationToken);
        var pairs = ClockPairEvaluator.Evaluate(dayEvents);
        var computedOvertimeMinutes = OvertimeEvaluator.EvaluateMinutes(pairs, shift);
        var interpretation = AttendanceInterpretationEvaluator.Evaluate(dayEvents, shift);

        var adjustment = await adjustmentRepository.GetLatestAsync(tenantId, personId, date, cancellationToken);

        return new WorkforceDailySummary
        {
            TenantId = tenantId,
            PersonId = personId,
            Date = date,
            ShiftId = shift.ShiftId,
            Pairs = pairs,
            ComputedOvertimeMinutes = computedOvertimeMinutes,
            Interpretation = interpretation,
            Adjustment = adjustment,
        };
    }

    public async Task<WorkforceAttendanceAdjustment> CreateAdjustmentAsync(
        string personId, string date, CreateWorkforceAttendanceAdjustmentRequest request, Audit.AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, AdjustmentManagePermission, cancellationToken))
        {
            throw WorkforceAttendanceError.PermissionRequired(AdjustmentManagePermission);
        }

        if (!CanonicalId.IsPersonId(personId))
        {
            throw WorkforceAttendanceError.Validation([new ValidationErrorItem("person_id", "invalid_format", "person_id must be a canonical per_ identifier")]);
        }

        if (!await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw WorkforceAttendanceError.PersonNotFound(personId);
        }

        var shift = await shiftRepository.GetAsync(tenantId, request.ShiftId, cancellationToken) ?? throw WorkforceAttendanceError.ShiftNotFound(request.ShiftId);

        var dayEvents = await eventGateway.ListForPersonOnDateAsync(personId, date, cancellationToken);
        var pairs = ClockPairEvaluator.Evaluate(dayEvents);
        var originalOvertimeMinutes = OvertimeEvaluator.EvaluateMinutes(pairs, shift);

        // AC04's own "original" is whatever the evaluator currently computes - captured fresh
        // here, never itself mutated by this write.
        var adjustment = WorkforceAttendanceAdjustment.Create(
            tenantId, personId, date, originalOvertimeMinutes, request.AdjustedOvertimeMinutes, request.Reason, actor.Id, UtcInstant.Now());

        await AuditAsync(tenantId, actor, adjustment, correlationId, cancellationToken);

        return await adjustmentRepository.CreateAsync(adjustment, CancellationToken.None);
    }

    private async Task AuditAsync(string tenantId, Audit.AuditActor actor, WorkforceAttendanceAdjustment adjustment, string correlationId, CancellationToken cancellationToken)
    {
        var auditEvent = WorkforceAttendanceAuditEventBuilder.Build(
            tenantId, actor, "workforce_attendance.adjustment_created", new AuditTarget("workforce_attendance_adjustment", adjustment.AdjustmentId), AuditOutcome.Allowed,
            "workforce_attendance_adjustment_created", correlationId, causationId: null,
            new Dictionary<string, (object?, string)>
            {
                ["person_id"] = (adjustment.PersonId, "internal"),
                ["date"] = (adjustment.Date, "internal"),
                ["original_overtime_minutes"] = (adjustment.OriginalOvertimeMinutes, "internal"),
                ["adjusted_overtime_minutes"] = (adjustment.AdjustedOvertimeMinutes, "internal"),
                ["reason"] = (adjustment.Reason, "internal"),
            });

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None);
        }
        catch (Exception)
        {
            throw WorkforceAttendanceError.AuditUnavailable();
        }

        if (!exists)
        {
            throw WorkforceAttendanceError.AuditUnavailable();
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw WorkforceAttendanceError.TenantContextMissing();
}

/// <summary>AC02/AC03/AC04 combined read: a person's one date, fully derived, plus the latest adjustment (if any) layered visibly on top - never hidden, never merged away.</summary>
public sealed record WorkforceDailySummary
{
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required string Date { get; init; }
    public required string ShiftId { get; init; }
    public required IReadOnlyList<WorkforceClockPair> Pairs { get; init; }
    public required int ComputedOvertimeMinutes { get; init; }
    public required WorkforceAttendanceInterpretation Interpretation { get; init; }
    public WorkforceAttendanceAdjustment? Adjustment { get; init; }

    /// <summary>The overtime figure a caller should actually act on: the latest adjustment's value if one exists, otherwise the freshly computed one - AC04's own "an adjustment overrides what every subsequent read shows," never silently averaged or merged with the original.</summary>
    public int EffectiveOvertimeMinutes => Adjustment?.AdjustedOvertimeMinutes ?? ComputedOvertimeMinutes;
}
