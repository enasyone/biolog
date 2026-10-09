using Biolog.Api.Audit;
using Biolog.Api.Features.DeviceCertification.Audit;
using Biolog.Api.Identifiers;
using Biolog.Api.Tenancy;

namespace Biolog.Api.Features.DeviceCertification;

/// <summary>
/// The device certification matrix business logic (BIO-DEV-002). Two independent scopes
/// share the same storage/lock/audit machinery but are exposed through separate methods
/// so each boundary is provable on its own:
///
/// - The platform-wide default (<c>tenantId: null</c>) - "As platform operations" per the
///   user story, control-plane, no tenant context required, the same shape
///   <c>BioTen001Module</c>'s own Tenant CRUD already uses for an operations action that
///   is not itself a tenant-plane read or write.
/// - A tenant's own override (BIO-DEV-002-AC03) - requires a resolved
///   <see cref="Tenancy.TenantContext"/>, fails closed exactly like
///   <see cref="Features.DeviceRegistry.DeviceService"/> does for every device operation
///   (ADR-0002 Decision 2).
///
/// Concurrency, audit-before-commit, ambiguous-audit-write verification and
/// point-of-no-return cancellation handling all mirror
/// <see cref="Features.DeviceRegistry.DeviceService.ChangeStatusAsync"/>/<c>AuditAsync</c>
/// line-for-line in structure - the same proven, twice-reviewed shape (BIO-DEV-001
/// QA-02/QA-03, ADR-0003 Decision 7 element 4), applied to a second aggregate type in this
/// same codebase rather than re-derived from scratch.
/// </summary>
public sealed class DeviceCertificationService
{
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IDeviceCertificationRepository _repository;
    private readonly ICertificationAuditSink _auditSink;
    private readonly DeviceCertificationLockRegistry _lockRegistry;

    public DeviceCertificationService(
        ITenantContextAccessor tenantAccessor,
        IDeviceCertificationRepository repository,
        ICertificationAuditSink auditSink,
        DeviceCertificationLockRegistry lockRegistry)
    {
        _tenantAccessor = tenantAccessor;
        _repository = repository;
        _auditSink = auditSink;
        _lockRegistry = lockRegistry;
    }

    public Task<DeviceCertification> RegisterGlobalAsync(
        DeviceCertificationRegistrationRequest request, CancellationToken cancellationToken)
        => RegisterAsync(tenantId: null, request, cancellationToken);

    /// <summary>BIO-DEV-002-AC03: registers this tenant's own override for a
    /// vendor+model+firmware+sdk combination. Fails closed with
    /// <see cref="TenantContextMissingException"/> when no tenant is resolved.</summary>
    public Task<DeviceCertification> RegisterTenantOverrideAsync(
        DeviceCertificationRegistrationRequest request, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        return RegisterAsync(tenant.TenantId, request, cancellationToken);
    }

    public Task<DeviceCertification?> UpdateGlobalAsync(
        DeviceCertificationStatusChangeRequest request, CancellationToken cancellationToken)
        => UpdateAsync(tenantId: null, request, cancellationToken);

    /// <summary>BIO-DEV-002-AC03/AC04: updates this tenant's own override. Fails closed
    /// with <see cref="TenantContextMissingException"/> when no tenant is resolved.</summary>
    public Task<DeviceCertification?> UpdateTenantOverrideAsync(
        DeviceCertificationStatusChangeRequest request, CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        return UpdateAsync(tenant.TenantId, request, cancellationToken);
    }

    /// <summary>The platform-wide default entry for this key, regardless of any tenant
    /// context - used by admin tooling that needs to see the base matrix itself, not the
    /// effective view any one tenant would see.</summary>
    public Task<DeviceCertification?> GetGlobalAsync(DeviceCertificationKey key, CancellationToken cancellationToken)
    {
        DeviceCertificationKeyValidator.Validate(key);
        return _repository.GetAsync(null, key, cancellationToken);
    }

    /// <summary>BIO-DEV-002-AC03: the effective status for the CALLING tenant, if one is
    /// resolved - its own override if it has registered one, otherwise the platform-wide
    /// default. A caller with no resolved tenant (no host channel match) sees only the
    /// platform-wide default - this lookup is deliberately not fail-closed on a missing
    /// tenant the way <see cref="Features.DeviceRegistry.DeviceService"/> is, because the
    /// platform-wide matrix is control-plane data every caller may read (BIO-DEV-002-AC02:
    /// "visible to admins", not "visible only inside a resolved tenant").</summary>
    public async Task<DeviceCertification?> GetEffectiveAsync(DeviceCertificationKey key, CancellationToken cancellationToken)
    {
        DeviceCertificationKeyValidator.Validate(key);

        var tenant = _tenantAccessor.Context;
        if (tenant is not null)
        {
            var overrideEntry = await _repository.GetAsync(tenant.TenantId, key, cancellationToken);
            if (overrideEntry is not null)
            {
                return overrideEntry;
            }
        }

        return await _repository.GetAsync(null, key, cancellationToken);
    }

    /// <summary>BIO-DEV-002-AC02: the full platform-wide matrix, so an admin can see every
    /// combination - including Unsupported ones - nothing is hidden by default.</summary>
    public Task<IReadOnlyList<DeviceCertification>> ListGlobalAsync(CancellationToken cancellationToken)
        => _repository.ListAsync(null, cancellationToken);

    /// <summary>The calling tenant's own overrides only (BIO-DEV-002-AC03) - not the
    /// merged effective view, so a tenant admin can see exactly what they have overridden
    /// versus what falls through to the platform-wide default. Fails closed with
    /// <see cref="TenantContextMissingException"/> when no tenant is resolved.</summary>
    public Task<IReadOnlyList<DeviceCertification>> ListTenantOverridesAsync(CancellationToken cancellationToken)
    {
        var tenant = RequireTenant();
        return _repository.ListAsync(tenant.TenantId, cancellationToken);
    }

