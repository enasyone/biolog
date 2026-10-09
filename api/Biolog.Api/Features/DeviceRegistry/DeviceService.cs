using Biolog.Api.Audit;
using Biolog.Api.Identifiers;
using Biolog.Api.Tenancy;
using Biolog.Features.DeviceAdapters;

namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>
/// The device-registry business logic (BIO-DEV-001): reads the resolved tenant context
/// itself, rather than trusting a caller-supplied tenant id, and fails closed
/// (<see cref="TenantContextMissingException"/>) when none is resolved - the fail-closed
/// rule ADR-0002 Decision 2 requires applies to this feature exactly like every other
/// tenant-plane read or write. Every registered endpoint on this host hits that path
/// today, because no story has implemented real tenant resolution yet (BIO-FND-008); that
/// is the correct current behaviour, not a bug in this one.
/// </summary>
public sealed class DeviceService
{
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IDeviceRepository _repository;
    private readonly IAuditSink _auditSink;
    private readonly DeviceLockRegistry _lockRegistry;

    public DeviceService(
        ITenantContextAccessor tenantAccessor, IDeviceRepository repository, IAuditSink auditSink, DeviceLockRegistry lockRegistry)
    {
        _tenantAccessor = tenantAccessor;
        _repository = repository;
        _auditSink = auditSink;
        _lockRegistry = lockRegistry;
    }

    public Task<Device> RegisterAsync(DeviceRegistrationRequest request, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        // BIO-DEV-001-QA-04: validated here (the request boundary) AND again inside
        // InMemoryDeviceRepository.CreateAsync (the repository boundary) - see
        // CredentialReferenceValidator's own doc for why both call sites matter.
        CredentialReferenceValidator.Validate(request.CredentialReference);
        return _repository.CreateAsync(tenant, request, cancellationToken);
    }

    public Task<Device?> GetAsync(string deviceId, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        return _repository.GetAsync(tenant, deviceId, cancellationToken);
    }

