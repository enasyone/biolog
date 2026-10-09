using Biolog.Features.BiometricRetention.Audit;
using Biolog.Features.BiometricRetention.Domain;

namespace Biolog.Features.BiometricRetention.Service;

/// <summary>
/// BIO-BIO-009-AC01/AC03: configures per-tenant/category retention and raw-image-retention
/// authorisation. Every mutating method implements the same ADR-0003 Decision 7 pattern as
/// <see cref="BiometricDeletionService"/> - audit-before-commit, <see cref="IBiometricRetentionAuditSink.ExistsAsync"/>
/// verification on an ambiguous throw, <see cref="CancellationToken.None"/> past the confirmed-audit
/// point. A read (<see cref="GetRetentionPolicyAsync"/>, <see cref="IsRawImageRetentionAuthorisedAsync"/>)
/// never touches the lock or the audit sink - nothing is mutated, nothing to make atomic.
///
/// <b>Permission gating lives entirely at the HTTP endpoint layer, not here.</b> ADR-0006
/// Decision 2/3: this is an authorization-decision use of the asserted-actor accommodation, so it
/// requires a verified provenance proof (<c>Api/ActorProvenanceGate.cs</c>) plus a real RBAC
/// grant lookup (<c>Domain/IPermissionGateway.cs</c> wrapping BIO-IDN-006's
/// <c>PermissionService.ActorHasPermissionAsync</c>) BEFORE this service is ever called - the
/// same shape BIO-MOB-005's own <c>EnrolmentPackageService.SetCapturePolicyAsync</c> already
/// uses (zero permission parameter on the service method itself; the endpoint handler gates).
/// By the time a caller reaches here, authorization has already been decided.
///
/// Internal, like every prior BIO-BIO service - reachable only from this assembly, Biolog.Api
/// (DI registration, via this project's own <c>InternalsVisibleTo</c>) and this feature's own
/// test project.
/// </summary>
internal sealed class RetentionPolicyService(
    IRetentionPolicy retentionPolicy,
    IRawImageRetentionPolicy rawImagePolicy,
    IBiometricRetentionAuditSink auditSink,
    BiometricRetentionLockRegistry lockRegistry,
    Func<string>? newId = null,
    Func<string>? nowUtc = null)
{
    private readonly Func<string> _newId = newId ?? (() => Guid.NewGuid().ToString());
    private readonly Func<string> _nowUtc = nowUtc ?? (() => DateTime.UtcNow.ToString("O"));

    public Task<RetentionPolicySetting> GetRetentionPolicyAsync(
        string tenantId, RetentionCategory category, CancellationToken cancellationToken) =>
        retentionPolicy.GetAsync(tenantId, category, cancellationToken);

    public Task<bool> IsRawImageRetentionAuthorisedAsync(string tenantId, CancellationToken cancellationToken) =>
        rawImagePolicy.IsAuthorisedAsync(tenantId, cancellationToken);

    /// <summary>AC01. Throws <see cref="BiometricRetentionException.InvalidRetentionDays"/> for a
    /// non-positive value, before any lock is taken or anything audited.</summary>
    public async Task SetRetentionPolicyAsync(
        string tenantId, RetentionCategory category, int retentionDays, string actorId, CancellationToken cancellationToken)
    {
        if (retentionDays <= 0)
        {
            throw BiometricRetentionException.InvalidRetentionDays(retentionDays);
        }

        var lockKey = $"policy|{tenantId}|{category}";
        using var _ = await lockRegistry.AcquireAsync(lockKey, cancellationToken);

        var now = _nowUtc();
        await AuditAsync(tenantId, "biometric_retention.policy_updated", category.ToString(), $"retentionDays={retentionDays}", actorId, now, cancellationToken);

        await retentionPolicy.SetAsync(tenantId, category, retentionDays, CancellationToken.None);
    }

    public async Task SetRawImageRetentionAuthorisedAsync(string tenantId, bool authorised, string actorId, CancellationToken cancellationToken)
    {
        var lockKey = $"raw-image|{tenantId}";
        using var _ = await lockRegistry.AcquireAsync(lockKey, cancellationToken);

        var now = _nowUtc();
        var action = authorised ? "biometric_retention.raw_image_authorised" : "biometric_retention.raw_image_revoked";
        await AuditAsync(tenantId, action, tenantId, reason: null, actorId, now, cancellationToken);

        if (authorised)
        {
            await rawImagePolicy.AuthoriseAsync(tenantId, CancellationToken.None);
        }
        else
        {
            await rawImagePolicy.RevokeAsync(tenantId, CancellationToken.None);
        }
    }

    /// <summary>ADR-0003 Decision 7, element 4: resolves an ambiguous
    /// <see cref="IBiometricRetentionAuditSink.WriteAsync"/> throw via
    /// <see cref="IBiometricRetentionAuditSink.ExistsAsync"/> before treating it as a real
    /// failure, rather than assuming the write never happened.</summary>
    private async Task AuditAsync(
        string tenantId, string action, string targetId, string? reason, string actorId, string nowUtc,
        CancellationToken cancellationToken)
    {
        var eventId = _newId();
        var auditEvent = new BiometricRetentionAuditEvent
        {
            EventId = eventId,
            TenantId = tenantId,
            Action = action,
            TargetId = targetId,
            Reason = reason,
            ActorId = actorId,
            OccurredAtUtc = nowUtc,
        };

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
            exists = await auditSink.ExistsAsync(eventId, cancellationToken);
        }
        catch (Exception)
        {
            throw BiometricRetentionException.AuditUnavailable("verification could not confirm durability.");
        }

        if (!exists)
        {
            throw BiometricRetentionException.AuditUnavailable("the event was not found on verification.");
        }
    }
}
