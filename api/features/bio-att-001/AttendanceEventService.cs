using Biolog.Features.Attendance.Api;
using Biolog.Features.Attendance.Audit;
using Biolog.Features.Attendance.Domain;
using Biolog.Features.Attendance.Identifiers;
using Biolog.Features.Attendance.Tenancy;

namespace Biolog.Features.Attendance;

/// <summary>
/// Application service for AC01/AC02/AC03: capturing and listing raw attendance events.
/// Implements ADR-0003 Decision 7's audit-atomicity pattern exactly as established across this
/// codebase's identity wave: a per-<c>(TenantId, IdempotencyKey)</c> lock
/// (<see cref="AttendanceEventLockRegistry"/>), audit-before-commit,
/// <see cref="IAttendanceAuditSink.ExistsAsync"/> verification on an ambiguous audit-write
/// throw, <see cref="CancellationToken.None"/> on the repository commit once the audit write is
/// confirmed durable.
///
/// <b>Capture and read stay open to any asserted actor (ADR-0006 Decision 2).</b> Unlike
/// <see cref="AttendancePolicyService"/> (an authorization decision - see its own header), event
/// capture and listing are audit-attribution uses only: every caller who can reach the tenant
/// context can already capture an event and list events for it, regardless of who they claim to
/// be, the same shape BIO-IDN-001 through BIO-IDN-005 establish for PERSON. A forged actor id
/// here changes who is blamed for a capture, not what the forger could already do - there is no
/// differential authority to escalate into, so Decision 2's boundary does not require this
/// endpoint to carry a provenance proof. See <c>docs/features/bio-att-001/README.md</c> "Why
/// capture and read stay open, and policy does not" for the full re-derivation (BIO-ACC-001's own
/// QA-01 lesson: a sibling story's precedent must be re-justified against what THIS story's own
/// data actually discloses, never pattern-matched on endpoint shape alone - attendance
/// presence/time data is comparable sensitivity to PERSON's own name/date-of-birth, not to
/// BIO-ACC-001's access-policy rule content).
/// </summary>
internal sealed class AttendanceEventService(
    ICurrentTenantProvider tenantProvider,
    IAttendanceEventRepository repository,
    IAttendanceAuditSink auditSink,
    AttendanceEventLockRegistry lockRegistry,
    IPersonExistenceGateway personGateway)
{
    public async Task<AttendanceEvent> CaptureAsync(
        CaptureAttendanceEventRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!AttendanceSourceExtensions.TryParse(request.Source, out var source))
        {
            throw AttendanceError.Validation([new ValidationErrorItem("source", "invalid", "source must be one of device, manual, import")]);
        }

        if (!AttendanceModeExtensions.TryParse(request.Mode, out var mode))
        {
            throw AttendanceError.Validation([new ValidationErrorItem("mode", "invalid", "mode must be one of check_in, check_out, presence")]);
        }

        if (!VerificationMethodExtensions.TryParse(request.Evidence.Method, out var evidenceMethod))
        {
            throw AttendanceError.Validation([new ValidationErrorItem("evidence.method", "invalid", "evidence.method must be one of biometric, credential, manual, unverified")]);
        }

        var evidence = new VerificationEvidence { Method = evidenceMethod, Verified = request.Evidence.Verified, Confidence = request.Evidence.Confidence };

        // Idempotency lock FIRST (AC03), before the person-existence gateway call: the lock
        // itself must serialize on the caller-controlled key regardless of what downstream
        // validation later rejects, or two concurrent identical-but-invalid retries could both
        // pass validation before either checks for the other.
        using var _ = await lockRegistry.AcquireAsync(tenantId, request.IdempotencyKey ?? string.Empty, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200)
        {
            throw AttendanceError.Validation([new ValidationErrorItem("idempotency_key", "invalid", "idempotency_key must be non-empty and at most 200 characters")]);
        }

        if (!await personGateway.ExistsAsync(request.PersonId, cancellationToken))
        {
            throw AttendanceError.PersonNotFound(request.PersonId);
        }

        var occurredAt = request.OccurredAt ?? throw AttendanceError.Validation([new ValidationErrorItem("occurred_at", "required", "occurred_at is required")]);

        var fingerprint = AttendanceEvent.ComputeFingerprint(
            tenantId, request.PersonId, source, request.DeviceId, request.Location, mode, evidence, occurredAt, request.IdempotencyKey);

        var existing = await repository.GetByIdempotencyKeyAsync(tenantId, request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.ContentFingerprint == fingerprint)
            {
                // AC03: identical redelivery - answered idempotently, no new event, no new audit entry.
                return existing;
            }

            throw AttendanceError.IdempotencyConflict(request.IdempotencyKey);
        }

        var now = UtcInstant.Now();
        var attendanceEvent = AttendanceEvent.Capture(
            tenantId, request.PersonId, source, request.DeviceId, request.Location, mode, evidence, occurredAt, now, request.IdempotencyKey);

        await AuditAsync(
            tenantId, actor, "attendance.event_captured", attendanceEvent.EventId, correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["mode"] = (mode.ToWireString(), "internal"),
                ["source"] = (source.ToWireString(), "internal"),
            },
            cancellationToken);

        return await repository.CreateAsync(attendanceEvent, CancellationToken.None);
    }

    public async Task<IReadOnlyList<AttendanceEvent>> ListForPersonOnDateAsync(string personId, string date, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.ListForPersonOnDateAsync(tenantId, personId, date, cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceEvent>> ListForTenantAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.ListForTenantAsync(tenantId, cancellationToken);
    }

    /// <summary>ADR-0003 Decision 7, element 4: a throw from <see cref="IAttendanceAuditSink.WriteAsync"/> does not by itself prove the event was never persisted - <see cref="IAttendanceAuditSink.ExistsAsync"/> resolves the ambiguity via ground truth before surfacing <c>audit_unavailable</c>.</summary>
    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string eventId, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = AttendanceAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("attendance_event", eventId), AuditOutcome.Allowed, "attendance_event_captured", correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw AttendanceError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw AttendanceError.TenantContextMissing();
}