    /// <summary>
    /// BIO-DEV-001-AC03: a status change is audited before this call returns success. The
    /// audit write is awaited, and its exception (if the sink is unavailable) propagates
    /// rather than being logged and swallowed - matching ADR-0002 Decision 5's "the audit
    /// sink is a hard dependency of the audited effect, not an optional side effect."
    ///
    /// <paramref name="actor"/> is accepted from the caller rather than derived from a
    /// verified session, because no auth story has shipped yet - the same "hook, not
    /// resolver" gap BIO-FND-008 already documents for tenant context, applied to actor
    /// identity. A future auth story replaces this parameter with a session-derived value
    /// with no change to the audit shape it feeds.
    ///
    /// BIO-DEV-001-QA-02, corrected: this method commits the audit event BEFORE the
    /// repository mutation, not after it with a rollback on failure. An earlier version of
    /// this fix committed the mutation first and rolled back on audit failure; Kemi's
    /// review (ADR-0003 Decision 7) found that pattern breaks under concurrent writers - a
    /// second writer can read the first writer's committed-but-unaudited value and build a
    /// legitimately-audited change on top of it before the first writer's rollback runs,
    /// permanently baking the never-audited value into a record whose own audit trail names
    /// only the second writer's delta, with no rollback ever able to detect or undo it.
    /// Auditing first removes the race by construction: nothing about this change is ever
    /// visible to another reader or writer until its own audit event already durably
    /// exists, so there is no committed-but-unaudited state for anyone to build on, and no
    /// rollback is ever needed.
    ///
    /// The device is looked up, and a stale <paramref name="expectedVersion"/> rejected,
    /// BEFORE the audit write - not merely before the repository commit - so the existing,
    /// already-correct behaviour is preserved: an operation against an unknown or
    /// cross-tenant device, or one racing an expected_version that is already stale at the
    /// time of the call, records no audit event at all (an operation that never had a real
    /// target to describe is not itself audit-worthy).
    ///
    /// BIO-DEV-001-QA-02, corrected a second time: auditing before the commit is not
    /// sufficient by itself. <see cref="IDeviceRepository.UpdateStatusAsync"/> re-checks the
    /// device's version independently, at its own call time - a second writer for the SAME
    /// device could still change the version in the gap between this method's pre-check and
    /// that call, so the eventual commit rejects a mutation whose audit event has ALREADY
    /// durably recorded <c>Outcome: Allowed</c>. That is a false success claim, not an
    /// honest attempt record, however narrow the window - an earlier version of this method
    /// documented that exact residual as acceptable, which Kemi/Seyi's identical finding on
    /// BIO-TEN-001 established it is not. <see cref="DeviceLockRegistry"/> now serializes
    /// the whole pre-check-audit-commit sequence per device id, so no second writer for the
    /// same device can even begin its own pre-check until this one's audit AND commit have
    /// both resolved - removing the race by construction rather than narrowing its window.
    ///
    /// BIO-DEV-001-QA-03: the lock closes the concurrent-WRITER race, but not a caller
    /// CANCELLATION arriving in the same gap. <see cref="IDeviceRepository.UpdateStatusAsync"/>
    /// checks <c>cancellationToken.ThrowIfCancellationRequested()</c> before its own
    /// compare-and-set; if the caller's token is cancelled after the audit write already
    /// succeeded but before that check runs, the repository throws
    /// <see cref="OperationCanceledException"/> without ever attempting the write - leaving
    /// the SAME false-success audit record the lock was built to prevent, just via
    /// cancellation instead of a second writer. Once the audit event has been durably
    /// recorded, this is the operation's point of no return: the repository call below
    /// deliberately uses <see cref="CancellationToken.None"/>, not the caller's token, so a
    /// cancellation request arriving after that point can no longer abandon the write it
    /// describes. Only a genuine fault (a real version conflict, an actual store failure)
    /// can stop it now - never a mere cancellation signal.
    ///
    /// ADR-0003 Decision 7, element 4 (Kemi, closing BIO-TEN-001's identical finding): a
    /// thrown <see cref="IAuditSink.RecordAsync"/> does not, by itself, prove the event was
    /// never durably recorded - a real store can commit a write and still throw on the
    /// acknowledgement (a timeout, a dropped connection after commit). Treating every throw
    /// as "definitely didn't happen" could leave a genuinely-recorded success event with no
    /// corresponding commit, from a cause <see cref="DeviceLockRegistry"/> cannot see (it
    /// serializes writers against each other, not the audit sink's own acknowledgement path).
    /// <see cref="AuditAsync"/> below resolves that ambiguity via <see cref="IAuditSink.ExistsAsync"/>
    /// before deciding, mirroring <c>TenantService.AuditAsync</c> (BIO-TEN-001) exactly.
    /// </summary>
    public async Task<Device?> ChangeStatusAsync(
        string deviceId,
        DeviceStatus newStatus,
        int expectedVersion,
        AuditActor actor,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();

        using (await _lockRegistry.AcquireAsync(deviceId, cancellationToken))
        {
            var current = await _repository.GetAsync(tenant, deviceId, cancellationToken);
            if (current is null)
            {
                return null;
            }

            if (current.Version != expectedVersion)
            {
                throw new DeviceConcurrencyConflictException(current.Version);
            }

            var now = DateTimeOffset.UtcNow;
            var auditEvent = new AuditEvent
            {
                EventId = CanonicalId.NewEventId(),
                SchemaVersion = 1,
                TenantId = tenant.TenantId,
                Actor = actor,
                Action = "device.status_changed",
                Target = new AuditTarget("device", deviceId),
                Outcome = AuditOutcome.Allowed,
                ReasonCode = "status_updated",
                CorrelationId = CanonicalId.IsWellFormed(correlationId, "evt") ? correlationId! : CanonicalId.NewEventId(),
                CausationId = null,
                OccurredAt = now,
                ReceivedAt = now,
                Classification = "internal",
            };

            await AuditAsync(auditEvent, cancellationToken);

            // Now provably safe from a concurrent writer: nothing else could have changed
            // this device's version since the pre-check above, because nothing else could
            // enter this block for the same device id while this writer holds the lock.
            // CancellationToken.None is deliberate (QA-03): the audit event already exists,
            // so this write must not be abandoned by a mere cancellation request.
            return await _repository.UpdateStatusAsync(tenant, deviceId, newStatus, expectedVersion, CancellationToken.None);
        }
    }

