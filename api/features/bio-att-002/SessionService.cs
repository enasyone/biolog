using Biolog.Features.AttendanceSessions.Api;
using Biolog.Features.AttendanceSessions.Audit;
using Biolog.Features.AttendanceSessions.Domain;
using Biolog.Features.AttendanceSessions.Identifiers;
using Biolog.Features.AttendanceSessions.Tenancy;

namespace Biolog.Features.AttendanceSessions;

/// <summary>
/// Application service for AC01: creating a session and revising its roster. Implements ADR-0003
/// Decision 7's audit-atomicity pattern exactly as established across this codebase's identity
/// wave: a per-<c>(TenantId, SessionId)</c> lock (<see cref="SessionLockRegistry"/>) on the roster
/// update path, audit-before-commit, <see cref="ISessionAuditSink.ExistsAsync"/> verification on
/// an ambiguous audit-write throw, <see cref="CancellationToken.None"/> on the repository commit
/// once the audit write is confirmed durable.
///
/// <b>Session/roster CRUD stays open to any asserted actor (ADR-0006 Decision 2).</b> Unlike
/// <see cref="SessionAttendancePolicyService"/> and <see cref="AttendanceAdjustmentService"/> (this
/// feature's two authorization decisions - see their own headers), creating a session or revising
/// who is on its roster is an audit-attribution use only: every caller who can reach the tenant
/// context can already do this, regardless of who they claim to be - the same shape BIO-TRN-001's
/// own Route/Vehicle/Stop CRUD establishes (only the one genuine authorization-bearing action per
/// story gets an ADR-0006 gate).
/// </summary>
internal sealed class SessionService(
    ICurrentTenantProvider tenantProvider,
    ISessionRepository repository,
    ISessionAuditSink auditSink,
    SessionLockRegistry lockRegistry,
    IPersonExistenceGateway personGateway)
{
    public async Task<Session> CreateAsync(CreateSessionRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!SessionTypeExtensions.TryParse(request.SessionType, out var sessionType))
        {
            throw SessionError.Validation([new ValidationErrorItem("session_type", "invalid", "session_type must be one of lecture, class, exam, hostel, event")]);
        }

        await EnsureRosterPersonsExistAsync(request.RosterPersonIds, cancellationToken);

        var now = UtcInstant.Now();
        var session = Session.Create(tenantId, sessionType, request.Title, request.ScheduledDate, request.ScheduledStartTime, request.RosterPersonIds, actor.Id, now);

        await AuditAsync(tenantId, actor, "attendance_session.created", session.SessionId, correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["session_type"] = (sessionType.ToWireString(), "internal"),
                ["roster_size"] = (session.RosterPersonIds.Count, "internal"),
            },
            cancellationToken);

        return await repository.CreateAsync(session, CancellationToken.None);
    }

    public async Task<Session> UpdateRosterAsync(string sessionId, UpdateSessionRosterRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        await EnsureRosterPersonsExistAsync(request.RosterPersonIds, cancellationToken);

        using var _ = await lockRegistry.AcquireAsync(tenantId, sessionId, cancellationToken);

        var current = await repository.GetAsync(tenantId, sessionId, cancellationToken) ?? throw SessionError.SessionNotFound(sessionId);
        if (current.Version != request.ExpectedVersion)
        {
            throw SessionError.ConcurrencyConflict("session", request.ExpectedVersion);
        }

        var updated = current.UpdateRoster(request.RosterPersonIds, actor.Id, UtcInstant.Now());

        await AuditAsync(tenantId, actor, "attendance_session.roster_updated", sessionId, correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["roster_size"] = (updated.RosterPersonIds.Count, "internal"),
                ["version"] = (updated.Version, "internal"),
            },
            cancellationToken);

        if (!await repository.TrySaveAsync(updated, current.Version, CancellationToken.None))
        {
            throw SessionError.ConcurrencyConflict("session", current.Version);
        }

        return updated;
    }

    public async Task<Session?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<Session>> ListForTenantAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.ListByTenantAsync(tenantId, cancellationToken);
    }

    /// <summary>AC01 - every roster member must reference a person who genuinely exists in the resolved tenant, the same BIO-IDN-002-QA-01 lesson every identity-wave story applies.</summary>
    private async Task EnsureRosterPersonsExistAsync(IReadOnlyList<string> rosterPersonIds, CancellationToken cancellationToken)
    {
        foreach (var personId in rosterPersonIds.Distinct(StringComparer.Ordinal))
        {
            if (!await personGateway.ExistsAsync(personId, cancellationToken))
            {
                throw SessionError.PersonNotFound(personId);
            }
        }
    }

    /// <summary>ADR-0003 Decision 7, element 4: a throw from <see cref="ISessionAuditSink.WriteAsync"/> does not by itself prove the event was never persisted - <see cref="ISessionAuditSink.ExistsAsync"/> resolves the ambiguity via ground truth before surfacing <c>audit_unavailable</c>.</summary>
    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string sessionId, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = SessionAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("attendance_session", sessionId), AuditOutcome.Allowed, action.Replace('.', '_'), correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw SessionError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw SessionError.TenantContextMissing();
}
