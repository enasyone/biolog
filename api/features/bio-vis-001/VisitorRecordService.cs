using Biolog.Features.Visitors.Api;
using Biolog.Features.Visitors.Audit;
using Biolog.Features.Visitors.Domain;
using Biolog.Features.Visitors.Identifiers;
using Biolog.Features.Visitors.Tenancy;

namespace Biolog.Features.Visitors;

/// <summary>
/// Application service for AC01 (preregistration + optional PERSON link), AC02 (host approval,
/// auditable), AC03 (configurable fields, enforced at check-in) and AC04 (automatic expiry, read
/// time). Implements ADR-0003 Decision 7's audit-atomicity pattern exactly as established across
/// this codebase: a per-<c>(TenantId, VisitorId)</c> lock (<see cref="VisitorRecordLockRegistry"/>),
/// audit-before-commit, <see cref="IVisitorAuditSink.ExistsAsync"/> verification on an ambiguous
/// audit-write throw, <see cref="CancellationToken.None"/> on the repository commit once the
/// audit write is confirmed durable.
///
/// <b><see cref="ApproveAsync"/>/<see cref="DenyAsync"/> are this feature's authorization
/// decisions (ADR-0006 Decision 1/2)</b> - deciding who gets physical premises access requires
/// <see cref="DecidePermission"/> via <see cref="IPermissionGateway"/> AND a verified ADR-0006
/// Decision 3 provenance proof (<see cref="Api.ActorProvenanceGate"/>).
/// <see cref="PreregisterAsync"/>/<see cref="GetAsync"/>/<see cref="ListAsync"/>/
/// <see cref="CheckInAsync"/>/<see cref="CheckOutAsync"/> and the evidence-capture methods
/// (NDA/safety-briefing/watchlist-result/temporary-credential) stay open to any asserted actor -
/// audit-attribution only, re-derived (not pattern-matched) exactly as
/// <c>docs/features/bio-vis-001/README.md</c> "Why preregistration/check-in/check-out stay open,
/// and approval/config do not" explains: a preregistered visitor's own contact/vehicle/watchlist
/// data is comparable sensitivity to BIO-ATT-001's presence data and PERSON's own name/DOB, not to
/// BIO-ACC-001's access-policy rule content.
/// </summary>
internal sealed class VisitorRecordService(
    ICurrentTenantProvider tenantProvider,
    IVisitorRecordRepository repository,
    IVisitorConfigRepository configRepository,
    IVisitorAuditSink auditSink,
    VisitorRecordLockRegistry lockRegistry,
    IPersonExistenceGateway personGateway,
    IPermissionGateway permissionGateway)
{
    public const string DecidePermission = "visitor.approve";

    public async Task<VisitorRecord> PreregisterAsync(
        PreregisterVisitorRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var errors = new List<ValidationErrorItem>();

        var fullName = ValidateRequired(request.FullName, "full_name", 200, errors);
        var hostPersonId = request.HostPersonId;
        if (string.IsNullOrWhiteSpace(hostPersonId))
        {
            errors.Add(new ValidationErrorItem("host_person_id", "required", "host_person_id is required"));
        }
        else if (!CanonicalId.IsPersonId(hostPersonId))
        {
            errors.Add(new ValidationErrorItem("host_person_id", "invalid_format", "host_person_id must be a canonical per_ identifier"));
        }

        if (request.PersonId is not null && !CanonicalId.IsPersonId(request.PersonId))
        {
            errors.Add(new ValidationErrorItem("person_id", "invalid_format", "person_id must be a canonical per_ identifier when present"));
        }

        var contactEmail = ValidateOptional(request.ContactEmail, "contact_email", 254, errors);
        var contactPhone = ValidateOptional(request.ContactPhone, "contact_phone", 32, errors);
        var vehiclePlate = ValidateOptional(request.VehiclePlate, "vehicle_plate", 32, errors);

        ThrowIfInvalid(errors);

        if (request.PersonId is not null && !await personGateway.ExistsAsync(request.PersonId, cancellationToken))
        {
            throw VisitorError.PersonNotFound(request.PersonId);
        }

        if (!await personGateway.ExistsAsync(hostPersonId!, cancellationToken))
        {
            throw VisitorError.PersonNotFound(hostPersonId!);
        }

        var config = await configRepository.GetAsync(tenantId, cancellationToken);
        var now = UtcInstant.Now();
        var expiresAt = ResolveExpiresAt(request.ExpiresAt, config, now, errors);
        ThrowIfInvalid(errors);

        var record = VisitorRecord.Preregister(
            tenantId, request.PersonId, fullName!, contactEmail, contactPhone, hostPersonId!, vehiclePlate,
            request.TemporaryCredentialRequested, expiresAt, now);

        using var _ = await lockRegistry.AcquireAsync(tenantId, record.Id, cancellationToken);

        await AuditAsync(tenantId, actor, "visitor.preregistered", record.Id, "visitor_preregistered", correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["host_approval_status"] = (record.HostApprovalStatus.ToWireString(), "internal"),
                ["visit_status"] = (record.VisitStatus.ToWireString(), "internal"),
            },
            cancellationToken);

        return await repository.CreateAsync(record, CancellationToken.None);
    }

    public async Task<VisitorRecord> GetAsync(string visitorId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, visitorId, cancellationToken) ?? throw VisitorError.NotFound(visitorId);
    }

    public Task<IReadOnlyList<VisitorRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListByTenantAsync(tenantId, cancellationToken);
    }

    public Task<VisitorRecord> ApproveAsync(string visitorId, VersionedVisitorRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        DecideAsync(visitorId, request.Version, actor, correlationId,
            (current, now) => current.Approve(actor.Id, now), "visitor.approved", "visitor_approved", cancellationToken);

    public async Task<VisitorRecord> DenyAsync(string visitorId, DenyVisitorRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw VisitorError.Validation([new ValidationErrorItem("reason", "required", "reason is required when denying host approval")]);
        }

        return await DecideAsync(visitorId, request.Version, actor, correlationId,
            (current, now) => current.Deny(actor.Id, request.Reason!.Trim(), now), "visitor.denied", "visitor_denied", cancellationToken);
    }

    private async Task<VisitorRecord> DecideAsync(
        string visitorId, int? requestedVersion, AuditActor actor, string correlationId,
        Func<VisitorRecord, string, VisitorRecord> transition, string action, string reasonCode, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, DecidePermission, cancellationToken))
        {
            throw VisitorError.PermissionRequired(DecidePermission);
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, visitorId, cancellationToken);
        var current = await repository.GetAsync(tenantId, visitorId, cancellationToken) ?? throw VisitorError.NotFound(visitorId);

        if (requestedVersion is not int expectedVersion)
        {
            throw VisitorError.Validation([new ValidationErrorItem("version", "required", "version is required so a stale write can be detected")]);
        }

        if (expectedVersion != current.Version)
        {
            throw VisitorError.ConcurrencyConflict(visitorId, expectedVersion, current.Version);
        }

        if (current.HostApprovalStatus != HostApprovalStatus.Pending)
        {
            throw VisitorError.ApprovalAlreadyDecided(visitorId, current.HostApprovalStatus);
        }

        var updated = transition(current, UtcInstant.Now());

        await AuditAsync(tenantId, actor, action, visitorId, reasonCode, correlationId,
            new Dictionary<string, (object?, string)> { ["host_approval_status"] = (updated.HostApprovalStatus.ToWireString(), "internal") },
            cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<VisitorRecord> CheckInAsync(string visitorId, VersionedVisitorRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(tenantId, visitorId, cancellationToken);
        var current = await repository.GetAsync(tenantId, visitorId, cancellationToken) ?? throw VisitorError.NotFound(visitorId);
        RequireVersion(visitorId, request.Version, current.Version);

        if (current.HostApprovalStatus != HostApprovalStatus.Approved)
        {
            throw VisitorError.CheckInBlocked(visitorId, "not_approved", "host approval has not been granted");
        }

        if (current.VisitStatus != VisitStatus.Preregistered)
        {
            throw VisitorError.CheckInBlocked(visitorId, "already_checked_in_or_out", $"visit is already {current.VisitStatus.ToWireString()}");
        }

        var now = UtcInstant.Now();
        if (VisitorStatusEvaluator.EffectiveStatus(current, now) == EffectiveVisitStatus.Expired)
        {
            throw VisitorError.CheckInBlocked(visitorId, "expired", $"this preregistration expired at {current.ExpiresAt}");
        }

        // Proactive safety rule, not merely a configurable AC03 field: a flagged watchlist
        // result blocks check-in unconditionally - a tenant's config governs which evidence is
        // REQUIRED before check-in, never whether a positive flag can be configured away.
        if (current.WatchlistScreeningResult == WatchlistScreeningResult.Flagged)
        {
            throw VisitorError.CheckInBlocked(visitorId, "watchlist_flagged", "this visitor is flagged on watchlist screening");
        }

        var config = await configRepository.GetAsync(tenantId, cancellationToken);
        if (config.RequireVehiclePlate && string.IsNullOrWhiteSpace(current.VehiclePlate))
        {
            throw VisitorError.CheckInBlocked(visitorId, "vehicle_plate_required", "this tenant requires a vehicle plate before check-in");
        }

        if (config.RequireNda && !current.NdaAccepted)
        {
            throw VisitorError.CheckInBlocked(visitorId, "nda_required", "this tenant requires NDA acceptance before check-in");
        }

        if (config.RequireSafetyBriefing && !current.SafetyBriefingCompleted)
        {
            throw VisitorError.CheckInBlocked(visitorId, "safety_briefing_required", "this tenant requires safety briefing completion before check-in");
        }

        if (config.RequireWatchlistScreening && current.WatchlistScreeningResult == WatchlistScreeningResult.NotScreened)
        {
            throw VisitorError.CheckInBlocked(visitorId, "watchlist_screening_required", "this tenant requires watchlist screening before check-in");
        }

        var updated = current.CheckIn(now);
        await AuditAsync(tenantId, actor, "visitor.checked_in", visitorId, "visitor_checked_in", correlationId, EmptyDetail, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<VisitorRecord> CheckOutAsync(string visitorId, VersionedVisitorRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(tenantId, visitorId, cancellationToken);
        var current = await repository.GetAsync(tenantId, visitorId, cancellationToken) ?? throw VisitorError.NotFound(visitorId);
        RequireVersion(visitorId, request.Version, current.Version);

        if (current.VisitStatus != VisitStatus.CheckedIn)
        {
            throw VisitorError.CheckOutBlocked(visitorId, $"visit is {current.VisitStatus.ToWireString()}, not checked-in");
        }

        var updated = current.CheckOut(UtcInstant.Now());
        await AuditAsync(tenantId, actor, "visitor.checked_out", visitorId, "visitor_checked_out", correlationId, EmptyDetail, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<VisitorRecord> RecordNdaAcceptanceAsync(string visitorId, VersionedVisitorRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(tenantId, visitorId, cancellationToken);
        var current = await repository.GetAsync(tenantId, visitorId, cancellationToken) ?? throw VisitorError.NotFound(visitorId);
        RequireVersion(visitorId, request.Version, current.Version);

        var updated = current.RecordNdaAcceptance(UtcInstant.Now());
        if (updated.Version == current.Version)
        {
            return current; // already accepted: no-op, nothing to audit
        }

        await AuditAsync(tenantId, actor, "visitor.nda_accepted", visitorId, "visitor_nda_accepted", correlationId, EmptyDetail, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<VisitorRecord> RecordSafetyBriefingAsync(string visitorId, VersionedVisitorRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(tenantId, visitorId, cancellationToken);
        var current = await repository.GetAsync(tenantId, visitorId, cancellationToken) ?? throw VisitorError.NotFound(visitorId);
        RequireVersion(visitorId, request.Version, current.Version);

        var updated = current.RecordSafetyBriefing(UtcInstant.Now());
        if (updated.Version == current.Version)
        {
            return current;
        }

        await AuditAsync(tenantId, actor, "visitor.safety_briefing_completed", visitorId, "visitor_safety_briefing_completed", correlationId, EmptyDetail, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<VisitorRecord> RecordWatchlistResultAsync(string visitorId, RecordWatchlistResultRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        if (!WatchlistScreeningResultExtensions.TryParse(request.Result, out var result))
        {
            throw VisitorError.Validation([new ValidationErrorItem("result", "invalid", "result must be one of not_screened, clear, flagged")]);
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, visitorId, cancellationToken);
        var current = await repository.GetAsync(tenantId, visitorId, cancellationToken) ?? throw VisitorError.NotFound(visitorId);
        RequireVersion(visitorId, request.Version, current.Version);

        var updated = current.RecordWatchlistResult(result);
        if (updated.Version == current.Version)
        {
            return current;
        }

        await AuditAsync(tenantId, actor, "visitor.watchlist_result_recorded", visitorId, "visitor_watchlist_result_recorded", correlationId,
            new Dictionary<string, (object?, string)> { ["result"] = (result.ToWireString(), "internal") }, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<VisitorRecord> IssueTemporaryCredentialAsync(string visitorId, VersionedVisitorRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(tenantId, visitorId, cancellationToken);
        var current = await repository.GetAsync(tenantId, visitorId, cancellationToken) ?? throw VisitorError.NotFound(visitorId);
        RequireVersion(visitorId, request.Version, current.Version);

        if (current.HostApprovalStatus != HostApprovalStatus.Approved)
        {
            throw VisitorError.TemporaryCredentialBlocked(visitorId, "host approval has not been granted");
        }

        if (!current.TemporaryCredentialRequested)
        {
            throw VisitorError.TemporaryCredentialBlocked(visitorId, "no temporary credential was requested at preregistration");
        }

        var config = await configRepository.GetAsync(tenantId, cancellationToken);
        if (!config.AllowTemporaryCredential)
        {
            throw VisitorError.TemporaryCredentialBlocked(visitorId, "this tenant does not allow temporary credentials");
        }

        var updated = current.IssueTemporaryCredential();
        if (updated.Version == current.Version)
        {
            return current; // already issued: no-op, nothing to audit
        }

        await AuditAsync(tenantId, actor, "visitor.temporary_credential_issued", visitorId, "visitor_temporary_credential_issued", correlationId, EmptyDetail, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    private static readonly Dictionary<string, (object?, string)> EmptyDetail = [];

    private static void RequireVersion(string visitorId, int? requested, int actual)
    {
        if (requested is not int expected)
        {
            throw VisitorError.Validation([new ValidationErrorItem("version", "required", "version is required so a stale write can be detected")]);
        }

        if (expected != actual)
        {
            throw VisitorError.ConcurrencyConflict(visitorId, expected, actual);
        }
    }

    private static string ResolveExpiresAt(string? requested, VisitorTenantConfig config, string now, List<ValidationErrorItem> errors)
    {
        if (!UtcInstant.TryParse(now, out var nowInstant))
        {
            throw new InvalidOperationException("unreachable: UtcInstant.Now() always renders a value UtcInstant.TryParse accepts");
        }

        if (string.IsNullOrWhiteSpace(requested))
        {
            return UtcInstant.Render(nowInstant.AddHours(config.DefaultVisitDurationHours));
        }

        if (!UtcInstant.TryParse(requested, out var requestedInstant))
        {
            errors.Add(new ValidationErrorItem("expires_at", "invalid_format", "expires_at must be an RFC 3339 UTC instant"));
            return now;
        }

        var maxAllowed = nowInstant.AddHours(config.MaxVisitDurationHours);
        if (requestedInstant > maxAllowed)
        {
            throw VisitorError.ExpiresAtExceedsMax(config.MaxVisitDurationHours);
        }

        if (requestedInstant <= nowInstant)
        {
            errors.Add(new ValidationErrorItem("expires_at", "out_of_range", "expires_at must be in the future"));
        }

        return requested;
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string visitorId, string reasonCode, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = VisitorAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("visitor_record", visitorId), AuditOutcome.Allowed, reasonCode, correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw VisitorError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw VisitorError.TenantContextMissing();

    private static string? ValidateRequired(string? value, string field, int maxLength, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem(field, "required", $"{field} is required"));
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            errors.Add(new ValidationErrorItem(field, "out_of_range", $"{field} must be at most {maxLength} characters"));
            return null;
        }

        return trimmed;
    }

    private static string? ValidateOptional(string? value, string field, int maxLength, List<ValidationErrorItem> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem(field, "invalid_format", $"{field} must not be blank when present"));
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            errors.Add(new ValidationErrorItem(field, "out_of_range", $"{field} must be at most {maxLength} characters"));
            return null;
        }

        return trimmed;
    }

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw VisitorError.Validation(errors);
        }
    }
}