    /// <summary>
    /// BIO-DEV-004-AC01: records a health-poll snapshot. Deliberately mirrors
    /// <see cref="ChangeStatusAsync"/>'s own audit-before-commit, per-device-locked,
    /// pre-check-then-<see cref="CancellationToken.None"/>-after-the-audit-write discipline
    /// exactly - a health snapshot is a distinct write (BIO-DEV-004) from a lifecycle status
    /// change (BIO-DEV-001), but the SAME concurrent-writer/false-success/cancellation hazards
    /// apply to it for the identical reasons documented on that method, so the identical fix
    /// shape is reused rather than re-derived.
    /// </summary>
    public async Task<Device?> RecordHeartbeatAsync(
        string deviceId,
        DeviceHealthStatus health,
        string? detail,
        DateTimeOffset checkedAt,
        int expectedVersion,
        AuditActor actor,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();

        using (await _lockRegistry.AcquireAsync(deviceId, cancellationToken))
        {
            var current = await _repository.GetAsync(tenant, deviceId, cancellationToken);
            if (current is null)
            {
                return null;
            }

            if (current.Version != expectedVersion)
            {
                throw new DeviceConcurrencyConflictException(current.Version);
            }

            var now = DateTimeOffset.UtcNow;
            var auditEvent = new AuditEvent
            {
                EventId = CanonicalId.NewEventId(),
                SchemaVersion = 1,
                TenantId = tenant.TenantId,
                Actor = actor,
                Action = "device.heartbeat_recorded",
                Target = new AuditTarget("device", deviceId),
                Outcome = AuditOutcome.Allowed,
                ReasonCode = "health_recorded",
                CorrelationId = CanonicalId.IsWellFormed(correlationId, "evt") ? correlationId! : CanonicalId.NewEventId(),
                CausationId = null,
                OccurredAt = now,
                ReceivedAt = now,
                Classification = "internal",
            };

            await AuditAsync(auditEvent, cancellationToken);

            // CancellationToken.None is deliberate, same reasoning as ChangeStatusAsync: the
            // audit event already durably exists, so a mere cancellation request arriving
            // after this point must not abandon the write it describes.
            return await _repository.RecordHeartbeatAsync(
                tenant, deviceId, health, detail, checkedAt, expectedVersion, CancellationToken.None);
        }
    }

    /// <summary>BIO-DEV-004-AC01/AC05: every device visible to the resolved tenant. A read,
    /// so no lock/audit - matches <see cref="GetAsync"/>'s own shape.</summary>
    public Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        return _repository.ListAsync(tenant, cancellationToken);
    }

    /// <summary>
    /// Durably records <paramref name="auditEvent"/>, resolving an ambiguous
    /// <see cref="IAuditSink.RecordAsync"/> throw via <see cref="IAuditSink.ExistsAsync"/>
    /// before treating it as a real failure (ADR-0003 Decision 7, element 4; see
    /// <see cref="ChangeStatusAsync"/>'s doc for why). Confirmed-present is treated as
    /// success (the caller's own lock-protected commit then makes the event's claim true);
    /// confirmed-absent, or a verification call that itself throws (a total audit-store
    /// outage), throws <see cref="AuditUnavailableException"/> with nothing committed.
    /// </summary>
    private async Task AuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _auditSink.RecordAsync(auditEvent, cancellationToken);
            return; // confirmed: the write completed without throwing
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await _auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new AuditUnavailableException(
                "the audit sink failed and could not be verified; the status change was not committed", ex);
        }

        if (!exists)
        {
            throw new AuditUnavailableException(
                "the audit sink failed and no matching event was found; the status change was not committed");
        }

        // else: confirmed present despite the throw on RecordAsync - proceed as a success.
    }

    private TenantContext RequireTenant() => _tenantAccessor.Context ?? throw new TenantContextMissingException();
}
