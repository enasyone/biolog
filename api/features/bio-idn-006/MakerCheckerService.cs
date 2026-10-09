using System.Text.RegularExpressions;
using Biolog.Features.Rbac.Api;
using Biolog.Features.Rbac.Audit;
using Biolog.Features.Rbac.Domain;
using Biolog.Features.Rbac.Identifiers;
using Biolog.Features.Rbac.Tenancy;

namespace Biolog.Features.Rbac;

/// <summary>
/// Application service for BIO-IDN-006-AC03: whether a sensitive action requires second
/// approval (<see cref="SensitiveActionRule"/>), and the maker-checker workflow itself
/// (<see cref="ApprovalRequest"/>). Implements the identical ADR-0003 Decision 7 audit-atomicity
/// discipline every mutation in this project applies.
///
/// <b>Separation of duties is this service's own responsibility to enforce, not merely
/// documented</b> - see <see cref="DecideAsync"/> and <see cref="ApprovalRequest"/>'s header.
/// This is the direct target of the story's mandatory privilege-escalation negative tests.
///
/// <b>Two different lock scopes, reasoned explicitly</b> (BIO-IDN-003-QA-01's lesson):
/// <see cref="SensitiveActionRuleLockRegistry"/> is keyed <c>(TenantId, ActionKey)</c> - a small
/// admin toggle, unconditional write, no invariant beyond that single key.
/// <see cref="ApprovalRequestLockRegistry"/> is keyed <c>(TenantId, ActionKey, TargetRef)</c> -
/// the SCOPE <see cref="ApprovalRequest"/>'s own "at most one Pending per target" invariant
/// spans, mirroring BIO-IDN-003's <c>InstitutionalIdentifierLockRegistry</c> shape exactly.
/// Neither scope ever needs to lock the other - a rule change and an approval-request write
/// touch disjoint keys.
///
/// <b>BIO-IDN-006-QA-04 (Seyi round 2, Critical).</b> <see cref="SetActionRuleAsync"/> now
/// requires the caller to hold <c>"rbac.sensitive_action_rule_manage"</c> - round 1 wired
/// maker-checker into `SUPER_ADMIN` mutation, but configuring the RULE ITSELF (including
/// disabling it) remained open to any asserted actor, letting an unprivileged caller defeat the
/// control before a legitimate administrator ever used it. See
/// <see cref="RoleAssignmentService"/>'s header for the full round 2 history.
///
/// <b>BIO-IDN-006-QA-05 (Seyi round 2, High).</b> <see cref="TryConsumeApprovalAsync"/> is the
/// atomic, single-use counterpart to <see cref="IsActionClearedAsync"/> (which remains a
/// read-only status check, unchanged) - see <see cref="ApprovalRequest"/>'s header "Consumption
/// is single-use".
///
/// <b>BIO-IDN-006-QA-07 (Seyi round 4, Critical).</b> Rounds 1-2 gated who may CONFIGURE
/// maker-checker (<see cref="SetActionRuleAsync"/>, QA-04) and who may MUTATE the SUPER_ADMIN
/// role once cleared (QA-01/QA-03), but left <see cref="RequestApprovalAsync"/> and
/// <see cref="DecideAsync"/> themselves open to any asserted actor - the only check was that the
/// maker and checker be two DIFFERENT ids (<see cref="ApprovalRequest"/>'s "Separation of
/// duties"). Two different but equally arbitrary, equally unauthorized asserted identities could
/// still create and approve a sensitive request between themselves, defeating the entire control:
/// a maker-checker gate is only as strong as the authority of its own participants, and
/// "different from each other" is not "genuinely authorized". <see cref="RequestApprovalAsync"/>
/// now requires <see cref="ApprovalRequestPermission"/>; <see cref="DecideAsync"/> now requires
/// <see cref="ApprovalDecidePermission"/> - both checked via the same
/// <see cref="PermissionService.ActorHasPermissionAsync"/> mechanism as every other gate in this
/// story, fail-closed by default except <c>SUPER_ADMIN</c> (implicit), so a tenant must
/// explicitly delegate participation in a sensitive workflow to a role before that role's holders
/// may act as a maker or a checker for it. The self-approval check remains, unchanged, as a
/// SEPARATE property of an otherwise-authorized checker, not a substitute for authority.
/// </summary>
internal sealed partial class MakerCheckerService(
    ICurrentTenantProvider tenantProvider,
    ISensitiveActionRuleRepository ruleRepository,
    IApprovalRequestRepository requestRepository,
    IRbacAuditSink auditSink,
    SensitiveActionRuleLockRegistry ruleLockRegistry,
    ApprovalRequestLockRegistry requestLockRegistry,
    PermissionService permissionService)
{
    /// <summary>BIO-IDN-006-QA-04: required to configure a sensitive-action rule.</summary>
    public const string SensitiveActionRuleManagePermission = "rbac.sensitive_action_rule_manage";

    /// <summary>BIO-IDN-006-QA-07: required to raise (make) a maker-checker approval request.</summary>
    public const string ApprovalRequestPermission = "rbac.approval_request";

    /// <summary>BIO-IDN-006-QA-07: required to decide (check) a maker-checker approval request.</summary>
    public const string ApprovalDecidePermission = "rbac.approval_decide";

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ActionKeyPattern();

    public async Task<SensitiveActionRule> SetActionRuleAsync(
        string actionKey, SetSensitiveActionRuleRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionService.ActorHasPermissionAsync(tenantId, actor.Id, SensitiveActionRuleManagePermission, cancellationToken))
        {
            throw RbacError.PermissionRequired(SensitiveActionRuleManagePermission);
        }

        var errors = new List<ValidationErrorItem>();
        var key = ValidateActionKey(actionKey, errors);
        if (request.RequiresSecondApproval is not bool requiresSecondApproval)
        {
            errors.Add(new ValidationErrorItem("requires_second_approval", "required", "requires_second_approval is required"));
            requiresSecondApproval = default;
        }

        ThrowIfInvalid(errors);

        using var _ = await ruleLockRegistry.AcquireAsync(tenantId, key!, cancellationToken);
        var current = await ruleRepository.GetAsync(tenantId, key!, cancellationToken);
        var now = UtcInstant.Now();
        var updated = new SensitiveActionRule(tenantId, key!) { RequiresSecondApproval = requiresSecondApproval, Version = (current?.Version ?? 0) + 1, CreatedAt = current?.CreatedAt ?? now, UpdatedAt = now };

        await AuditAsync(tenantId, actor, "rbac.sensitive_action_rule_set", "sensitive_action_rule", key!,
            requiresSecondApproval ? "second_approval_required" : "second_approval_not_required", correlationId, cancellationToken);
        return await ruleRepository.UpsertAsync(updated, CancellationToken.None);
    }

    public Task<SensitiveActionRule?> GetActionRuleAsync(string actionKey, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return ruleRepository.GetAsync(tenantId, actionKey, cancellationToken);
    }

    /// <summary>The maker's request. Rejected outright if a Pending request already exists for this exact (action_key, target_ref) - AC03's own structural invariant, checked here AND independently re-checked at the persistence boundary.</summary>
    public async Task<ApprovalRequest> RequestApprovalAsync(
        string actionKey, RequestApprovalRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionService.ActorHasPermissionAsync(tenantId, actor.Id, ApprovalRequestPermission, cancellationToken))
        {
            throw RbacError.PermissionRequired(ApprovalRequestPermission);
        }

        var errors = new List<ValidationErrorItem>();
        var key = ValidateActionKey(actionKey, errors);
        var targetRef = ValidateTargetRef(request.TargetRef, errors);
        var reason = ValidateReason(request.Reason, errors);
        ThrowIfInvalid(errors);

        using var _ = await requestLockRegistry.AcquireAsync(tenantId, key!, targetRef!, cancellationToken);
        var history = await requestRepository.ListHistoryAsync(tenantId, key!, targetRef!, cancellationToken);
        if (history.Any(h => h.Status == ApprovalRequestStatus.Pending))
        {
            throw RbacError.ApprovalRequestAlreadyPending(key!, targetRef!);
        }

        var now = UtcInstant.Now();
        var sequenceNumber = history.Count + 1;
        var approvalRequest = new ApprovalRequest(tenantId, key!, targetRef!, sequenceNumber)
        {
            Status = ApprovalRequestStatus.Pending, RequestedByActorId = actor.Id, RequestReason = reason!, Version = 1, CreatedAt = now, UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "rbac.approval_request", "approval_request", $"{key}/{targetRef}/{sequenceNumber}", "approval_requested", correlationId, cancellationToken);
        return await requestRepository.CreateAsync(approvalRequest, CancellationToken.None);
    }

    /// <summary>
    /// The checker's decision - AC03's core security property. Always rejects an attempt to
    /// decide an already-decided request (never a silent no-op: unlike a revoke/retire's
    /// idempotent "ensure this state" semantics, a decision carries content two racing callers
    /// could legitimately disagree on, so the loser must be told, not silently absorbed).
    /// Self-approval is checked here, immediately after reading the pending request, before any
    /// write is attempted - see ApprovalRequest's header.
    /// </summary>
    public async Task<ApprovalRequest> DecideAsync(
        string actionKey, string targetRef, int sequenceNumber, DecideApprovalRequest request, AuditActor actor, string correlationId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionService.ActorHasPermissionAsync(tenantId, actor.Id, ApprovalDecidePermission, cancellationToken))
        {
            throw RbacError.PermissionRequired(ApprovalDecidePermission);
        }

        var errors = new List<ValidationErrorItem>();
        if (!TryParseDecision(request.Decision, out var decision))
        {
            errors.Add(new ValidationErrorItem("decision", "invalid_format", "decision must be APPROVED or REJECTED"));
        }

        var reasonCode = ValidateReason(request.ReasonCode, errors, field: "reason_code");
        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            expectedVersion = default;
        }

        ThrowIfInvalid(errors);

        using var _ = await requestLockRegistry.AcquireAsync(tenantId, actionKey, targetRef, cancellationToken);
        var current = await requestRepository.GetAsync(tenantId, actionKey, targetRef, sequenceNumber, cancellationToken)
            ?? throw RbacError.ApprovalRequestNotFound(actionKey, targetRef, sequenceNumber);

        if (current.Status != ApprovalRequestStatus.Pending)
        {
            throw RbacError.ApprovalRequestAlreadyDecided(actionKey, targetRef, sequenceNumber, current.Status);
        }

        if (expectedVersion != current.Version)
        {
            throw RbacError.ApprovalRequestConcurrencyConflict(actionKey, targetRef, sequenceNumber, expectedVersion, current.Version);
        }

        // AC03's core security property: the checker may never be the maker. Checked
        // immediately after reading the current record, before any write is attempted - see
        // ApprovalRequest's header "Separation of duties".
        if (string.Equals(current.RequestedByActorId, actor.Id, StringComparison.Ordinal))
        {
            throw RbacError.SelfApprovalForbidden();
        }

        var now = UtcInstant.Now();
        var updated = current with
        {
            Status = decision, DecidedByActorId = actor.Id, DecisionReasonCode = reasonCode, DecidedAt = now, Version = current.Version + 1, UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "rbac.approval_decide", "approval_request", $"{actionKey}/{targetRef}/{sequenceNumber}",
            decision == ApprovalRequestStatus.Approved ? "approval_granted" : "approval_denied", correlationId, cancellationToken);

        // Guaranteed to succeed: the per-target lock held since entry means no other writer in
        // this process could have changed `current` since it was read. CancellationToken is
        // None: the audit event above is already confirmed durable (see class header).
        return await requestRepository.ReplaceAsync(current, updated, actor.Id, CancellationToken.None);
    }

    /// <summary>AC03's full history for one target, oldest first.</summary>
    public Task<IReadOnlyList<ApprovalRequest>> ListHistoryAsync(string actionKey, string targetRef, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return requestRepository.ListHistoryAsync(tenantId, actionKey, targetRef, cancellationToken);
    }

    /// <summary>
    /// Convenience gate: is this action currently CLEARED to proceed for this target? True when
    /// the tenant has not required second approval for this action key at all, or when the
    /// MOST RECENT request for this target was APPROVED. Never derived from anything but the
    /// latest record - a rejected or still-pending request never silently clears.
    /// </summary>
    public async Task<bool> IsActionClearedAsync(string actionKey, string targetRef, CancellationToken cancellationToken = default)
    {
        var rule = await GetActionRuleAsync(actionKey, cancellationToken);
        if (rule is null || !rule.RequiresSecondApproval)
        {
            return true;
        }

        var history = await ListHistoryAsync(actionKey, targetRef, cancellationToken);
        var latest = history.OrderByDescending(r => r.SequenceNumber).FirstOrDefault();
        return latest?.Status == ApprovalRequestStatus.Approved;
    }

    /// <summary>
    /// BIO-IDN-006-QA-05's fix: the REAL authorization-gate primitive (as opposed to
    /// <see cref="IsActionClearedAsync"/>'s read-only status check) - atomically consumes the
    /// most recent APPROVED, not-yet-consumed request for this target under the per-target lock,
    /// so a second concurrent or later attempt can never reuse the SAME approval. Returns
    /// <c>true</c> immediately (nothing to consume) when the tenant has not required second
    /// approval for this action at all - fail-open, matching <see cref="SensitiveActionRule"/>'s
    /// own default. Returns <c>false</c> when required but no unconsumed approval exists
    /// (pending, rejected, stale/superseded, or already consumed by an earlier mutation) - the
    /// caller must obtain a genuinely NEW approval (the next <see cref="ApprovalRequest.SequenceNumber"/>)
    /// before retrying.
    /// </summary>
    public async Task<bool> TryConsumeApprovalAsync(string actionKey, string targetRef, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var rule = await ruleRepository.GetAsync(tenantId, actionKey, cancellationToken);
        if (rule is null || !rule.RequiresSecondApproval)
        {
            return true;
        }

        using var _ = await requestLockRegistry.AcquireAsync(tenantId, actionKey, targetRef, cancellationToken);
        var history = await requestRepository.ListHistoryAsync(tenantId, actionKey, targetRef, cancellationToken);
        var latest = history.OrderByDescending(r => r.SequenceNumber).FirstOrDefault();
        if (latest is null || latest.Status != ApprovalRequestStatus.Approved || latest.ConsumedAt is not null)
        {
            return false;
        }

        var consumed = latest with { ConsumedAt = UtcInstant.Now(), Version = latest.Version + 1, UpdatedAt = UtcInstant.Now() };
        await requestRepository.ConsumeAsync(latest, consumed, CancellationToken.None);
        return true;
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string targetType, string targetId, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var target = new AuditTarget(targetType, targetId);
        var auditEvent = RbacAuditEventBuilder.Build(
            tenantId, actor, action, target, AuditOutcome.Allowed, reasonCode, correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)>(), withheld: []);

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
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw RbacError.AuditUnavailable();
        }

        if (!exists)
        {
            throw RbacError.AuditUnavailable();
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw RbacError.TenantContextMissing();

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw RbacError.Validation(errors);
        }
    }

    private static string? ValidateActionKey(string? value, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem("action_key", "required", "action_key is required"));
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 100 || !ActionKeyPattern().IsMatch(trimmed))
        {
            errors.Add(new ValidationErrorItem("action_key", "invalid_format", "action_key must be a namespaced verb like 'person.archive', at most 100 characters"));
            return null;
        }

        return trimmed;
    }

    private static string? ValidateTargetRef(string? value, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem("target_ref", "required", "target_ref is required"));
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 200)
        {
            errors.Add(new ValidationErrorItem("target_ref", "out_of_range", "target_ref must be at most 200 characters"));
            return null;
        }

        return trimmed;
    }

    private static string? ValidateReason(string? value, List<ValidationErrorItem> errors, string field = "reason")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem(field, "required", $"{field} is required"));
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 500)
        {
            errors.Add(new ValidationErrorItem(field, "out_of_range", $"{field} must be at most 500 characters"));
            return null;
        }

        return trimmed;
    }

    private static bool TryParseDecision(string? value, out ApprovalRequestStatus status)
    {
        switch (value)
        {
            case "APPROVED": status = ApprovalRequestStatus.Approved; return true;
            case "REJECTED": status = ApprovalRequestStatus.Rejected; return true;
            default: status = default; return false;
        }
    }
}
