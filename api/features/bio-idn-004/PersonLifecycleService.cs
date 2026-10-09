using Biolog.Features.PersonLifecycle.Api;
using Biolog.Features.PersonLifecycle.Audit;
using Biolog.Features.PersonLifecycle.Domain;
using Biolog.Features.PersonLifecycle.Identifiers;
using Biolog.Features.PersonLifecycle.Tenancy;

namespace Biolog.Features.PersonLifecycle;

/// <summary>
/// Application service for BIO-IDN-004. Registered Scoped, like every sibling story's own
/// service, for the identical reason: this service reads a per-request
/// <see cref="ICurrentTenantProvider"/>, so it cannot be Singleton - the lock table lives in its
/// own Singleton class, <see cref="PersonLifecycleLockRegistry"/>.
///
/// <b>AC01 - explicit transitions.</b> <see cref="TransitionAsync"/> validates the requested
/// <c>(from, to)</c> pair against <see cref="LifecycleTransitionPolicy"/> - a closed, explicit
/// whitelist - BEFORE ever calling <see cref="IPersonLifecycleGateway"/>. An unlisted pair is
/// rejected fast as <c>transition_not_allowed</c>, never forwarded.
///
/// <b>AC02 - emergency suspension emits a credential/access revocation event.</b> When the
/// requested transition is <see cref="LifecycleTransitionPolicy.IsEmergencySuspension"/>
/// (ACTIVE -&gt; SUSPENDED, the only pair that qualifies), a
/// <see cref="CredentialAccessRevocationEvent"/> is published to
/// <see cref="ICredentialAccessRevocationEventSink"/> - see this class's header "Suspension/
/// revocation atomicity" for exactly when, and why that took three review rounds to get right.
///
/// <b>AC03 - reactivation requires permission and a reason.</b> When the requested transition is
/// <see cref="LifecycleTransitionPolicy.IsReactivation"/> (SUSPENDED -&gt; ACTIVE, the only pair
/// that qualifies - the initial VERIFIED -&gt; ACTIVE activation is NOT a reactivation and needs
/// neither), <paramref name="assertedPermission"/> must equal
/// <see cref="PermissionResolver.ReactivatePersonPermission"/> and <c>request.Reason</c> must be
/// non-blank, or the request is rejected before the gateway is ever called. Reactivation is
/// deliberately NEVER blocked by a pending revocation reconciliation from an earlier suspension
/// - see "Suspension/revocation atomicity" for why that would be scope creep with its own
/// operational risk, and why it does not need to be blocked for the pending fact to stay safe.
///
/// <b>AC04 - archived records remain traceable.</b> <see cref="ListEventsAsync"/> returns every
/// lifecycle event this feature has ever recorded for a person, unconditionally - there is no
/// special case that hides or prunes history once a person reaches ARCHIVED, and
/// <see cref="GetSnapshotAsync"/> keeps returning the record too (BIO-IDN-001's own repository
/// never deletes a PERSON; ARCHIVED is a terminal status, not a tombstone).
///
/// <b>Audit ordering for an ORDINARY transition.</b> BIO-IDN-002/003 write their own audit event
/// BEFORE their own repository commit, because in THOSE stories the "commit" is a same-process,
/// uncontested in-memory dictionary write with no real ambiguous-throw window once the audit is
/// confirmed. Here, the "commit" is <see cref="IPersonLifecycleGateway.ChangeStatusAsync"/> - a
/// call into ANOTHER feature's already-audited, already-locked service, itself synchronous and
/// unambiguous. So for an ordinary transition, <see cref="TransitionAsync"/> calls the gateway
/// FIRST and writes this feature's own supplementary lifecycle event AFTER confirming success -
/// there is nothing to roll back if that write then fails, because the underlying status change
/// already durably happened regardless. An <c>audit_unavailable</c> from this service therefore
/// means the change committed, but this feature's own supplementary record of it could not be
/// confirmed - see <see cref="PersonLifecycleError.AuditUnavailable"/>.
///
/// <b>Suspension/revocation atomicity - the four-round history.</b> AC02 requires a suspension
/// to ALWAYS have a revocation event; the status commit and the revocation publish are two
/// INDEPENDENT writes to two DIFFERENT stores with no shared transaction, so true atomicity is
/// not achievable outright.
/// <list type="bullet">
/// <item>Round 1 committed the status change first, then published. A publish failure left the
/// person genuinely SUSPENDED with no revocation event and no safe retry - a silent,
/// un-closeable gap. FAIL.</item>
/// <item>Round 2 reversed it: publish first, commit second. A REJECTED commit (e.g. a stale
/// version - an ordinary, expected outcome, not a rare fault) then left a false revocation event
/// for a suspension that never happened. FAIL - "an accepted trade-off" does not satisfy AC02,
/// since reversing the order of two independent writes can only ever protect one direction of
/// inconsistency at the other's expense.</item>
/// <item>Round 3 committed first (closing round 2's finding) and, on a publish failure, made the
/// gap an EXPLICITLY MODELLED state - but derived "is reconciliation pending" from the person's
/// CURRENT status being SUSPENDED. That broke the moment a permissioned reactivation succeeded:
/// the status-derived check silently started reporting "not pending," and the only
/// reconciliation path (a SUSPENDED -&gt; SUSPENDED request) became unreachable - the missing
/// revocation became permanently unreported and unrecoverable. FAIL - the same class of gap as
/// round 1, reached by a different path.</item>
/// <item>Round 4 introduced <see cref="IPendingSuspensionReconciliationStore"/>, tracking each
/// pending suspension as its OWN durable record, never derived from current status - but the
/// record was marked AFTER the ordinary supplementary lifecycle audit write, not before. An
/// audit-sink outage at that point aborted the method (as <c>audit_unavailable</c>) before the
/// pending record was ever created - the person was left SUSPENDED with no revocation event AND
/// no pending record to find, the gap undetectable and unrepairable. FAIL - the same class of
/// gap as rounds 1 and 3, reached by a third path.</item>
/// <item>This version: <see cref="TransitionAsync"/> marks the pending record the VERY FIRST
/// thing after the gateway commit for a suspension, before EITHER of the two writes that can
/// throw (the ordinary lifecycle audit event and the revocation publish) is even attempted. Both
/// of those writes are then attempted INDEPENDENTLY of one another's outcome - a failure in one
/// never prevents the other from being attempted - with the revocation publish's own failure
/// surfaced with priority, since it is AC02's own security-critical write.
/// <see cref="ReconcilePendingRevocationsAsync"/> remains a SEPARATE operation, reachable
/// regardless of the person's current status, that closes every outstanding pending entry.
/// <see cref="HasPendingRevocationsAsync"/> still reports the durable fact directly, not a
/// status-derived approximation of it.</item>
/// </list>
/// </summary>
internal sealed class PersonLifecycleService(
    ICurrentTenantProvider tenantProvider,
    IPersonLifecycleGateway gateway,
    IPersonLifecycleAuditSink auditSink,
    ICredentialAccessRevocationEventSink revocationSink,
    IPendingSuspensionReconciliationStore pendingStore,
    PersonLifecycleLockRegistry lockRegistry)
{
    private const int MaxReasonLength = 500;

    public async Task<PersonSnapshot> TransitionAsync(
        string personId, TransitionRequest request, string? assertedPermission, AuditActor actor, string correlationId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var errors = new List<ValidationErrorItem>();
        if (!CanonicalId.IsPersonId(personId))
        {
            errors.Add(new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier"));
        }

        var targetStatus = ValidateTargetStatus(request.TargetStatus, errors);
        var version = ValidateVersion(request.Version, errors);
        var reason = ValidateReason(request.Reason, errors);
        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

        var snapshot = await gateway.GetSnapshotAsync(personId, cancellationToken) ?? throw PersonLifecycleError.PersonNotFound(personId);
        var fromStatus = snapshot.Status;

        if (!LifecycleTransitionPolicy.IsAllowed(fromStatus, targetStatus))
        {
            throw PersonLifecycleError.TransitionNotAllowed(fromStatus, targetStatus);
        }

        var isSuspension = LifecycleTransitionPolicy.IsEmergencySuspension(fromStatus, targetStatus);
        var isReactivation = LifecycleTransitionPolicy.IsReactivation(fromStatus, targetStatus);

        if (isReactivation)
        {
            if (!string.Equals(assertedPermission, PermissionResolver.ReactivatePersonPermission, StringComparison.Ordinal))
            {
                throw PersonLifecycleError.PermissionRequired(PermissionResolver.ReactivatePersonPermission);
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw PersonLifecycleError.ReasonRequired();
            }
        }

        // THE COMMIT: called FIRST for every transition, suspension included - a REJECTED
        // request (e.g. a stale version) must never produce a revocation event for a suspension
        // that never happened. See class header "Suspension/revocation atomicity".
        var result = await gateway.ChangeStatusAsync(personId, targetStatus, version, actor.Id, actor.Type.ToWireString(), correlationId, cancellationToken);
        if (!result.Success)
        {
            throw TranslateGatewayError(result.ErrorCode, personId, version, fromStatus, targetStatus);
        }

        var updated = result.Updated!;
        var action = isSuspension ? "person_lifecycle.emergency_suspend" : isReactivation ? "person_lifecycle.reactivate" : "person_lifecycle.transition";
        var reasonCode = isSuspension ? "person_emergency_suspended" : isReactivation ? "person_reactivated" : "person_status_transitioned";

        if (isSuspension)
        {
            // BIO-IDN-004-QA-01 (Seyi round 4, High): MarkPendingAsync is the VERY FIRST thing
            // this method does after the commit, before EITHER of the two writes below, both of
            // which can throw. Marking pending only after the ordinary lifecycle audit write
            // meant an audit-sink outage aborted the method (as audit_unavailable) before the
            // pending record was ever created - the person was left SUSPENDED with no revocation
            // event AND no pending record for ReconcilePendingRevocationsAsync to find, the gap
            // undetectable and unrepairable. This record, unlike the person's own status, is
            // never overwritten by a later transition - see class header "Suspension/revocation
            // atomicity".
            var pending = new PendingSuspensionReconciliation
            {
                TenantId = tenantId,
                PersonId = personId,
                SuspendedAtVersion = updated.Version,
                Reason = reason,
                RequestedByActorId = actor.Id,
                CorrelationId = correlationId,
                RecordedAt = UtcInstant.Now(),
            };
            await pendingStore.MarkPendingAsync(pending, cancellationToken);
        }

        // The ordinary supplementary lifecycle event and (for a suspension) the revocation
        // publish are each attempted INDEPENDENTLY of the other's outcome - a failure in one
        // must never prevent the other from being attempted. If the revocation publish fails,
        // that error is surfaced with priority (it is AC02's own security-critical write); an
        // audit-write failure is only surfaced if the revocation publish (when applicable)
        // otherwise succeeded.
        PersonLifecycleError? auditError = null;
        try
        {
            await WriteLifecycleEventAsync(tenantId, actor, action, personId, reasonCode, correlationId, fromStatus, targetStatus, reason, cancellationToken);
        }
        catch (PersonLifecycleError ex)
        {
            auditError = ex;
        }

        if (isSuspension)
        {
            await PublishRevocationEventAsync(tenantId, personId, updated.Version, actor, correlationId, reason, cancellationToken);
            await pendingStore.ClearAsync(tenantId, personId, updated.Version, cancellationToken);
        }

        if (auditError is not null)
        {
            throw auditError;
        }

        return updated;
    }

    /// <summary>
    /// BIO-IDN-004-QA-01 (Seyi round 3): closes every outstanding pending suspension revocation
    /// for this person, regardless of their CURRENT status - reachable whether they are still
    /// SUSPENDED, have since been reactivated, re-suspended, archived, or anything else. Safe to
    /// call at any time, including when nothing is pending (a no-op) or repeatedly (idempotent
    /// per entry, via <see cref="ICredentialAccessRevocationEventSink.ExistsForSuspensionAsync"/>).
    /// </summary>
    public async Task<PersonSnapshot> ReconcilePendingRevocationsAsync(
        string personId, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

        var snapshot = await gateway.GetSnapshotAsync(personId, cancellationToken) ?? throw PersonLifecycleError.PersonNotFound(personId);
        var pending = await pendingStore.ListPendingAsync(tenantId, personId, cancellationToken);

        foreach (var entry in pending)
        {
            if (!await revocationSink.ExistsForSuspensionAsync(tenantId, personId, entry.SuspendedAtVersion, cancellationToken))
            {
                // A still-failing sink throws RevocationPendingReconciliation here, leaving any
                // earlier entries in this loop already cleared and this one (and any after it)
                // still durably pending for the next reconciliation attempt.
                await PublishRevocationEventAsync(tenantId, personId, entry.SuspendedAtVersion, actor, entry.CorrelationId, entry.Reason, cancellationToken);
            }

            await pendingStore.ClearAsync(tenantId, personId, entry.SuspendedAtVersion, cancellationToken);
        }

        return snapshot;
    }

    public Task<PersonSnapshot?> GetSnapshotAsync(string personId, CancellationToken cancellationToken = default)
    {
        RequireTenant();
        return gateway.GetSnapshotAsync(personId, cancellationToken);
    }

    /// <summary>BIO-IDN-004-QA-01 (Seyi round 3): the durable fact, never derived from the person's current status - see class header.</summary>
    public async Task<bool> HasPendingRevocationsAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var pending = await pendingStore.ListPendingAsync(tenantId, personId, cancellationToken);
        return pending.Count > 0;
    }

    /// <summary>BIO-IDN-004-AC04: unconditional - never filtered by the person's current status.</summary>
    public Task<IReadOnlyList<PersonLifecycleAuditEvent>> ListEventsAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return auditSink.ListByPersonAsync(tenantId, personId, cancellationToken);
    }

    private async Task WriteLifecycleEventAsync(
        string tenantId, AuditActor actor, string action, string personId, string reasonCode, string correlationId,
        PersonStatus fromStatus, PersonStatus toStatus, string? reason, CancellationToken cancellationToken)
    {
        var target = new AuditTarget("person", personId);
        var detail = new Dictionary<string, (object?, string)>
        {
            ["from_status"] = (fromStatus.ToWireString(), "internal"),
            ["to_status"] = (toStatus.ToWireString(), "internal"),
            ["reason"] = (reason, "internal"),
        };
        var auditEvent = PersonLifecycleAuditEventBuilder.Build(
            tenantId, actor, action, target, AuditOutcome.Allowed, reasonCode, correlationId, causationId: null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw PersonLifecycleError.AuditUnavailable();
        }

        if (!exists)
        {
            throw PersonLifecycleError.AuditUnavailable();
        }
    }

    private async Task PublishRevocationEventAsync(
        string tenantId, string personId, int suspendedAtVersion, AuditActor actor, string correlationId, string? reason, CancellationToken cancellationToken)
    {
        var revocationEvent = new CredentialAccessRevocationEvent
        {
            EventId = CanonicalId.NewEventId(),
            TenantId = tenantId,
            PersonId = personId,
            SuspendedAtVersion = suspendedAtVersion,
            ReasonCode = "person_emergency_suspended",
            Reason = reason,
            RequestedByActorId = actor.Id,
            CorrelationId = correlationId,
            OccurredAt = UtcInstant.Now(),
        };

        try
        {
            await revocationSink.PublishAsync(revocationEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await revocationSink.ExistsAsync(revocationEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw PersonLifecycleError.RevocationPendingReconciliation();
        }

        if (!exists)
        {
            throw PersonLifecycleError.RevocationPendingReconciliation();
        }
    }

    private static PersonLifecycleError TranslateGatewayError(
        string? errorCode, string personId, int expectedVersion, PersonStatus fromStatus, PersonStatus targetStatus) => errorCode switch
    {
        "person_not_found" => PersonLifecycleError.PersonNotFound(personId),
        "concurrency_conflict" => PersonLifecycleError.ConcurrencyConflict(personId, expectedVersion),
        "tenant_context_missing" => PersonLifecycleError.TenantContextMissing(),
        // "invalid_status_transition" from BIO-IDN-001's own table should be unreachable - this
        // feature's own LifecycleTransitionPolicy mirrors it and already rejected anything it
        // would reject, fail-fast, before the gateway was ever called (see class header). A
        // mismatch surfacing here means the two tables have drifted, not a real bypass.
        "invalid_status_transition" => PersonLifecycleError.TransitionNotAllowed(fromStatus, targetStatus),
        null => PersonLifecycleError.UnrecognisedGatewayError("(none)"),
        _ => PersonLifecycleError.UnrecognisedGatewayError(errorCode),
    };

    private string RequireTenant() => tenantProvider.TenantId ?? throw PersonLifecycleError.TenantContextMissing();

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw PersonLifecycleError.Validation(errors);
        }
    }

    private static PersonStatus ValidateTargetStatus(string? value, List<ValidationErrorItem> errors)
    {
        if (!PersonStatusExtensions.TryParse(value, out var status))
        {
            errors.Add(new ValidationErrorItem(
                "target_status", "invalid_format",
                "target_status must be one of PRE_ENROLLED, VERIFIED, ACTIVE, SUSPENDED, EXPIRED, FORMER_ALUMNI, REVOKED, ARCHIVED"));
        }

        return status;
    }

    private static int ValidateVersion(int? value, List<ValidationErrorItem> errors)
    {
        if (value is not int version)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            return default;
        }

        return version;
    }

    private static string? ValidateReason(string? value, List<ValidationErrorItem> errors)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > MaxReasonLength)
        {
            errors.Add(new ValidationErrorItem("reason", "out_of_range", $"reason must be at most {MaxReasonLength} characters"));
            return null;
        }

        return trimmed;
    }
}