    private async Task<DeviceCertification> RegisterAsync(
        string? tenantId, DeviceCertificationRegistrationRequest request, CancellationToken cancellationToken)
    {
        var key = request.Key;
        DeviceCertificationKeyValidator.Validate(key);
        ValidateTestEvidenceReference(request.TestEvidenceReference);

        using (await _lockRegistry.AcquireAsync(LockKey(tenantId, key), cancellationToken))
        {
            // An attempt against a combination that already has an entry in this exact
            // scope is not audit-worthy as a "registered" event - it never had a real
            // effect to describe (mirrors DeviceService: "an operation against an unknown
            // or cross-tenant device records no audit event at all").
            if (await _repository.GetAsync(tenantId, key, cancellationToken) is not null)
            {
                throw new DeviceCertificationAlreadyExistsException(key.Vendor, key.Model, key.Firmware, key.Sdk);
            }

            var now = DateTimeOffset.UtcNow;
            var auditEvent = new CertificationAuditEvent
            {
                EventId = CanonicalId.NewEventId(),
                SchemaVersion = 1,
                TenantId = tenantId,
                Actor = new CertificationAuditActor(request.ActorType, request.ActorId),
                Action = "device_certification.registered",
                Target = new CertificationAuditTarget(TargetType(tenantId), key.Composite),
                Outcome = CertificationAuditOutcome.Allowed,
                ReasonCode = "certification_registered",
                CorrelationId = CanonicalId.IsWellFormed(request.CorrelationId, "evt") ? request.CorrelationId! : CanonicalId.NewEventId(),
                CausationId = null,
                OccurredAt = now,
                ReceivedAt = now,
                Classification = "internal",
            };

            await AuditAsync(auditEvent, cancellationToken);

            // Provably the only writer for this key+scope right now (the lock above), and
            // just confirmed absent under that same lock. CancellationToken.None past this
            // point for the same reason DeviceService uses it past its own audit write:
            // the audit event already durably exists, so this create must not be
            // abandoned by a mere cancellation request.
            return await _repository.CreateAsync(tenantId, request, CancellationToken.None);
        }
    }

    private async Task<DeviceCertification?> UpdateAsync(
        string? tenantId, DeviceCertificationStatusChangeRequest request, CancellationToken cancellationToken)
    {
        var key = request.Key;
        DeviceCertificationKeyValidator.Validate(key);
        ValidateTestEvidenceReference(request.TestEvidenceReference);

        using (await _lockRegistry.AcquireAsync(LockKey(tenantId, key), cancellationToken))
        {
            var current = await _repository.GetAsync(tenantId, key, cancellationToken);
            if (current is null)
            {
                return null;
            }

            if (current.Version != request.ExpectedVersion)
            {
                throw new DeviceCertificationConcurrencyConflictException(current.Version);
            }

            var now = DateTimeOffset.UtcNow;
            var auditEvent = new CertificationAuditEvent
            {
                EventId = CanonicalId.NewEventId(),
                SchemaVersion = 1,
                TenantId = tenantId,
                Actor = new CertificationAuditActor(request.ActorType, request.ActorId),
                Action = "device_certification.updated",
                Target = new CertificationAuditTarget(TargetType(tenantId), key.Composite),
                Outcome = CertificationAuditOutcome.Allowed,
                ReasonCode = "certification_updated",
                CorrelationId = CanonicalId.IsWellFormed(request.CorrelationId, "evt") ? request.CorrelationId! : CanonicalId.NewEventId(),
                CausationId = null,
                OccurredAt = now,
                ReceivedAt = now,
                Classification = "internal",
            };

            await AuditAsync(auditEvent, cancellationToken);

            // Same point-of-no-return reasoning as RegisterAsync/DeviceService.ChangeStatusAsync:
            // the audit event already exists, so a mere cancellation must not abandon it.
            return await _repository.UpdateAsync(
                tenantId, key, request.Status, request.TestEvidenceReference, request.ExpectedVersion, CancellationToken.None);
        }
    }

    /// <summary>
    /// Durably records <paramref name="auditEvent"/>, resolving an ambiguous
    /// <see cref="ICertificationAuditSink.RecordAsync"/> throw via
    /// <see cref="ICertificationAuditSink.ExistsAsync"/> before treating it as a real
    /// failure (ADR-0003 Decision 7, element 4). Mirrors
    /// <see cref="Features.DeviceRegistry.DeviceService.AuditAsync"/> exactly.
    /// </summary>
    private async Task AuditAsync(CertificationAuditEvent auditEvent, CancellationToken cancellationToken)
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
                "the audit sink failed and could not be verified; the certification change was not committed", ex);
        }

        if (!exists)
        {
            throw new AuditUnavailableException(
                "the audit sink failed and no matching event was found; the certification change was not committed");
        }

        // else: confirmed present despite the throw on RecordAsync - proceed as a success.
    }

    private static void ValidateTestEvidenceReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidTestEvidenceReferenceException("test evidence reference must not be empty or whitespace-only");
        }

        if (value.Length > 512)
        {
            throw new InvalidTestEvidenceReferenceException("test evidence reference must not exceed 512 characters");
        }
    }

    private TenantContext RequireTenant() => _tenantAccessor.Context ?? throw new TenantContextMissingException();

    private static string TargetType(string? tenantId) =>
        tenantId is null ? "device_certification" : "device_certification_override";

    private static string LockKey(string? tenantId, DeviceCertificationKey key) =>
        $"{tenantId ?? "*global*"}::{key.Composite}";
}
