using System.Text.RegularExpressions;
using Biolog.Features.Rbac.Api;
using Biolog.Features.Rbac.Audit;
using Biolog.Features.Rbac.Domain;
using Biolog.Features.Rbac.Identifiers;
using Biolog.Features.Rbac.Tenancy;

namespace Biolog.Features.Rbac;

/// <summary>
/// Application service for BIO-IDN-006-AC02: which <see cref="RolePermissionGrant"/>s a role
/// carries in the resolved tenant, and <see cref="HasPermissionAsync"/> - the REAL permission
/// verification surface BIO-IDN-004's own <c>PermissionResolver</c> already documented itself as
/// waiting for ("real verification of the CLAIM is deferred to BIO-IDN-006"). This story does
/// not itself wire that verification into BIO-IDN-004 or any other sibling story's endpoints -
/// see docs/features/bio-idn-006/README.md "Deliberately out of scope" for why that integration
/// is left as a future, disclosed step (mirroring the existence-gateway pattern already proven
/// for BIO-IDN-002/003/004/005) rather than retrofitted here.
///
/// Implements the same audit-atomicity discipline as <see cref="RoleAssignmentService"/> for its
/// one mutation (<see cref="SetGrantAsync"/>); <see cref="PermissionGrantLockRegistry"/>'s
/// lock is held only across that write, since <see cref="IRolePermissionGrantRepository.UpsertAsync"/>
/// is unconditional (see that interface's header).
///
/// <b>BIO-IDN-006-QA-03 (Seyi round 2, Critical).</b> <see cref="SetGrantAsync"/> now requires
/// the calling actor to hold <c>"rbac.permission_grant_manage"</c> before configuring a grant for
/// any role - round 1's fix rejected configuring <c>SUPER_ADMIN</c> itself, but performed NO
/// authorization check for every OTHER role, so an unprivileged caller could grant an arbitrary
/// (non-`SUPER_ADMIN`) role any permission key at all, including a high-impact one - the exact
/// same escalation class as leaving role assignment unguarded, just one hop removed. See
/// <see cref="RoleAssignmentService"/>'s header for the parallel fix on assignment itself and the
/// full round 2 history.
///
/// <see cref="ActorHasPermissionAsync"/> is the internal, non-throwing counterpart
/// <see cref="RoleAssignmentService"/> and <see cref="MakerCheckerService"/> both call for their
/// own authorization gates - malformed or nonexistent actor ids simply report "not granted",
/// never an exception, since those callers need a clean authorized/unauthorized signal, not a
/// validation error (the public <see cref="HasPermissionAsync"/> keeps throwing for those cases,
/// since ITS callers - external API consumers checking a specific person's permissions - do want
/// a distinguishable validation/not-found signal).
/// </summary>
internal sealed partial class PermissionService(
    ICurrentTenantProvider tenantProvider,
    IRolePermissionGrantRepository grantRepository,
    IPersonRoleAssignmentRepository assignmentRepository,
    IRbacAuditSink auditSink,
    PermissionGrantLockRegistry lockRegistry,
    IPersonExistenceGateway personGateway)
{
    /// <summary>BIO-IDN-006-QA-03: required to configure a grant for any (non-SUPER_ADMIN) role.</summary>
    public const string PermissionGrantManagePermission = "rbac.permission_grant_manage";

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex PermissionKeyPattern();

    /// <summary>AC02's real enforcement point. SUPER_ADMIN is never configurable - see RoleType's and RolePermissionGrant's headers - so this rejects that attempt before the lock is even acquired. BIO-IDN-006-QA-03: every OTHER role's grant also now requires the caller to hold PermissionGrantManagePermission.</summary>
    public async Task<RolePermissionGrant> SetGrantAsync(
        RoleType role, string permissionKey, SetPermissionGrantRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (role == RoleType.SuperAdmin)
        {
            throw RbacError.SuperAdminGrantNotConfigurable();
        }

        if (!await ActorHasPermissionAsync(tenantId, actor.Id, PermissionGrantManagePermission, cancellationToken))
        {
            throw RbacError.PermissionRequired(PermissionGrantManagePermission);
        }

        var errors = new List<ValidationErrorItem>();
        var key = ValidatePermissionKey(permissionKey, errors);
        if (request.Granted is not bool granted)
        {
            errors.Add(new ValidationErrorItem("granted", "required", "granted is required"));
            granted = default;
        }

        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, role, key!, cancellationToken);
        var current = await grantRepository.GetAsync(tenantId, role, key!, cancellationToken);
        var now = UtcInstant.Now();
        var updated = new RolePermissionGrant(tenantId, role, key!)
        {
            Granted = granted, Version = (current?.Version ?? 0) + 1, CreatedAt = current?.CreatedAt ?? now, UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "rbac.permission_grant_set", role, key!, granted, correlationId, cancellationToken);
        return await grantRepository.UpsertAsync(updated, CancellationToken.None);
    }

    public Task<RolePermissionGrant?> GetGrantAsync(RoleType role, string permissionKey, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return grantRepository.GetAsync(tenantId, role, permissionKey, cancellationToken);
    }

    /// <summary>
    /// The real permission-verification surface. Proves <paramref name="personId"/> exists in
    /// the resolved tenant (applying BIO-IDN-002-QA-01's lesson even to a read - a silent
    /// "not granted" for a mistyped or foreign-tenant person id would be a dangerous masking
    /// failure mode for a security check), then resolves every ACTIVE role the person holds and
    /// checks whether any of them carries this permission (SUPER_ADMIN short-circuits to
    /// granted, via every role it appears as - see RoleType's header).
    /// </summary>
    public async Task<(bool Granted, IReadOnlyList<RoleType> ViaRoles)> HasPermissionAsync(
        string personId, string permissionKey, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!CanonicalId.IsPersonId(personId))
        {
            throw RbacError.Validation([new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier")]);
        }

        if (!await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw RbacError.PersonNotFound(personId);
        }

        return await ResolvePermissionAsync(tenantId, personId, permissionKey, cancellationToken);
    }

    /// <summary>
    /// BIO-IDN-006-QA-03's internal counterpart to <see cref="HasPermissionAsync"/>: never throws
    /// for a malformed or nonexistent actor id, simply reports "not granted" - the correct signal
    /// for an authorization GATE (as opposed to a validation endpoint), where a caller who isn't
    /// even a real, resolvable person is, definitionally, not authorized.
    /// </summary>
    internal async Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default)
    {
        if (!CanonicalId.IsPersonId(actorId) || !await personGateway.ExistsAsync(actorId, cancellationToken))
        {
            return false;
        }

        var (granted, _) = await ResolvePermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
        return granted;
    }

    private async Task<(bool Granted, IReadOnlyList<RoleType> ViaRoles)> ResolvePermissionAsync(
        string tenantId, string personId, string permissionKey, CancellationToken cancellationToken)
    {
        var activeRoles = await assignmentRepository.ListActiveForPersonAsync(tenantId, personId, cancellationToken);
        if (activeRoles.Any(a => a.Role == RoleType.SuperAdmin))
        {
            return (true, [RoleType.SuperAdmin]);
        }

        var viaRoles = new List<RoleType>();
        foreach (var assignment in activeRoles)
        {
            var grant = await grantRepository.GetAsync(tenantId, assignment.Role, permissionKey, cancellationToken);
            if (grant?.Granted == true)
            {
                viaRoles.Add(assignment.Role);
            }
        }

        return (viaRoles.Count > 0, viaRoles);
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, RoleType role, string permissionKey, bool granted, string correlationId, CancellationToken cancellationToken)
    {
        var target = new AuditTarget("role_permission_grant", $"{role.ToWireString()}/{permissionKey}");
        var detail = new Dictionary<string, (object?, string)>
        {
            ["role"] = (role.ToWireString(), "internal"), ["permission_key"] = (permissionKey, "internal"), ["granted"] = (granted, "internal"),
        };
        var auditEvent = RbacAuditEventBuilder.Build(
            tenantId, actor, action, target, AuditOutcome.Allowed, granted ? "permission_granted" : "permission_revoked", correlationId, causationId: null, detail,
            withheld: []);

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

    private static string? ValidatePermissionKey(string? value, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem("permission_key", "required", "permission_key is required"));
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 100 || !PermissionKeyPattern().IsMatch(trimmed))
        {
            errors.Add(new ValidationErrorItem("permission_key", "invalid_format", "permission_key must be a namespaced verb like 'person.reactivate', at most 100 characters"));
            return null;
        }

        return trimmed;
    }
}
